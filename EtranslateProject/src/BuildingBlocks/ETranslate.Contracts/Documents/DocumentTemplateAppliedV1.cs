namespace ETranslate.Contracts.Documents;

public sealed record DocumentTemplateAppliedV1(
    Guid EventId, Guid DocumentId, Guid TranslationJobId, Guid TenantId,
    Guid TemplateRevisionId, Guid AppliedByUserId, DateTimeOffset OccurredAtUtc);
