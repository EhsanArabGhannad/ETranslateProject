using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Endpoints;

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenants/{tenantId:guid}/subscription", GetSubscriptionAsync)
            .WithTags("Subscriptions");
        // Compatibility alias, protected by the same live tenant membership check.
        endpoints.MapGet("/internal/v1/subscriptions/{tenantId:guid}", GetSubscriptionAsync)
            .WithTags("Internal Billing");

        return endpoints;
    }

    private static async Task<IResult> GetSubscriptionAsync(
        Guid tenantId,
        HttpContext context,
        SubscriptionAccessClient access,
        BillingDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var authorized = await access.CheckAsync(tenantId, context.Request.Headers.Authorization.ToString(), cancellationToken);
        if (authorized != 200) return authorized == 503
            ? Results.Problem(statusCode: 503, title: "subscription_access_service_unavailable") : Results.StatusCode(authorized);
        var subscription = await database.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        var snapshot = SubscriptionSnapshot.Observe(tenantId, subscription, timeProvider.GetUtcNow());
        return subscription is null ? Results.Json(snapshot, statusCode: 202) : Results.Ok(snapshot);
    }
}
