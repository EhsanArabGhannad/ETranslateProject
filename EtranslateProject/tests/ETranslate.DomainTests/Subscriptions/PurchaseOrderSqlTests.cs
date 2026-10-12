using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Orders;
using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class PurchaseOrderSqlTests
{
    private static readonly BillingPlan Plan = new("office-monthly", "Synthetic office", "TranslationOffice", PriceTry: 123.45m);
    private static BillingDbContext Db() => new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer("Server=.\\ESIMSSQLSERVER;Database=ETranslateBilling;Integrated Security=True;Encrypt=True;TrustServerCertificate=True").Options);
    private static PurchaseOrderService Service(BillingPlan? plan = null) => new(new Factory(), new([plan ?? Plan]), TimeProvider.System);
    private static async Task<SubscriptionActor> Fixture()
    {
        var actor = new SubscriptionActor(Guid.NewGuid(), Guid.NewGuid(), "TranslationOffice", "Owner");
        await using var db = Db(); Assert.False(db.Database.HasPendingModelChanges());
        db.Subscriptions.Add(Subscription.StartTrial(actor.TenantId, DateTimeOffset.UtcNow.AddDays(-31), actor.TenantType));
        await db.SaveChangesAsync(); return actor;
    }

    [LocalBillingSqlFact] public async Task Concurrent_replays_create_one_immutable_order_without_any_paid_grant()
    {
        var actor = await Fixture(); var request = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service().CreateAsync(actor, Plan.Code, request, default)));
        Assert.Single(results.Select(r => r.Order.Id).Distinct()); Assert.Equal(1, results.Count(r => !r.Replayed));
        var replay = await Service(Plan with { PriceTry = 999m, Enabled = false }).CreateAsync(actor, Plan.Code, request, default);
        Assert.Equal(123.45m, replay.Order.AmountTry); Assert.True(replay.Replayed);
        await using var db = Db(); Assert.Equal(1, await db.PurchaseOrders.CountAsync(o => o.TenantId == actor.TenantId));
        var subscription = await db.Subscriptions.SingleAsync(s => s.TenantId == actor.TenantId);
        Assert.Equal(0, subscription.Version); Assert.Null(subscription.PaidEndsAtUtc);
        Assert.Equal(0, await db.PaidPeriods.CountAsync(p => p.TenantId == actor.TenantId));
    }

    [LocalBillingSqlFact] public async Task Scope_and_actor_are_bound_and_cannot_cancel_another_tenant_order()
    {
        var actor = await Fixture(); var other = await Fixture(); var request = Guid.NewGuid();
        var order = (await Service().CreateAsync(actor, Plan.Code, request, default)).Order;
        Assert.Equal(409, (await Assert.ThrowsAsync<PurchaseOrderException>(() => Service().CreateAsync(actor with { UserId = Guid.NewGuid() }, Plan.Code, request, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<PurchaseOrderException>(() => Service().CancelAsync(other, order.Id, 0, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchaseOrderException>(() => Service().CreateAsync(actor with { Role = "Translator" }, Plan.Code, Guid.NewGuid(), default))).Status);
        var own = await Service().CreateAsync(other, Plan.Code, request, default);
        Assert.NotEqual(order.Id, own.Order.Id);
    }

    [LocalBillingSqlFact] public async Task Cancel_and_replay_never_reopen_or_extend_an_order()
    {
        var actor = await Fixture(); var request = Guid.NewGuid(); var order = (await Service().CreateAsync(actor, Plan.Code, request, default)).Order;
        var canceled = await Service().CancelAsync(actor, order.Id, 0, default);
        Assert.Equal(PurchaseOrderStatus.Canceled, canceled.Status); Assert.Equal(1, canceled.Version);
        var repeat = await Service().CancelAsync(actor, order.Id, 0, default);
        var replay = await Service().CreateAsync(actor, Plan.Code, request, default);
        Assert.Equal(1, repeat.Version); Assert.Equal(PurchaseOrderStatus.Canceled, replay.Order.Status);
        Assert.Equal(order.ExpiresAtUtc, replay.Order.ExpiresAtUtc);
    }

    [LocalBillingSqlFact] public async Task Wrong_type_and_pending_onboarding_leave_no_order()
    {
        var actor = await Fixture();
        await Assert.ThrowsAsync<PurchaseOrderException>(() => Service(Plan with { TenantType = "IndependentTranslator" }).CreateAsync(actor, Plan.Code, Guid.NewGuid(), default));
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchaseOrderException>(() => Service().CreateAsync(actor with { TenantId = Guid.NewGuid() }, Plan.Code, Guid.NewGuid(), default))).Status);
        await using var db = Db(); Assert.False(await db.PurchaseOrders.AnyAsync(o => o.TenantId == actor.TenantId));
    }
    private sealed class Factory : IDbContextFactory<BillingDbContext>
    {
        public BillingDbContext CreateDbContext() => Db();
        public Task<BillingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Db());
    }
}
