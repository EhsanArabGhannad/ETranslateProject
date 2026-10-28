namespace ETranslate.Documents.Api.Documents;

// Immutable managed image; retained independently of the current draft for historical revisions.
public sealed class DocumentImage
{
    private DocumentImage() { }
    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public string OriginalFileName { get; private init; } = "";
    public string ContentType { get; private init; } = "";
    public long SizeBytes { get; private init; }
    public string Sha256 { get; private init; } = "";
    public string StorageKey { get; private init; } = "";
    public Guid UploadedByUserId { get; private init; }
    public DateTimeOffset UploadedAtUtc { get; private init; }

    public static DocumentImage Create(Guid id, Guid documentId, string fileName, string contentType, long sizeBytes, string sha256, string storageKey, Guid actor, DateTimeOffset now)
    {
        // Share the bounded PNG/JPEG metadata contract, not template ownership.
        var metadata = TemplateAsset.Create(id, documentId, fileName, contentType, sizeBytes, sha256, storageKey, actor, now);
        return new() { Id = id, DocumentId = documentId, OriginalFileName = metadata.OriginalFileName, ContentType = metadata.ContentType,
            SizeBytes = metadata.SizeBytes, Sha256 = metadata.Sha256, StorageKey = storageKey, UploadedByUserId = actor, UploadedAtUtc = now };
    }
}
