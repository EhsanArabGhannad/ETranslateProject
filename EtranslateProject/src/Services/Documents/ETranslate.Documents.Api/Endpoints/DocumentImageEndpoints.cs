using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using ETranslate.Documents.Api.Rendering;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    private static async Task<IResult> UploadDocumentImageAsync(Guid tenantId, Guid translationJobId, Guid documentId, int expectedRevision, Guid? uploadId,
        IFormFile file, HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database,
        IDocumentBlobStore blobStore, DocumentBlobWrites blobWrites, IPublishEndpoint publishEndpoint, TimeProvider timeProvider, CancellationToken cancellationToken)
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
        if (uploadId == Guid.Empty || !TemplateAsset.IsSupportedContentType(type) || file.Length is <= 0 or > TemplateAsset.MaximumSizeBytes || string.IsNullOrWhiteSpace(name) || name.Length > 255)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose a PNG or JPEG image, up to 5 MiB, with a valid file name."] });
        if (uploadId.HasValue && await ReplayImageUploadAsync(uploadId.Value, document, access.Actor!.UserId, expectedRevision, file, database, cancellationToken) is { } replay) return replay;
        var id = uploadId ?? Guid.NewGuid();
        // Each contender owns a different blob, even when the logical upload ID is the same.
        var key = $"{tenantId:N}/{documentId:N}/images/{id:N}-{Guid.NewGuid():N}{SourceFilePolicy.GetExtension(type)}";
        await using var write = blobWrites.Begin(key);
        StoredBlob? stored = null;
        try
        {
            await using var stream = file.OpenReadStream();
            var bytes = await ReadBoundedAsync(stream, TemplateAsset.MaximumSizeBytes, cancellationToken);
            if (!DocumentImagePolicy.HasSafeDimensions(bytes, type)) throw new InvalidDataException();
            await using var validated = new MemoryStream(bytes, writable: false);
            stored = await blobStore.SaveAsync(key, validated, type, TemplateAsset.MaximumSizeBytes, cancellationToken);
            var image = DocumentImage.Create(id, documentId, name, type, stored.SizeBytes, stored.Sha256, key, access.Actor!.UserId, timeProvider.GetUtcNow());
            database.DocumentImages.Add(image);
            await publishEndpoint.Publish(new DocumentImageUploadedV1(Guid.NewGuid(), id, documentId, tenantId, image.Sha256, image.SizeBytes, image.UploadedByUserId, image.UploadedAtUtc), cancellationToken);
            write.PersistenceStarting();
            await database.SaveChangesAsync(cancellationToken);
            write.Complete();
            httpContext.Response.Headers.CacheControl = "private, no-store";
            return Results.Created($"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/images/{id}", new { image.Id });
        }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException || error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await write.DiscardAfterConflictAsync();
            if (uploadId.HasValue && await ReplayImageUploadAsync(id, document, access.Actor!.UserId, expectedRevision, file, database, cancellationToken, stored) is { } committed) return committed;
            return Results.Conflict(new { error = "document_revision_or_review_conflict" });
        }
        catch (Exception error) when (error is InvalidDataException or BlobTooLargeException or PdfRenderValidationException)
        { return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["The image content or size is invalid."] }); }
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
