using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentTemplateEndpoints
{
    private static async Task<IResult> UploadAssetAsync(
        Guid tenantId, Guid templateId, IFormFile file, HttpContext httpContext,
        TenantAccessClient tenantAccessClient, DocumentsDbContext database,
        IDocumentBlobStore blobStore, IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var template = await database.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == templateId && item.TenantId == tenantId, cancellationToken);
        if (template is null) return Results.NotFound();
        if (!template.IsActive) return Results.Conflict(new { error = "document_template_is_archived" });

        var contentType = file.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        var fileName = Path.GetFileName(file.FileName);
        var errors = new Dictionary<string, string[]>();
        if (!TemplateAsset.IsSupportedContentType(contentType))
            errors["contentType"] = ["Only PNG and JPEG template images are supported."];
        if (file.Length <= 0 || file.Length > TemplateAsset.MaximumSizeBytes)
            errors["file"] = ["Template images must be between 1 byte and 5 MiB."];
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
            errors["fileName"] = ["File name is required and cannot exceed 255 characters."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var id = Guid.NewGuid();
        var key = $"{tenantId:N}/templates/{templateId:N}/assets/{id:N}{SourceFilePolicy.GetExtension(contentType)}";
        StoredBlob stored;
        try
        {
            await using var source = file.OpenReadStream();
            stored = await blobStore.SaveAsync(key, source, contentType, TemplateAsset.MaximumSizeBytes, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or BlobTooLargeException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var asset = TemplateAsset.Create(id, templateId, fileName, contentType,
                stored.SizeBytes, stored.Sha256, key, access.Actor!.UserId, now);
            database.TemplateAssets.Add(asset);
            await publishEndpoint.Publish(new TemplateAssetUploadedV1(
                Guid.NewGuid(), id, templateId, tenantId, asset.Sha256, asset.SizeBytes,
                asset.UploadedByUserId, now), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created(
                $"/api/v1/tenants/{tenantId}/document-templates/{templateId}/assets/{id}", ToAssetResponse(asset));
        }
        catch
        {
            await blobStore.DeleteIfExistsAsync(key, CancellationToken.None);
            throw;
        }
    }

    private static async Task<IResult> GetAssetsAsync(
        Guid tenantId, Guid templateId, HttpContext httpContext,
        TenantAccessClient tenantAccessClient, DocumentsDbContext database, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        if (!await database.DocumentTemplates.AnyAsync(
            item => item.Id == templateId && item.TenantId == tenantId, cancellationToken)) return Results.NotFound();
        var assets = await database.TemplateAssets.AsNoTracking()
            .Where(item => item.TemplateId == templateId).OrderBy(item => item.UploadedAtUtc)
            .ToListAsync(cancellationToken);
        return Results.Ok(assets.Select(ToAssetResponse));
    }

    private static async Task<IResult> DownloadAssetAsync(
        Guid tenantId, Guid templateId, Guid assetId, HttpContext httpContext,
        TenantAccessClient tenantAccessClient, DocumentsDbContext database,
        IDocumentBlobStore blobStore, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        var asset = await (from candidate in database.TemplateAssets.AsNoTracking()
                           join template in database.DocumentTemplates on candidate.TemplateId equals template.Id
                           where candidate.Id == assetId && template.Id == templateId && template.TenantId == tenantId
                           select candidate).SingleOrDefaultAsync(cancellationToken);
        if (asset is null) return Results.NotFound();
        var stream = await blobStore.OpenReadAsync(asset.StorageKey, cancellationToken);
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return stream is null
            ? Results.Problem(statusCode: 500, title: "Template asset content is unavailable.")
            : Results.File(stream, asset.ContentType, asset.OriginalFileName, enableRangeProcessing: true);
    }

    private static async Task<IResult?> ValidateAssetsAsync(
        Guid templateId, DocumentTemplateRevision revision, DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> ids;
        try
        {
            ids = TemplateAssetReferences.Parse(revision.EditorContentJson, revision.HeaderContentJson,
                revision.FooterContentJson, revision.PageLayoutJson, revision.WatermarkJson);
        }
        catch (DocumentTemplateValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }
        if (ids.Count == 0) return null;
        var count = await database.TemplateAssets.CountAsync(
            asset => asset.TemplateId == templateId && ids.Contains(asset.Id), cancellationToken);
        return count == ids.Count ? null : Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["assetId"] = ["Every referenced asset must belong to this template."]
        });
    }

    private static TemplateAssetResponse ToAssetResponse(TemplateAsset asset) =>
        new(asset.Id, asset.TemplateId, asset.OriginalFileName, asset.ContentType,
            asset.SizeBytes, asset.Sha256, asset.UploadedByUserId, asset.UploadedAtUtc);
}

public sealed record TemplateAssetResponse(
    Guid Id, Guid TemplateId, string OriginalFileName, string ContentType,
    long SizeBytes, string Sha256, Guid UploadedByUserId, DateTimeOffset UploadedAtUtc);
