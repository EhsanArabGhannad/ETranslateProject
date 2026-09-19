namespace ETranslate.Contracts.Documents;

public sealed record DocumentTemplateCreatedV1(
    Guid EventId,
    Guid TemplateId,
    Guid TenantId,
    Guid CreatedByUserId,
    int InitialRevision,
    DateTimeOffset OccurredAtUtc);
