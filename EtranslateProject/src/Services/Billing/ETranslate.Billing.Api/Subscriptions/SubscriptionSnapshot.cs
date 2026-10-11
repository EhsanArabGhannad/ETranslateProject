namespace ETranslate.Billing.Api.Subscriptions;

// Read-only observation. GET never starts, renews or expires a subscription.
public sealed record SubscriptionSnapshot(Guid TenantId, string? PlanCode, string Status,
    DateTimeOffset? TrialStartedAtUtc, DateTimeOffset? TrialEndsAtUtc, DateTimeOffset ObservedAtUtc, int? RemainingTrialDays)
{
    public static SubscriptionSnapshot Observe(Guid tenantId, Subscription? subscription, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || (subscription is not null && subscription.TenantId != tenantId))
            throw new ArgumentException("Subscription must belong to the authorized tenant.");
        if (subscription is null) return new(tenantId, null, "Pending", null, null, now, null);
        var status = subscription.GetEffectiveStatus(now);
        int? days = subscription.PlanCode != Subscription.TrialPlanCode ? null : status == SubscriptionStatus.Trialing && subscription.TrialEndsAtUtc is { } end
            ? (int)Math.Clamp(Math.Ceiling((end - now).TotalDays), 0, Subscription.TrialLengthInDays) : 0;
        return new(tenantId, subscription.PlanCode, status.ToString(), subscription.TrialStartedAtUtc, subscription.TrialEndsAtUtc, now, days);
    }
}
