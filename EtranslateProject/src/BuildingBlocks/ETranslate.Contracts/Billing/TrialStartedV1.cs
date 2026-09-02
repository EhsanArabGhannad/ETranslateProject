namespace ETranslate.Contracts.Billing;

public sealed record TrialStartedV1(
    Guid EventId,
    Guid TenantId,
    DateTimeOffset TrialStartedAtUtc,
    DateTimeOffset TrialEndsAtUtc,
    DateTimeOffset OccurredAtUtc);
