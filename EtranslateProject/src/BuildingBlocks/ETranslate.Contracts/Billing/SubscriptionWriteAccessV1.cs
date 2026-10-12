namespace ETranslate.Contracts.Billing;

// An observation, not a reusable authorization token or payment receipt.
public sealed record SubscriptionWriteAccessV1(Guid TenantId, string Status, bool CanWrite,
    string? Reason, DateTimeOffset ObservedAtUtc, DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? PaidStartsAtUtc = null, DateTimeOffset? PaidEndsAtUtc = null);
