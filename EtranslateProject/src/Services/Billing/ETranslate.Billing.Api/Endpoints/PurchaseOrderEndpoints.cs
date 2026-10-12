using System.Text.Json.Serialization;
using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Orders;
using ETranslate.Billing.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Endpoints;

public static class PurchaseOrderEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/subscription/orders").WithTags("Purchase drafts (no payment)");
        group.MapPost("/", CreateAsync); group.MapGet("/", ListAsync); group.MapGet("/{orderId:guid}", GetAsync);
        group.MapPost("/{orderId:guid}/cancel", CancelAsync);
        return endpoints;
    }
    private static async Task<SubscriptionActorResult> Authorize(Guid tenantId, HttpContext context, SubscriptionAccessClient access, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var result = await access.GetActorAsync(tenantId, context.Request.Headers.Authorization.ToString(), ct);
        return result.Status == 200 && !PurchaseOrderService.CanManage(result.Actor!) ? new(403, null) : result;
    }
    private static IResult Error(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
    private static IResult AccessError(int status) => status == 503 ? Error(503, "purchase_order_access_unavailable") : Results.StatusCode(status);
    private static object Response(SubscriptionPurchaseOrder order, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new { order.Id, order.TenantId, order.PlanCode, order.PlanName, order.Months, order.AmountTry, order.Currency, order.Intent,
            status = order.EffectiveStatus(now).ToString(), order.CreatedAtUtc, order.ExpiresAtUtc,
            order.CanceledAtUtc, order.Version, checkoutEnabled = false, paymentReceived = false, observedAtUtc = now };
    }
    private static async Task<IResult> CreateAsync(Guid tenantId, CreatePurchaseOrderRequest request, HttpContext context,
        SubscriptionAccessClient access, PurchaseOrderService orders, TimeProvider clock, CancellationToken ct)
    {
        var actor = await Authorize(tenantId, context, access, ct); if (actor.Status != 200) return AccessError(actor.Status);
        try
        {
            var result = await orders.CreateAsync(actor.Actor!, request.PlanCode, request.RequestId, ct);
            return Results.Json(Response(result.Order, clock), statusCode: result.Replayed ? 200 : 201);
        }
        catch (PurchaseOrderException error) { return Error(error.Status, error.Code); }
    }
    private static async Task<IResult> ListAsync(Guid tenantId, HttpContext context, SubscriptionAccessClient access,
        BillingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await Authorize(tenantId, context, access, ct); if (actor.Status != 200) return AccessError(actor.Status);
        var orders = await db.PurchaseOrders.AsNoTracking().Where(o => o.TenantId == tenantId).OrderByDescending(o => o.CreatedAtUtc).ThenBy(o => o.Id).Take(50).ToListAsync(ct);
        return Results.Ok(orders.Select(o => Response(o, clock)));
    }
    private static async Task<IResult> GetAsync(Guid tenantId, Guid orderId, HttpContext context, SubscriptionAccessClient access,
        BillingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await Authorize(tenantId, context, access, ct); if (actor.Status != 200) return AccessError(actor.Status);
        var order = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, ct);
        return order is null ? Results.NotFound() : Results.Ok(Response(order, clock));
    }
    private static async Task<IResult> CancelAsync(Guid tenantId, Guid orderId, CancelPurchaseOrderRequest request,
        HttpContext context, SubscriptionAccessClient access, PurchaseOrderService orders, TimeProvider clock, CancellationToken ct)
    {
        var actor = await Authorize(tenantId, context, access, ct); if (actor.Status != 200) return AccessError(actor.Status);
        try { return Results.Ok(Response(await orders.CancelAsync(actor.Actor!, orderId, request.ExpectedVersion, ct), clock)); }
        catch (PurchaseOrderException error) { return Error(error.Status, error.Code); }
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePurchaseOrderRequest(string PlanCode, Guid RequestId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelPurchaseOrderRequest(long ExpectedVersion);
