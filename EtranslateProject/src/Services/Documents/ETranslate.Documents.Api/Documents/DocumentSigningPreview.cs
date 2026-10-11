namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentSigningPreview
{
    private DocumentSigningPreview() { }
    public DocumentSigningPreview(Guid id, Guid documentId, Guid preparationId, Guid reviewId, Guid sourcePdfVersionId,
        string sourcePdfSha256, string rendererVersion, string snapshotJson, string storageKey, long sizeBytes, string sha256,
        Guid actor, DateTimeOffset now)
    {
        if (new[] { id, documentId, preparationId, reviewId, sourcePdfVersionId, actor }.Any(value => value == Guid.Empty) ||
            sourcePdfSha256 is not { Length: 64 } || !sourcePdfSha256.All(Uri.IsHexDigit) ||
            sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit) || sizeBytes is <= 0 or > 25 * 1024 * 1024 ||
            string.IsNullOrWhiteSpace(rendererVersion) || rendererVersion.Length > 100 || string.IsNullOrWhiteSpace(snapshotJson) ||
            string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500) throw new ArgumentException("Invalid unsigned preview metadata.");
        Id = id; DocumentId = documentId; PreparationId = preparationId; ReviewId = reviewId; SourcePdfVersionId = sourcePdfVersionId;
        SourcePdfSha256 = sourcePdfSha256.ToLowerInvariant(); RendererVersion = rendererVersion; PlanSnapshotJson = snapshotJson;
        StorageKey = storageKey; SizeBytes = sizeBytes; Sha256 = sha256.ToLowerInvariant(); CreatedByUserId = actor; CreatedAtUtc = now;
    }
    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public Guid PreparationId { get; private init; }
    public Guid ReviewId { get; private init; }
    public Guid SourcePdfVersionId { get; private init; }
    public string SourcePdfSha256 { get; private init; } = "";
    public string RendererVersion { get; private init; } = "";
    public string PlanSnapshotJson { get; private init; } = "";
    public string StorageKey { get; private init; } = "";
    public long SizeBytes { get; private init; }
    public string Sha256 { get; private init; } = "";
    public Guid CreatedByUserId { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
}
