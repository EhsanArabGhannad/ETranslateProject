using ETranslate.Billing.Api.Subscriptions;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartTrial_CreatesExactlyThirtyDayTrial()
    {
        var tenantId = Guid.NewGuid();

        var subscription = Subscription.StartTrial(tenantId, Start);

        Assert.Equal(tenantId, subscription.TenantId);
        Assert.Equal(Subscription.TrialPlanCode, subscription.PlanCode);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Start, subscription.TrialStartedAtUtc);
        Assert.Equal(Start.AddDays(30), subscription.TrialEndsAtUtc);
    }

    [Fact]
    public void EffectiveStatus_ExpiresAtTrialBoundary()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Start);

        Assert.Equal(
            SubscriptionStatus.Trialing,
            subscription.GetEffectiveStatus(Start.AddDays(30).AddTicks(-1)));
        Assert.Equal(
            SubscriptionStatus.Expired,
            subscription.GetEffectiveStatus(Start.AddDays(30)));
    }

    [Fact]
    public void ExpireTrial_IsIdempotent()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Start);
        var expirationTime = Start.AddDays(31);

        Assert.True(subscription.ExpireTrial(expirationTime));
        Assert.False(subscription.ExpireTrial(expirationTime.AddMinutes(1)));
        Assert.Equal(SubscriptionStatus.Expired, subscription.Status);
        Assert.Equal(expirationTime, subscription.UpdatedAtUtc);
    }
}
