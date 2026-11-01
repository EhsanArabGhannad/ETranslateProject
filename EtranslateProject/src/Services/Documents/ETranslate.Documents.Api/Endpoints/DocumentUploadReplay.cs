using System.Security.Cryptography;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    // A client uploadId is the immutable asset ID, scoped by the authorized document and actor.
    // Existing primary keys provide uniqueness; legacy callers can omit the optional ID.
    private static IResult UploadIdentityConflict() => Results.Conflict(new { error = "upload_identity_conflict" });

    private static async Task<StoredBlob> FingerprintUploadAsync(IFormFile file, long limit, CancellationToken token)
    {
        await using var stream = file.OpenReadStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long length = 0; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0) {
            length += read; if (length > limit) throw new BlobTooLargeException(limit);
            hash.AppendData(buffer.AsSpan(0, read));
        }
        return new StoredBlob(length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task<IResult?> ReplayImageUploadAsync(Guid id, TranslationDocument document, Guid actor,
        int expectedRevision, IFormFile file, DocumentsDbContext database, CancellationToken token, StoredBlob? fingerprint = null)
    {
        var image = await database.DocumentImages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (image is null) return null;
        if (image.DocumentId != document.Id || image.UploadedByUserId != actor || image.OriginalFileName != Path.GetFileName(file.FileName) ||
            image.ContentType != file.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant() || image.SizeBytes != file.Length) return UploadIdentityConflict();
        fingerprint ??= await FingerprintUploadAsync(file, TemplateAsset.MaximumSizeBytes, token);
        if (image.Sha256 != fingerprint.Sha256 || image.SizeBytes != fingerprint.SizeBytes) return UploadIdentityConflict();
        var current = await database.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id, token);
        if (current.IsReviewLocked || current.CurrentDraftRevision != expectedRevision) return UploadIdentityConflict();
        return Results.Ok(new { image.Id });
    }

    private static async Task<IResult?> ReplaySourceUploadAsync(Guid id, TranslationDocument document, Guid actor,
        IFormFile file, DocumentsDbContext database, CancellationToken token, StoredBlob? fingerprint = null)
    {
        var source = await database.SourceFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (source is null) return null;
        if (source.DocumentId != document.Id || source.UploadedByUserId != actor || source.OriginalFileName != Path.GetFileName(file.FileName) ||
            source.ContentType != file.ContentType.Split(';', 2)[0].Trim() || source.SizeBytes != file.Length) return UploadIdentityConflict();
        fingerprint ??= await FingerprintUploadAsync(file, SourceFilePolicy.MaximumFileSizeBytes, token);
        if (source.Sha256 != fingerprint.Sha256 || source.SizeBytes != fingerprint.SizeBytes) return UploadIdentityConflict();
        if ((await database.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id, token)).IsReviewLocked) return UploadIdentityConflict();
        return Results.Ok(ToResponse(source));
    }
}
