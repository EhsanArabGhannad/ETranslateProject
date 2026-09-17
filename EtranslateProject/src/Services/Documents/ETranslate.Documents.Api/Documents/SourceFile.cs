namespace ETranslate.Documents.Api.Documents;

public sealed class SourceFile
{
    private SourceFile()
    {
    }

    internal SourceFile(
        Guid id,
        Guid documentId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string storageKey,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAtUtc)
    {
        Id = id;
        DocumentId = documentId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StorageKey = storageKey;
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = uploadedAtUtc;
    }

    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public string OriginalFileName { get; private init; } = string.Empty;
    public string ContentType { get; private init; } = string.Empty;
    public long SizeBytes { get; private init; }
    public string Sha256 { get; private init; } = string.Empty;
    public string StorageKey { get; private init; } = string.Empty;
    public Guid UploadedByUserId { get; private init; }
    public DateTimeOffset UploadedAtUtc { get; private init; }
}
