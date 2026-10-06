namespace ETranslate.Documents.Api.Documents;

// A rendered draft is immutable and is not a signature or a legal approval.
public sealed class DocumentPdfVersion
{
    private DocumentPdfVersion() { }
    public DocumentPdfVersion(Guid documentId, Guid draftRevisionId, int revisionNumber, Guid? templateRevisionId,
        string rendererVersion, string storageKey, long sizeBytes, string sha256, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid(); DocumentId = documentId; DraftRevisionId = draftRevisionId;
        RevisionNumber = revisionNumber; TemplateRevisionId = templateRevisionId; RendererVersion = rendererVersion;
        StorageKey = storageKey; SizeBytes = sizeBytes; Sha256 = sha256; CreatedByUserId = createdByUserId; CreatedAtUtc = createdAtUtc;
    }
    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public Guid DraftRevisionId { get; private init; }
    public int RevisionNumber { get; private init; }
    public Guid? TemplateRevisionId { get; private init; }
    public string RendererVersion { get; private init; } = "";
    public string StorageKey { get; private init; } = "";
    public long SizeBytes { get; private init; }
    public string Sha256 { get; private init; } = "";
    public Guid CreatedByUserId { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
}
