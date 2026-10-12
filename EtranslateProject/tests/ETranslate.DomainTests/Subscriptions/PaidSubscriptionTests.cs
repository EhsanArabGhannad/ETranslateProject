using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Contracts.Billing;
using ETranslate.SubscriptionAccess;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class PaidSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 30, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static BillingPlan Plan => new("independent-monthly", "Independent monthly", "IndependentTranslator");
    private static Subscription Expired() => Subscription.StartTrial(Tenant, Now.AddDays(-31), "IndependentTranslator");

    [Fact] public void Monthly_anchor_does_not_drift_after_a_short_month()
    {
        var subscription = Expired();
        var first = subscription.AllocatePaidPeriod(Plan, Now);
        Assert.Equal(Now, first.Start); Assert.Equal(Now.AddMonths(1), first.End);
        var second = subscription.AllocatePaidPeriod(Plan, Now.AddDays(5));
        Assert.Equal(first.End, second.Start); Assert.Equal(Now.AddMonths(2), second.End);
        Assert.Equal(31, second.End.Day); Assert.Equal(2, subscription.Version); Assert.Equal(2, subscription.PaidMonths);
    }

    [Theory] [InlineData(2024, 29)] [InlineData(2025, 28)]
    public void Leap_year_month_boundaries_are_explicit(int year, int day)
    {
        var start = new DateTimeOffset(year, 1, 31, 1, 0, 0, TimeSpan.Zero);
        var subscription = Subscription.StartTrial(Tenant, start.AddDays(-31), "IndependentTranslator");
        Assert.Equal(day, subscription.AllocatePaidPeriod(Plan, start).End.Day);
    }

    [Fact] public void Purchase_preserves_remaining_trial_and_switches_at_its_exact_end()
    {
        var subscription = Subscription.StartTrial(Tenant, Now.AddDays(-10), "IndependentTranslator");
        var period = subscription.AllocatePaidPeriod(Plan, Now);
        Assert.Equal(subscription.TrialEndsAtUtc, period.Start);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.GetEffectiveStatus(Now));
        Assert.Equal("trial", SubscriptionSnapshot.Observe(Tenant, subscription, Now).PlanCode);
        Assert.Equal(SubscriptionStatus.Active, subscription.GetEffectiveStatus(period.Start));
        Assert.Equal(SubscriptionStatus.Active, subscription.GetEffectiveStatus(period.End.AddTicks(-1)));
        Assert.Equal(SubscriptionStatus.Expired, subscription.GetEffectiveStatus(period.End));
        Assert.True(subscription.ExpireTrial(period.End)); Assert.False(subscription.ExpireTrial(period.End));
    }

    [Fact] public void Renewal_after_a_gap_starts_a_new_anchor_without_charging_for_the_gap()
    {
        var subscription = Expired(); subscription.AllocatePaidPeriod(Plan, Now);
        var later = Now.AddMonths(2).AddDays(3);
        var period = subscription.AllocatePaidPeriod(Plan, later);
        Assert.Equal(later, period.Start); Assert.Equal(later.AddMonths(1), period.End);
        Assert.Equal(1, subscription.PaidMonths);
    }

    [Fact] public void Future_prepaid_access_still_uses_the_trial_lease()
    {
        var subscription = Subscription.StartTrial(Tenant, Now, "IndependentTranslator"); subscription.AllocatePaidPeriod(Plan, Now);
        var decision = SubscriptionWritePolicy.Observe(Tenant, subscription, Now);
        Assert.True(decision.CanWrite); Assert.Equal("Trialing", decision.Status);
        Assert.Equal(SubscriptionWriteStatus.Allowed, SubscriptionWriteClient.Validate(Tenant, decision, Now));
        var paid = SubscriptionWritePolicy.Observe(Tenant, subscription, subscription.PaidStartsAtUtc!.Value);
        Assert.Equal("Active", paid.Status); Assert.True(paid.CanWrite);
        Assert.Equal(SubscriptionWriteStatus.Allowed, SubscriptionWriteClient.Validate(Tenant, paid, paid.ObservedAtUtc));
    }

    [Fact] public void Paid_expiry_in_transit_is_denied_and_unbounded_active_is_not_a_grant()
    {
        var end = Now.AddSeconds(1);
        var paid = new SubscriptionWriteAccessV1(Tenant, "Active", true, null, Now, null, Now.AddDays(-1), end);
        Assert.Equal(SubscriptionWriteStatus.Allowed, SubscriptionWriteClient.Validate(Tenant, paid, Now));
        Assert.Equal(SubscriptionWriteStatus.Required, SubscriptionWriteClient.Validate(Tenant, paid, end));
        Assert.Equal(SubscriptionWriteStatus.Unavailable, SubscriptionWriteClient.Validate(Tenant, paid with { PaidEndsAtUtc = null }, Now));
        Assert.Equal(SubscriptionWriteStatus.Unavailable, SubscriptionWriteClient.Validate(Tenant, paid with { PaidStartsAtUtc = Now.AddSeconds(1) }, Now));
        var legacy = Expired(); typeof(Subscription).GetProperty(nameof(Subscription.Status))!.SetValue(legacy, SubscriptionStatus.Active);
        Assert.False(SubscriptionWritePolicy.Observe(Tenant, legacy, Now).CanWrite);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(30)] [InlineData(31)] [InlineData(59)]
    public void Paid_snapshot_is_valid_in_bff_during_trial_active_and_expired_states(int days)
    {
        var subscription = Subscription.StartTrial(Tenant, Now.AddDays(-29), "IndependentTranslator"); subscription.AllocatePaidPeriod(Plan, Now);
        var s = SubscriptionSnapshot.Observe(Tenant, subscription, Now.AddDays(days));
        var view = new SubscriptionView(s.TenantId, s.PlanCode, s.Status, s.TrialStartedAtUtc, s.TrialEndsAtUtc, s.ObservedAtUtc,
            s.RemainingTrialDays, s.PaidPlanCode, s.PaidPlanName, s.PaidStartsAtUtc, s.PaidEndsAtUtc, s.PaidMonths, s.RemainingPaidDays);
        Assert.True(view.IsValidFor(Tenant)); Assert.False((view with { PaidEndsAtUtc = null }).IsValidFor(Tenant));
        Assert.False((view with { RemainingPaidDays = -1 }).IsValidFor(Tenant));
        Assert.False((view with { PaidMonths = 0 }).IsValidFor(Tenant));
    }

    [Fact] public void Plan_or_tenant_type_changes_do_not_rewrite_an_existing_paid_period()
    {
        var subscription = Expired(); subscription.AllocatePaidPeriod(Plan, Now);
        Assert.Throws<InvalidOperationException>(() => subscription.AllocatePaidPeriod(Plan with { Code = "different" }, Now));
        Assert.Throws<InvalidOperationException>(() => subscription.AllocatePaidPeriod(Plan with { TenantType = "TranslationOffice" }, Now));
        Assert.Equal(1, subscription.Version);
        var legacy = Subscription.StartTrial(Tenant, Now);
        Assert.Throws<InvalidOperationException>(() => legacy.AllocatePaidPeriod(Plan, Now));
    }

    [Theory] [InlineData(SubscriptionStatus.Canceled)] [InlineData(SubscriptionStatus.PastDue)]
    public void Disputed_or_canceled_status_needs_reconciliation(SubscriptionStatus status)
    {
        var subscription = Expired(); typeof(Subscription).GetProperty(nameof(Subscription.Status))!.SetValue(subscription, status);
        Assert.Throws<InvalidOperationException>(() => subscription.AllocatePaidPeriod(Plan, Now));
    }

    [Theory] [InlineData(0)] [InlineData(13)]
    public void Invalid_catalog_months_cannot_mutate_a_subscription(int months)
    {
        var subscription = Expired(); Assert.Throws<ArgumentException>(() => subscription.AllocatePaidPeriod(Plan with { Months = months }, Now));
        Assert.Equal(0, subscription.Version); Assert.Null(subscription.PaidEndsAtUtc);
    }

    [Fact] public void Catalog_is_validated_and_disabled_plans_cannot_be_allocated()
    {
        Assert.Throws<ArgumentException>(() => new BillingPlanCatalog([Plan, Plan]));
        Assert.Throws<ArgumentException>(() => new BillingPlanCatalog([Plan with { Code = "trial" }]));
        Assert.Throws<ArgumentException>(() => new BillingPlanCatalog([Plan with { Name = " " }]));
        Assert.Throws<ArgumentException>(() => new BillingPlanCatalog([Plan with { TenantType = "Other" }]));
        Assert.Throws<InvalidOperationException>(() => new BillingPlanCatalog([Plan with { Enabled = false }]).GetEnabled(Plan.Code));
        Assert.Throws<InvalidOperationException>(() => Expired().AllocatePaidPeriod(Plan with { Enabled = false }, Now));
    }

    [Fact] public void Immutable_payment_identity_does_not_allow_another_tenant_or_plan()
    {
        var subscription = Expired(); var period = subscription.AllocatePaidPeriod(Plan, Now);
        var payment = new ConfirmedSubscriptionPayment(Tenant, Plan.Code, "fixture", "CaseSensitiveReference", Now);
        payment.Validate(Now);
        var record = PaidSubscriptionPeriod.Create(subscription, Plan, payment, Now, period.Start, period.End);
        Assert.True(record.Matches(payment)); Assert.False(record.Matches(payment with { TenantId = Guid.NewGuid() }));
        Assert.False(record.Matches(payment with { PlanCode = "different" }));
        Assert.False(record.Matches(payment with { PaymentReference = "casesensitivereference" }));
        Assert.Throws<ArgumentException>(() => (payment with { ConfirmedAtUtc = Now.AddHours(1) }).Validate(Now));
        Assert.Throws<ArgumentException>(() => (payment with { PaymentReference = " padded " }).Validate(Now));
    }
}
