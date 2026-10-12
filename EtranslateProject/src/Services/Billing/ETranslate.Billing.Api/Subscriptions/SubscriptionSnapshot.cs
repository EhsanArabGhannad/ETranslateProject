namespace ETranslate.Billing.Api.Subscriptions;

// Read-only observation. GET never starts, renews or expires a subscription.
public sealed record SubscriptionSnapshot(Guid TenantId, string? PlanCode, string Status,
    DateTimeOffset? TrialStartedAtUtc, DateTimeOffset? TrialEndsAtUtc, DateTimeOffset ObservedAtUtc, int? RemainingTrialDays,
    string? PaidPlanCode = null, string? PaidPlanName = null, DateTimeOffset? PaidStartsAtUtc = null,
    DateTimeOffset? PaidEndsAtUtc = null, int? PaidMonths = null, int? RemainingPaidDays = null)
{
    public static SubscriptionSnapshot Observe(Guid tenantId, Subscription? subscription, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || (subscription is not null && subscription.TenantId != tenantId))
            throw new ArgumentException("Subscription must belong to the authorized tenant.");
        if (subscription is null) return new(tenantId, null, "Pending", null, null, now, null);
        var status = subscription.GetEffectiveStatus(now);
        var code = status == SubscriptionStatus.Trialing ? Subscription.TrialPlanCode : subscription.PlanCode;
        int? days = code != Subscription.TrialPlanCode ? null : status == SubscriptionStatus.Trialing && subscription.TrialEndsAtUtc is { } end
            ? (int)Math.Clamp(Math.Ceiling((end - now).TotalDays), 0, Subscription.TrialLengthInDays) : 0;
        var paid = subscription.HasValidPaidWindow;
        var paidDays = paid ? (int?)Math.Max(0, Math.Ceiling((subscription.PaidEndsAtUtc!.Value -
            (now < subscription.PaidStartsAtUtc ? subscription.PaidStartsAtUtc.Value : now)).TotalDays)) : null;
        return new(tenantId, code, status.ToString(), subscription.TrialStartedAtUtc, subscription.TrialEndsAtUtc, now, days,
            paid ? subscription.PlanCode : null, paid ? subscription.PaidPlanName : null,
            paid ? subscription.PaidStartsAtUtc : null, paid ? subscription.PaidEndsAtUtc : null,
            paid ? subscription.PaidMonths : null, paidDays);
    }
}
