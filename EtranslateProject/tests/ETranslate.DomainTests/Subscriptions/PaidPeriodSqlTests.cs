using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class LocalBillingSqlFactAttribute : FactAttribute
{
    public LocalBillingSqlFactAttribute()
    { if (Environment.GetEnvironmentVariable("ETRANSLATE_BILLING_SQL_TESTS") != "1") Skip = "Opt-in local native SQL fixtures only."; }
}
public sealed class LocalPaidUiFactAttribute : FactAttribute
{
    public LocalPaidUiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_BILLING_SQL_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("ETRANSLATE_PAID_UI_TENANT") is null) Skip = "Requires a new retained HTTP fixture from the local paid-period script.";
    }
}

public sealed class PaidPeriodSqlTests
{
    private static readonly BillingPlan Plan = new("office-monthly", "Synthetic office monthly", "TranslationOffice");
    private static readonly string Connection = "Server=.\\ESIMSSQLSERVER;Database=ETranslateBilling;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
    private static PaidPeriodApplicationService Service(BillingPlanCatalog? catalog = null) => new(new Factory(), catalog ?? new([Plan]), TimeProvider.System);
    private static BillingDbContext Db() => new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(Connection).Options);
    private static async Task<Guid> Fixture()
    {
        var tenant = Guid.NewGuid(); await using var db = Db();
        Assert.False(db.Database.HasPendingModelChanges());
        db.Subscriptions.Add(Subscription.StartTrial(tenant, DateTimeOffset.UtcNow.AddDays(-31), "TranslationOffice"));
        await db.SaveChangesAsync(); return tenant;
    }
    private static ConfirmedSubscriptionPayment Payment(Guid tenant) => new(tenant, Plan.Code, "fixture", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

    [LocalBillingSqlFact]
    public async Task Duplicate_confirmations_are_atomic_and_do_not_extend_again()
    {
        var tenant = await Fixture(); var payment = Payment(tenant);
        var first = await Service().ApplyConfirmedAsync(payment, default);
        var repeat = await Service(new([Plan with { Enabled = false }])).ApplyConfirmedAsync(payment, default);
        Assert.False(first.AlreadyApplied); Assert.True(repeat.AlreadyApplied);
        Assert.Equal(first.PeriodId, repeat.PeriodId); Assert.Equal(first.EndsAtUtc, repeat.EndsAtUtc);
        await using var db = Db(); var sub = await db.Subscriptions.SingleAsync(s => s.TenantId == tenant);
        Assert.Equal(1, sub.Version); Assert.Equal(first.EndsAtUtc, sub.PaidEndsAtUtc);
        Assert.Equal(1, await db.PaidPeriods.CountAsync(p => p.TenantId == tenant));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyConfirmedAsync(payment with { PlanCode = "different" }, default));
    }

    [LocalBillingSqlFact]
    public async Task Concurrent_identical_callbacks_allocate_exactly_once()
    {
        var tenant = await Fixture(); var payment = Payment(tenant);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service().ApplyConfirmedAsync(payment, default)));
        Assert.Single(results.Select(r => r.PeriodId).Distinct()); Assert.Equal(1, results.Count(r => !r.AlreadyApplied));
        await using var db = Db(); Assert.Equal(1, await db.PaidPeriods.CountAsync(p => p.TenantId == tenant));
        Assert.Equal(1, (await db.Subscriptions.SingleAsync(s => s.TenantId == tenant)).PaidMonths);
    }

    [LocalBillingSqlFact]
    public async Task Concurrent_different_confirmations_do_not_lose_a_month()
    {
        var tenant = await Fixture();
        await Task.WhenAll(Service().ApplyConfirmedAsync(Payment(tenant), default), Service().ApplyConfirmedAsync(Payment(tenant), default));
        await using var db = Db(); var sub = await db.Subscriptions.SingleAsync(s => s.TenantId == tenant);
        Assert.Equal(2, sub.PaidMonths); Assert.Equal(2, sub.Version);
        Assert.Equal(sub.BillingAnchorUtc!.Value.AddMonths(2), sub.PaidEndsAtUtc);
        Assert.Equal(2, await db.PaidPeriods.CountAsync(p => p.TenantId == tenant));
    }

    [LocalBillingSqlFact]
    public async Task Global_reference_scope_cannot_credit_another_tenant()
    {
        var first = await Fixture(); var second = await Fixture(); var payment = Payment(first);
        await Service().ApplyConfirmedAsync(payment, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyConfirmedAsync(payment with { TenantId = second }, default));
        await using var db = Db(); Assert.Equal(0, await db.PaidPeriods.CountAsync(p => p.TenantId == second));
        Assert.Equal(0, (await db.Subscriptions.SingleAsync(s => s.TenantId == second)).Version);
    }

    [LocalBillingSqlFact]
    public async Task Stale_expiration_worker_cannot_overwrite_a_new_paid_period()
    {
        var tenant = await Fixture(); await using var stale = Db();
        var old = await stale.Subscriptions.SingleAsync(s => s.TenantId == tenant); Assert.True(old.ExpireTrial(DateTimeOffset.UtcNow));
        await Service().ApplyConfirmedAsync(Payment(tenant), default);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var fresh = Db(); Assert.Equal(SubscriptionStatus.Active,
            (await fresh.Subscriptions.SingleAsync(s => s.TenantId == tenant)).GetEffectiveStatus(DateTimeOffset.UtcNow));
    }

    [LocalBillingSqlFact]
    public async Task Rejected_allocations_leave_no_payment_row_or_subscription_mutation()
    {
        var tenant = await Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new([Plan with { TenantType = "IndependentTranslator" }])).ApplyConfirmedAsync(Payment(tenant), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyConfirmedAsync(Payment(Guid.NewGuid()), default));
        await using var db = Db(); Assert.Equal(0, await db.PaidPeriods.CountAsync(p => p.TenantId == tenant));
        Assert.Null((await db.Subscriptions.SingleAsync(s => s.TenantId == tenant)).PaidStartsAtUtc);
    }

    [LocalPaidUiFact]
    public async Task Optional_new_http_fixture_is_allocated_through_the_internal_service()
    {
        var value = Environment.GetEnvironmentVariable("ETRANSLATE_PAID_UI_TENANT");
        var tenant = Guid.Parse(value!); await using var db = Db();
        var sub = await db.Subscriptions.SingleAsync(s => s.TenantId == tenant);
        Assert.Equal("TranslationOffice", sub.TenantType); Assert.Equal("trial", sub.PlanCode);
        Assert.True(sub.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-10));
        Assert.False(await db.PaidPeriods.AnyAsync(p => p.TenantId == tenant));
        var payment = Payment(tenant); var result = await Service().ApplyConfirmedAsync(payment, default);
        var repeated = await Service().ApplyConfirmedAsync(payment, default);
        Assert.Equal(result.PeriodId, repeated.PeriodId); Assert.True(repeated.AlreadyApplied);
    }

    private sealed class Factory : IDbContextFactory<BillingDbContext>
    {
        public BillingDbContext CreateDbContext() => Db();
        public Task<BillingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Db());
    }
}
