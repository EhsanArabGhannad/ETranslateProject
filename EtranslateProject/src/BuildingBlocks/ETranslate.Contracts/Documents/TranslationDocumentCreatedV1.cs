namespace ETranslate.Contracts.Documents;

public sealed record TranslationDocumentCreatedV1(
    Guid EventId,
    Guid DocumentId,
    Guid TranslationJobId,
    Guid TenantId,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAtUtc);
