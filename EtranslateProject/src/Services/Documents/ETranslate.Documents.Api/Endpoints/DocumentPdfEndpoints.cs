using System.Security.Cryptography;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Documents.Api.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    private static async Task<IResult> CreateDraftPdfAsync(Guid tenantId, Guid translationJobId, Guid documentId, int revisionNumber,
        HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database,
        IDocumentBlobStore blobStore, IDraftPdfRenderer renderer, TimeProvider timeProvider, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await database.TranslationDocuments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == documentId &&
            item.TenantId == tenantId && item.TranslationJobId == translationJobId, cancellationToken);
        if (document is null) return Results.NotFound();
        var draft = await database.DraftRevisions.AsNoTracking().SingleOrDefaultAsync(item => item.DocumentId == documentId &&
            item.RevisionNumber == revisionNumber, cancellationToken);
        if (draft is null) return Results.NotFound();
        var existing = await database.PdfVersions.AsNoTracking().SingleOrDefaultAsync(item => item.DraftRevisionId == draft.Id &&
            item.RendererVersion == DraftPdfPolicy.RendererVersion, cancellationToken);
        if (existing is not null) return Results.Ok(ToPdfResponse(existing));
        var template = document.TemplateRevisionId is null ? null : await (
            from revision in database.TemplateRevisions.AsNoTracking()
            join parent in database.DocumentTemplates.AsNoTracking() on revision.TemplateId equals parent.Id
            where revision.Id == document.TemplateRevisionId && parent.TenantId == tenantId
            select revision).SingleOrDefaultAsync(cancellationToken);
        if (document.TemplateRevisionId is not null && template is null) return Results.Conflict(new { error = "pinned_template_unavailable" });
        byte[] bytes;
        try
        {
            var ids = DraftPdfHtml.ImageReferences(draft.EditorContentJson, "doc")
                .Concat(DraftPdfHtml.ImageReferences(template?.HeaderContentJson, "header"))
                .Concat(DraftPdfHtml.ImageReferences(template?.FooterContentJson, "footer")).Distinct().ToArray();
            if (ids.Length > DraftPdfPolicy.MaximumImages) throw new PdfRenderValidationException("Too many managed images for a draft PDF.");
            var assets = template is null ? [] : await database.TemplateAssets.AsNoTracking()
                .Where(asset => asset.TemplateId == template.TemplateId && ids.Contains(asset.Id)).ToListAsync(cancellationToken);
            if (assets.Count != ids.Length || assets.Sum(asset => asset.SizeBytes) > DraftPdfPolicy.MaximumImageBytes)
                throw new PdfRenderValidationException("Images are missing, outside the pinned template, or too large.");
            var images = new Dictionary<Guid, PdfImage>();
            foreach (var asset in assets)
            {
                await using var stream = await blobStore.OpenReadAsync(asset.StorageKey, cancellationToken);
                if (stream is null) throw new PdfRenderValidationException("A template image is unavailable.");
                var image = await ReadBoundedAsync(stream, 5 * 1024 * 1024, cancellationToken);
                if (image.LongLength != asset.SizeBytes || Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant() != asset.Sha256 ||
                    !SourceFilePolicy.HasValidSignature(image.AsSpan(0, Math.Min(image.Length, SourceFilePolicy.MaximumSignatureLength)), asset.ContentType))
                    throw new PdfRenderValidationException("Template image integrity could not be verified.");
                images.Add(asset.Id, new PdfImage(asset.ContentType, image));
            }
            bytes = await renderer.RenderAsync(new DraftPdfInput(document.Id, draft.RevisionNumber, document.TemplateRevisionId,
                draft.EditorContentJson, template?.HeaderContentJson, template?.FooterContentJson, template?.PageLayoutJson, template?.WatermarkJson, images), cancellationToken);
        }
        catch (PdfRenderValidationException error) { return Results.Problem(statusCode: 422, title: "draft_pdf_not_supported", detail: error.Message); }
        catch (PdfRendererBusyException) { httpContext.Response.Headers.RetryAfter = "2"; return Results.Problem(statusCode: 429, title: "draft_pdf_renderer_busy"); }
        catch (PdfRendererUnavailableException error)
        {
            loggers.CreateLogger("DraftPdf").LogWarning(error, "Draft PDF rendering failed for document {DocumentId}, revision {RevisionNumber}", documentId, revisionNumber);
            return Results.Problem(statusCode: 503, title: "draft_pdf_renderer_unavailable");
        }
        var key = $"{tenantId:N}/{documentId:N}/draft-pdfs/{Guid.NewGuid():N}.pdf";
        await using var source = new MemoryStream(bytes, writable: false);
        var stored = await blobStore.SaveAsync(key, source, "application/pdf", DraftPdfPolicy.MaximumBytes, cancellationToken);
        var version = new DocumentPdfVersion(document.Id, draft.Id, draft.RevisionNumber, document.TemplateRevisionId,
            DraftPdfPolicy.RendererVersion, key, stored.SizeBytes, stored.Sha256, access.Actor!.UserId, timeProvider.GetUtcNow());
        try
        {
            database.PdfVersions.Add(version);
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await blobStore.DeleteIfExistsAsync(key, CancellationToken.None);
            existing = await database.PdfVersions.AsNoTracking().SingleOrDefaultAsync(item => item.DraftRevisionId == draft.Id &&
                item.RendererVersion == DraftPdfPolicy.RendererVersion, cancellationToken);
            if (existing is null) throw;
            return Results.Ok(ToPdfResponse(existing));
        }
        catch { await blobStore.DeleteIfExistsAsync(key, CancellationToken.None); throw; }
        return Results.Created($"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/pdfs/{version.Id}", ToPdfResponse(version));
    }

    private static async Task<IResult> GetDraftPdfsAsync(Guid tenantId, Guid translationJobId, Guid documentId,
        HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        if (!await database.TranslationDocuments.AnyAsync(item => item.Id == documentId && item.TenantId == tenantId &&
            item.TranslationJobId == translationJobId, cancellationToken)) return Results.NotFound();
        var versions = await database.PdfVersions.AsNoTracking().Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedAtUtc).ToListAsync(cancellationToken);
        httpContext.Response.Headers.CacheControl = "private, no-store";
        return Results.Ok(versions.Select(ToPdfResponse));
    }
    private static async Task<IResult> DownloadDraftPdfAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid pdfId,
        HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database, IDocumentBlobStore blobStore, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        var version = await (from pdf in database.PdfVersions.AsNoTracking()
            join document in database.TranslationDocuments.AsNoTracking() on pdf.DocumentId equals document.Id
            where pdf.Id == pdfId && document.Id == documentId && document.TenantId == tenantId && document.TranslationJobId == translationJobId
            select pdf).SingleOrDefaultAsync(cancellationToken);
        if (version is null) return Results.NotFound();
        var stream = await blobStore.OpenReadAsync(version.StorageKey, cancellationToken);
        if (stream is null) return Results.Problem(statusCode: 503, title: "draft_pdf_content_unavailable");
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers["X-Content-SHA256"] = version.Sha256;
        httpContext.Response.Headers["X-Document-Revision"] = version.RevisionNumber.ToString();
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        httpContext.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; frame-ancestors 'none'";
        return Results.File(stream, "application/pdf", $"translation-draft-r{version.RevisionNumber}-{version.Id:N}.pdf", enableRangeProcessing: true);
    }
    private static object ToPdfResponse(DocumentPdfVersion version) => new
    {
        version.Id, version.DocumentId, version.DraftRevisionId, version.RevisionNumber, version.TemplateRevisionId,
        version.RendererVersion, version.SizeBytes, version.Sha256, version.CreatedAtUtc, kind = "DraftUnsigned"
    };
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long limit, CancellationToken token)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[65536]; int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > limit) throw new PdfRenderValidationException("Image exceeds the draft PDF resource limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), token);
        }
        return buffer.ToArray();
    }
}
