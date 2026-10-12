using ETranslate.Billing.Api.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Subscriptions;

public sealed record PaidPeriodApplicationResult(Guid PeriodId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, bool AlreadyApplied);

public sealed class PaidPeriodApplicationService(IDbContextFactory<BillingDbContext> contexts, BillingPlanCatalog plans, TimeProvider clock)
{
    // Only a future verified server-side payment adapter may call this. Never bind a client POST to it.
    public async Task<PaidPeriodApplicationResult> ApplyConfirmedAsync(ConfirmedSubscriptionPayment payment, CancellationToken ct)
    {
        payment.Validate(clock.GetUtcNow());
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var previous = await db.PaidPeriods.AsNoTracking().SingleOrDefaultAsync(p =>
                p.Provider == payment.Provider && p.PaymentReference == payment.PaymentReference, ct);
            if (previous is not null)
            {
                if (!previous.Matches(payment)) throw new InvalidOperationException("Payment reference conflicts with its original allocation.");
                return new(previous.Id, previous.StartsAtUtc, previous.EndsAtUtc, true);
            }
            var plan = plans.GetEnabled(payment.PlanCode);
            var subscription = await db.Subscriptions.SingleOrDefaultAsync(s => s.TenantId == payment.TenantId, ct)
                ?? throw new InvalidOperationException("Subscription onboarding is incomplete.");
            var now = clock.GetUtcNow();
            var period = subscription.AllocatePaidPeriod(plan, now);
            var record = PaidSubscriptionPeriod.Create(subscription, plan, payment, now, period.Start, period.End);
            db.PaidPeriods.Add(record);
            try
            {
                // EF commits subscription/version and immutable period together in one transaction.
                await db.SaveChangesAsync(ct);
                return new(record.Id, record.StartsAtUtc, record.EndsAtUtc, false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3) { }
            catch (DbUpdateException error) when (attempt < 3 && error.InnerException is SqlException { Number: 2601 or 2627 }) { }
        }
        throw new InvalidOperationException("Concurrent period allocation requires reconciliation.");
    }
}
