namespace ETranslate.Contracts.TranslationJobs;

public sealed record TranslationJobCreatedV1(
    Guid EventId,
    Guid TranslationJobId,
    Guid TenantId,
    Guid CreatedByUserId,
    string SignaturePolicy,
    string NotaryRequirement,
    DateTimeOffset OccurredAtUtc);
