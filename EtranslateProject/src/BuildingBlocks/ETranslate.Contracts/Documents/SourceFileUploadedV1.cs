namespace ETranslate.Contracts.Documents;

public sealed record SourceFileUploadedV1(
    Guid EventId,
    Guid SourceFileId,
    Guid DocumentId,
    Guid TranslationJobId,
    Guid TenantId,
    string Sha256,
    long SizeBytes,
    DateTimeOffset OccurredAtUtc);
