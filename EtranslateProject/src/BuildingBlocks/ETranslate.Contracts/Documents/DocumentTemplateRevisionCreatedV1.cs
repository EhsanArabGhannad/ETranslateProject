namespace ETranslate.Contracts.Documents;

public sealed record DocumentTemplateRevisionCreatedV1(
    Guid EventId,
    Guid TemplateId,
    Guid TenantId,
    int RevisionNumber,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAtUtc);
