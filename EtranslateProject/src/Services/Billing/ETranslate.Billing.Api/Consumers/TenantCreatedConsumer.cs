using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Contracts.Billing;
using ETranslate.Contracts.Tenants;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Consumers;

public sealed class TenantCreatedConsumer(
    BillingDbContext database,
    TimeProvider timeProvider) : IConsumer<TenantCreatedV1>
{
    public async Task Consume(ConsumeContext<TenantCreatedV1> context)
    {
        var tenantId = context.Message.TenantId;
        if (await database.Subscriptions.AnyAsync(
                subscription => subscription.TenantId == tenantId,
                context.CancellationToken))
        {
            return;
        }

        var startedAtUtc = context.Message.OccurredAtUtc;
        var occurredAtUtc = timeProvider.GetUtcNow();
        var subscription = Subscription.StartTrial(tenantId, startedAtUtc);
        database.Subscriptions.Add(subscription);

        await context.Publish(
            new TrialStartedV1(
                Guid.NewGuid(),
                tenantId,
                subscription.TrialStartedAtUtc!.Value,
                subscription.TrialEndsAtUtc!.Value,
                occurredAtUtc),
            context.CancellationToken);

        await database.SaveChangesAsync(context.CancellationToken);
    }
}
