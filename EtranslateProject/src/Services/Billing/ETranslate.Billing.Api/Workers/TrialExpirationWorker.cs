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
                subscription.Status == Subscriptions.SubscriptionStatus.Trialing &&
                subscription.TrialEndsAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var subscription in elapsedTrials)
        {
            subscription.ExpireTrial(now);
        }

        if (elapsedTrials.Count == 0)
        {
            return;
        }

        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Expired {TrialCount} elapsed trial subscriptions.", elapsedTrials.Count);
    }
}
