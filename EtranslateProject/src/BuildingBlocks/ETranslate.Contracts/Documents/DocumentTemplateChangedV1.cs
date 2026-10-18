namespace ETranslate.Contracts.Documents;

public sealed record DocumentTemplateChangedV1(Guid EventId, Guid TenantId, Guid TranslationJobId, Guid DocumentId,
    Guid? PreviousTemplateRevisionId, Guid TemplateRevisionId, int DraftRevisionNumber, Guid ChangedByUserId, DateTimeOffset OccurredAtUtc);
