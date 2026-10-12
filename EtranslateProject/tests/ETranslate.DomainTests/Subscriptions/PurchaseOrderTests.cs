using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Orders;
using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class PurchaseOrderTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static BillingPlan Plan => new("office-monthly", "Office monthly", "TranslationOffice");
    private static Subscription Sub() => Subscription.StartTrial(Tenant, Now.AddDays(-31), "TranslationOffice");
    private static SubscriptionPurchaseOrder Order(decimal? amount = null) => SubscriptionPurchaseOrder.Create(Sub(), Plan with { PriceTry = amount }, "TranslationOffice", User, Guid.NewGuid(), Now);

    [Theory] [InlineData("Owner", true)] [InlineData("Administrator", true)] [InlineData("Translator", false)] [InlineData("Reviewer", false)] [InlineData("Unknown", false)]
    public void Financial_drafts_are_managers_only(string role, bool allowed)
        => Assert.Equal(allowed, PurchaseOrderService.CanManage(new(Tenant, User, "TranslationOffice", role)));

    [Fact] public void Unpriced_draft_is_not_free_paid_or_a_subscription_change()
    {
        var sub = Sub(); var order = SubscriptionPurchaseOrder.Create(sub, Plan, "TranslationOffice", User, Guid.NewGuid(), Now);
        Assert.Null(order.AmountTry); Assert.Equal("TRY", order.Currency);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status); Assert.Equal("Purchase", order.Intent);
        Assert.Equal(0, sub.Version); Assert.Null(sub.PaidEndsAtUtc);
        Assert.Equal(SubscriptionStatus.Expired, sub.GetEffectiveStatus(Now));
    }

    [Fact] public void Price_snapshot_is_server_derived_and_does_not_follow_catalog_edits()
    {
        var plan = Plan with { PriceTry = 125.50m }; var order = SubscriptionPurchaseOrder.Create(Sub(), plan, "TranslationOffice", User, Guid.NewGuid(), Now);
        plan = plan with { PriceTry = 900m, Months = 2, Name = "Updated" };
        Assert.Equal(125.50m, order.AmountTry); Assert.Equal(1, order.Months); Assert.Equal("Office monthly", order.PlanName);
    }

    [Theory] [InlineData(-1, "Draft")] [InlineData(0, "Expired")] [InlineData(1, "Expired")]
    public void Exact_expiry_observation_never_persists_or_extends(int ticks, string status)
    {
        var order = Order(); var end = Now.AddMinutes(30);
        Assert.Equal(status, order.EffectiveStatus(end.AddTicks(ticks)).ToString());
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status); Assert.Equal(end, order.ExpiresAtUtc); Assert.Equal(0, order.Version);
    }

    [Fact] public void Cancel_is_idempotent_but_does_not_cancel_the_subscription()
    {
        var order = Order(); order.Cancel(User, 0, Now.AddMinutes(1)); order.Cancel(Guid.NewGuid(), 0, Now.AddMinutes(2));
        Assert.Equal(PurchaseOrderStatus.Canceled, order.Status); Assert.Equal(1, order.Version);
        Assert.Equal(User, order.CanceledByUserId); Assert.Equal(Now.AddMinutes(1), order.CanceledAtUtc);
    }

    [Fact] public void Expired_or_stale_cancellation_does_not_mutate()
    {
        var order = Order();
        Assert.Throws<PurchaseOrderException>(() => order.Cancel(User, 1, Now));
        Assert.Throws<PurchaseOrderException>(() => order.Cancel(User, 0, Now.AddMinutes(30)));
        Assert.Equal(0, order.Version); Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact] public void Invalid_type_disabled_plan_and_empty_identifiers_are_rejected()
    {
        Assert.Throws<PurchaseOrderException>(() => SubscriptionPurchaseOrder.Create(Sub(), Plan, "IndependentTranslator", User, Guid.NewGuid(), Now));
        Assert.Throws<PurchaseOrderException>(() => SubscriptionPurchaseOrder.Create(Sub(), Plan with { Enabled = false }, "TranslationOffice", User, Guid.NewGuid(), Now));
        Assert.Throws<PurchaseOrderException>(() => SubscriptionPurchaseOrder.Create(Sub(), Plan, "TranslationOffice", Guid.Empty, Guid.NewGuid(), Now));
        Assert.Throws<PurchaseOrderException>(() => SubscriptionPurchaseOrder.Create(Sub(), Plan, "TranslationOffice", User, Guid.Empty, Now));
    }

    [Theory] [InlineData("0")] [InlineData("-1")] [InlineData("12.001")] [InlineData("10000000000000000")]
    public void Invalid_lira_prices_fail_configuration(string value)
        => Assert.Throws<ArgumentException>(() => new BillingPlanCatalog([Plan with { PriceTry = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) }]));

    [Fact] public void Replay_identity_cannot_be_rebound_to_a_plan_or_another_actor()
    {
        var order = Order(); Assert.True(order.Matches(User, Plan.Code));
        Assert.False(order.Matches(Guid.NewGuid(), Plan.Code)); Assert.False(order.Matches(User, "other"));
    }

    [Fact] public void Valid_bff_draft_and_catalog_reject_other_tenants_or_payment_claims()
    {
        var order = Order(); var view = new PurchaseOrderView(order.Id, Tenant, order.PlanCode, order.PlanName, order.Months,
            order.AmountTry, "TRY", order.Intent, "Draft", Now, Now.AddMinutes(30), null, 0, false, false, Now);
        Assert.True(view.IsValidFor(Tenant)); Assert.False(view.IsValidFor(Guid.NewGuid()));
        Assert.False((view with { Currency = "USD" }).IsValidFor(Tenant));
        Assert.False((view with { PaymentReceived = true }).IsValidFor(Tenant));
        Assert.False((view with { CheckoutEnabled = true }).IsValidFor(Tenant));
        Assert.False((view with { Status = "Paid" }).IsValidFor(Tenant));
        Assert.False((view with { ExpiresAtUtc = Now.AddDays(1) }).IsValidFor(Tenant));
        var tenant = new TenantView(Tenant, "Office", "TranslationOffice", "Owner");
        var catalog = new PurchasePlanCatalogView(Tenant, tenant.Type, true, "TRY", "UTC", false, false,
            [new(Plan.Code, Plan.Name, Plan.TenantType, 1, true, null)]);
        Assert.True(catalog.IsValidFor(tenant)); Assert.False((catalog with { PurchaseEnabled = true }).IsValidFor(tenant));
        Assert.False((catalog with { PricingConfigured = true }).IsValidFor(tenant));
        Assert.False((catalog with { TenantType = "IndependentTranslator" }).IsValidFor(tenant));
    }
}
