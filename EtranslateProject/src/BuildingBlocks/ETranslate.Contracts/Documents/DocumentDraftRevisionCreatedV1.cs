namespace ETranslate.Contracts.Documents;

public sealed record DocumentDraftRevisionCreatedV1(
    Guid EventId,
    Guid DocumentId,
    Guid TranslationJobId,
    Guid TenantId,
    int RevisionNumber,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAtUtc);
