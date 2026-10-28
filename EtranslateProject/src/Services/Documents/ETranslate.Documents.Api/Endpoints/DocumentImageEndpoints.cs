using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using ETranslate.Documents.Api.Rendering;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    private static async Task<IResult> UploadDocumentImageAsync(Guid tenantId, Guid translationJobId, Guid documentId, int expectedRevision,
        IFormFile file, HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database,
        IDocumentBlobStore blobStore, IPublishEndpoint publishEndpoint, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await FindDocumentAsync(database, tenantId, translationJobId, documentId, cancellationToken);
        if (document is null) return Results.NotFound();
        try { document.RecordImageUpload(expectedRevision, timeProvider.GetUtcNow()); }
        catch (DocumentRevisionConflictException) { return RevisionConflict(expectedRevision, document.CurrentDraftRevision); }
        catch (DocumentReviewConflictException) { return Results.Conflict(new { error = "document_review_locked" }); }
        var name = Path.GetFileName(file.FileName);
        var type = file.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!TemplateAsset.IsSupportedContentType(type) || file.Length is <= 0 or > TemplateAsset.MaximumSizeBytes || string.IsNullOrWhiteSpace(name) || name.Length > 255)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose a PNG or JPEG image, up to 5 MiB, with a valid file name."] });
        var id = Guid.NewGuid();
        var key = $"{tenantId:N}/{documentId:N}/images/{id:N}{SourceFilePolicy.GetExtension(type)}";
        try
        {
            await using var stream = file.OpenReadStream();
            var bytes = await ReadBoundedAsync(stream, TemplateAsset.MaximumSizeBytes, cancellationToken);
            if (!DocumentImagePolicy.HasSafeDimensions(bytes, type)) throw new InvalidDataException();
            await using var validated = new MemoryStream(bytes, writable: false);
            var stored = await blobStore.SaveAsync(key, validated, type, TemplateAsset.MaximumSizeBytes, cancellationToken);
            var image = DocumentImage.Create(id, documentId, name, type, stored.SizeBytes, stored.Sha256, key, access.Actor!.UserId, timeProvider.GetUtcNow());
            database.DocumentImages.Add(image);
            await publishEndpoint.Publish(new DocumentImageUploadedV1(Guid.NewGuid(), id, documentId, tenantId, image.Sha256, image.SizeBytes, image.UploadedByUserId, image.UploadedAtUtc), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            httpContext.Response.Headers.CacheControl = "private, no-store";
            return Results.Created($"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/images/{id}", new { image.Id });
        }
        catch (DbUpdateConcurrencyException)
        { await blobStore.DeleteIfExistsAsync(key, CancellationToken.None); return Results.Conflict(new { error = "document_revision_or_review_conflict" }); }
        catch (Exception error) when (error is InvalidDataException or BlobTooLargeException or PdfRenderValidationException)
        { await blobStore.DeleteIfExistsAsync(key, CancellationToken.None); return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["The image content or size is invalid."] }); }
        catch { await blobStore.DeleteIfExistsAsync(key, CancellationToken.None); throw; }
    }

    private static async Task<IResult> DownloadDocumentImageAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid imageId,
        HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database, IDocumentBlobStore blobStore, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        var image = await (from candidate in database.DocumentImages.AsNoTracking()
            join document in database.TranslationDocuments.AsNoTracking() on candidate.DocumentId equals document.Id
            where document.Id == documentId && document.TenantId == tenantId && document.TranslationJobId == translationJobId && candidate.Id == imageId
            select candidate).SingleOrDefaultAsync(cancellationToken);
        if (image is null) return Results.NotFound();
        var stream = await blobStore.OpenReadAsync(image.StorageKey, cancellationToken);
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return stream is null ? Results.NotFound() : Results.File(stream, image.ContentType, enableRangeProcessing: true);
    }
}
