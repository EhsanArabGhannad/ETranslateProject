using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Trust;
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
    private static async Task<IResult> CreateSigningPreviewAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid preparationId,
        HttpContext context, TenantAccessClient identity, SigningPlanClient trust, DocumentsDbContext db,
        IDocumentBlobStore store, ISigningPreviewRenderer renderer, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var access = await AuthorizeAsync(tenantId, context, identity, true, ct);
        if (access.Error is not null) return access.Error;
        if (!access.Actor!.CanReviewDocuments) return Results.StatusCode(403);
        if (!await PreviewDocumentExists(db, tenantId, translationJobId, documentId, ct)) return Results.NotFound();
        var authorization = context.Request.Headers.Authorization.ToString();
        var lookup = await trust.ReadAsync(tenantId, translationJobId, documentId, preparationId, authorization, ct);
        if (lookup.Status != 200) return Results.StatusCode(lookup.Status);
        var plan = lookup.Value!;
        if (!SigningPreviewPolicy.ValidSnapshot(plan, tenantId, translationJobId, documentId, preparationId))
            return Results.Problem(statusCode: 503, title: "signing_preview_plan_inconsistent");
        if (!SigningPreviewPolicy.CanGenerate(plan) || !await PreviewSourceIsCurrent(db, plan, ct)) return PreviewConflict();
        var source = await db.PdfVersions.AsNoTracking().SingleAsync(pdf => pdf.Id == plan.PdfVersionId && pdf.DocumentId == documentId, ct);
        var originalBytes = await ReadVerifiedPreviewPdf(store, source.StorageKey, source.SizeBytes, source.Sha256, ct);
        if (originalBytes is null) return PreviewUnavailable();
        var existing = await db.SigningPreviews.AsNoTracking().SingleOrDefaultAsync(item => item.PreparationId == preparationId &&
            item.RendererVersion == SigningPreviewPolicy.RendererVersion, ct);
        if (existing is not null)
        {
            if (await ReadVerifiedPreviewPdf(store, existing.StorageKey, existing.SizeBytes, existing.Sha256, ct) is null) return PreviewUnavailable();
            var current = await trust.ReadAsync(tenantId, translationJobId, documentId, preparationId, authorization, ct);
            if (current.Status != 200) return Results.StatusCode(current.Status);
            if (!SigningPreviewPolicy.CanGenerate(current.Value!) || !await PreviewSourceIsCurrent(db, plan, ct)) return PreviewConflict();
            return Results.Ok(PreviewResponse(existing));
        }
        var id = Guid.NewGuid(); byte[] bytes;
        try { bytes = await renderer.RenderAsync(originalBytes, plan, id, ct); }
        catch (PdfRenderValidationException error) { return Results.Problem(statusCode: 422, title: "signing_preview_not_supported", detail: error.Message); }
        catch (PdfRendererBusyException) { context.Response.Headers.RetryAfter = "2"; return Results.Problem(statusCode: 429, title: "signing_preview_renderer_busy"); }
        catch (PdfRendererUnavailableException error)
        {
            loggers.CreateLogger("SigningPreview").LogWarning(error, "Unsigned preview rendering failed for preparation {PreparationId}", preparationId);
            return Results.Problem(statusCode: 503, title: "signing_preview_renderer_unavailable");
        }
        var key = $"{tenantId:N}/{documentId:N}/signing-previews/{id:N}.pdf";
        await using var stream = new MemoryStream(bytes, writable: false);
        var stored = await store.SaveAsync(key, stream, "application/pdf", DraftPdfPolicy.MaximumBytes, ct);
        try
        {
            // Recheck after rendering; this is a planning snapshot, never an authorization to sign.
            var latest = await trust.ReadAsync(tenantId, translationJobId, documentId, preparationId, authorization, ct);
            if (latest.Status != 200)
            {
                await store.DeleteIfExistsAsync(key, CancellationToken.None);
                return Results.StatusCode(latest.Status);
            }
            if (!SigningPreviewPolicy.CanGenerate(latest.Value!) || !await PreviewSourceIsCurrent(db, plan, ct))
            {
                await store.DeleteIfExistsAsync(key, CancellationToken.None);
                return PreviewConflict();
            }
            var preview = new DocumentSigningPreview(id, documentId, preparationId, plan.ReviewId, plan.PdfVersionId,
                plan.PdfSha256, SigningPreviewPolicy.RendererVersion, JsonSerializer.Serialize(plan), key,
                stored.SizeBytes, stored.Sha256, access.Actor.UserId, clock.GetUtcNow());
            db.SigningPreviews.Add(preview);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/signing-previews/{id}/file", PreviewResponse(preview));
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await store.DeleteIfExistsAsync(key, CancellationToken.None);
            existing = await db.SigningPreviews.AsNoTracking().SingleOrDefaultAsync(item => item.PreparationId == preparationId &&
                item.RendererVersion == SigningPreviewPolicy.RendererVersion, ct);
            if (existing is null) throw;
            return await ReadVerifiedPreviewPdf(store, existing.StorageKey, existing.SizeBytes, existing.Sha256, ct) is null
                ? PreviewUnavailable() : Results.Ok(PreviewResponse(existing));
        }
        catch { await store.DeleteIfExistsAsync(key, CancellationToken.None); throw; }
    }
    private static async Task<IResult> GetSigningPreviewsAsync(Guid tenantId, Guid translationJobId, Guid documentId,
        HttpContext context, TenantAccessClient identity, DocumentsDbContext db, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var access = await AuthorizeAsync(tenantId, context, identity, false, ct);
        if (access.Error is not null) return access.Error;
        if (!await PreviewDocumentExists(db, tenantId, translationJobId, documentId, ct)) return Results.NotFound();
        return Results.Ok((await db.SigningPreviews.AsNoTracking().Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedAtUtc).Take(100).ToListAsync(ct)).Select(PreviewResponse));
    }
    private static async Task<IResult> DownloadSigningPreviewAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid previewId,
        HttpContext context, TenantAccessClient identity, DocumentsDbContext db, IDocumentBlobStore store, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var access = await AuthorizeAsync(tenantId, context, identity, false, ct);
        if (access.Error is not null) return access.Error;
        var preview = await (from item in db.SigningPreviews.AsNoTracking()
            join document in db.TranslationDocuments.AsNoTracking() on item.DocumentId equals document.Id
            where item.Id == previewId && document.Id == documentId && document.TenantId == tenantId && document.TranslationJobId == translationJobId
            select item).SingleOrDefaultAsync(ct);
        if (preview is null) return Results.NotFound();
        var bytes = await ReadVerifiedPreviewPdf(store, preview.StorageKey, preview.SizeBytes, preview.Sha256, ct);
        if (bytes is null) return PreviewUnavailable();
        context.Response.Headers["X-Content-SHA256"] = preview.Sha256;
        context.Response.Headers["X-Source-PDF-SHA256"] = preview.SourcePdfSha256;
        context.Response.Headers["X-Artifact-Kind"] = SigningPreviewPolicy.Kind;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; frame-ancestors 'none'";
        return Results.File(bytes, "application/pdf", $"signing-preview-UNSIGNED-{preview.Id:N}.pdf", enableRangeProcessing: true);
    }
    private static Task<bool> PreviewDocumentExists(DocumentsDbContext db, Guid tenant, Guid job, Guid document, CancellationToken ct)
        => db.TranslationDocuments.AsNoTracking().AnyAsync(item => item.Id == document && item.TenantId == tenant && item.TranslationJobId == job, ct);
    private static Task<bool> PreviewSourceIsCurrent(DocumentsDbContext db, SigningPreviewSnapshotV1 plan, CancellationToken ct)
        => (from document in db.TranslationDocuments.AsNoTracking()
            join review in db.Reviews.AsNoTracking() on document.Id equals review.DocumentId
            join pdf in db.PdfVersions.AsNoTracking() on review.PdfVersionId equals pdf.Id
            where document.Id == plan.DocumentId && document.TenantId == plan.TenantId && document.TranslationJobId == plan.TranslationJobId &&
                document.ReviewStatus == DocumentReviewStatus.Approved && review.Status == DocumentReviewStatus.Approved &&
                review.Id == plan.ReviewId && document.ReviewRound == plan.ReviewRound && review.Round == plan.ReviewRound &&
                document.CurrentDraftRevision == plan.RevisionNumber && review.RevisionNumber == plan.RevisionNumber && pdf.RevisionNumber == plan.RevisionNumber &&
                pdf.Id == plan.PdfVersionId && pdf.DocumentId == plan.DocumentId && pdf.DraftRevisionId == plan.DraftRevisionId &&
                review.DraftRevisionId == plan.DraftRevisionId && pdf.TemplateRevisionId == plan.TemplateRevisionId && document.TemplateRevisionId == plan.TemplateRevisionId &&
                pdf.Sha256 == plan.PdfSha256 && review.PdfSha256 == plan.PdfSha256 && pdf.SizeBytes == plan.PdfSizeBytes
            select document.Id).AnyAsync(ct);
    private static async Task<byte[]?> ReadVerifiedPreviewPdf(IDocumentBlobStore store, string key, long size, string hash, CancellationToken ct)
    {
        if (size is <= 0 or > DraftPdfPolicy.MaximumBytes) return null;
        try
        {
            await using var stream = await store.OpenReadAsync(key, ct);
            if (stream is null) return null;
            var bytes = await ReadBoundedAsync(stream, DraftPdfPolicy.MaximumBytes, ct);
            return bytes.LongLength == size && Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase) ? bytes : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (PdfRenderValidationException) { return null; }
    }
    private static IResult PreviewConflict() => Results.Conflict(new { error = "signing_preview_source_changed", detail = "Only a prepared plan linked to the current approved PDF can generate a preview." });
    private static IResult PreviewUnavailable() => Results.Problem(statusCode: 503, title: "signing_preview_content_unavailable_or_corrupt");
    private static object PreviewResponse(DocumentSigningPreview item) => new
    {
        item.Id, item.DocumentId, item.PreparationId, item.ReviewId, item.SourcePdfVersionId, item.SourcePdfSha256,
        item.RendererVersion, item.SizeBytes, item.Sha256, item.CreatedByUserId, item.CreatedAtUtc, kind = SigningPreviewPolicy.Kind
    };
}
