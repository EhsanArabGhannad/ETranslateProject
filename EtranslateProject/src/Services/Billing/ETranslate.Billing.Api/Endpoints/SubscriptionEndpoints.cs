using ETranslate.Billing.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Endpoints;

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/internal/v1/subscriptions/{tenantId:guid}", GetSubscriptionAsync)
            .WithTags("Internal Billing");

        return endpoints;
    }

    private static async Task<IResult> GetSubscriptionAsync(
        Guid tenantId,
        BillingDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var subscription = await database.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (subscription is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new SubscriptionResponse(
            subscription.TenantId,
            subscription.PlanCode,
            subscription.GetEffectiveStatus(timeProvider.GetUtcNow()),
            subscription.TrialStartedAtUtc,
            subscription.TrialEndsAtUtc));
    }
}

public sealed record SubscriptionResponse(
    Guid TenantId,
    string PlanCode,
    Subscriptions.SubscriptionStatus Status,
    DateTimeOffset? TrialStartedAtUtc,
    DateTimeOffset? TrialEndsAtUtc);
