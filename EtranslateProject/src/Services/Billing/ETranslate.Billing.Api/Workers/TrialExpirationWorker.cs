using ETranslate.Billing.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Workers;

public sealed class TrialExpirationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<TrialExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireElapsedTrialsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), timeProvider, stoppingToken);
        }
    }

    private async Task ExpireElapsedTrialsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var now = timeProvider.GetUtcNow();

        var elapsedTrials = await database.Subscriptions
            .Where(subscription =>
                subscription.Status == Subscriptions.SubscriptionStatus.Trialing && subscription.TrialEndsAtUtc <= now ||
                subscription.Status == Subscriptions.SubscriptionStatus.Active &&
                (subscription.PaidEndsAtUtc == null || subscription.PaidEndsAtUtc <= now))
            .ToListAsync(cancellationToken);

        foreach (var subscription in elapsedTrials)
        {
            subscription.ExpireTrial(now);
        }

        if (elapsedTrials.Count == 0)
        {
            return;
        }

        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Subscription expiry raced with a period update; effective reads remain authoritative.");
            return;
        }
        logger.LogInformation("Expired {SubscriptionCount} elapsed subscriptions.", elapsedTrials.Count);
    }
}
