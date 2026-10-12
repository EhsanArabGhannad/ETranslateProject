using ETranslate.Contracts.Billing;

namespace ETranslate.Billing.Api.Subscriptions;

public static class SubscriptionWritePolicy
{
    public static SubscriptionWriteAccessV1 Observe(Guid tenantId, Subscription? subscription, DateTimeOffset now)
    {
        var snapshot = SubscriptionSnapshot.Observe(tenantId, subscription, now);
        var allowed = snapshot.Status == "Active" && subscription!.HasValidPaidWindow && subscription.PaidStartsAtUtc <= now && now < subscription.PaidEndsAtUtc ||
            snapshot.Status == "Trialing" && snapshot.TrialStartedAtUtc <= now && now < snapshot.TrialEndsAtUtc;
        return new(tenantId, snapshot.Status, allowed,
            allowed ? null : snapshot.Status == "Pending" ? "subscription_pending" : "subscription_required",
            now, snapshot.TrialEndsAtUtc, snapshot.PaidStartsAtUtc, snapshot.PaidEndsAtUtc);
    }
}
