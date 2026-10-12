using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Subscriptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Orders;

public sealed record PurchaseOrderCreation(SubscriptionPurchaseOrder Order, bool Replayed);

public sealed class PurchaseOrderService(IDbContextFactory<BillingDbContext> contexts, BillingPlanCatalog catalog, TimeProvider clock)
{
    public static bool CanManage(SubscriptionActor actor) => actor.IsValid(actor.TenantId) && actor.Role is "Owner" or "Administrator";
    public async Task<PurchaseOrderCreation> CreateAsync(SubscriptionActor actor, string planCode, Guid requestId, CancellationToken ct)
    {
        if (!CanManage(actor)) throw new PurchaseOrderException(403, "purchase_order_forbidden");
        if (requestId == Guid.Empty || string.IsNullOrWhiteSpace(planCode) || planCode.Length > 80)
            throw new PurchaseOrderException(400, "purchase_order_invalid_request");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var previous = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.TenantId == actor.TenantId && o.RequestId == requestId, ct);
            if (previous is not null)
            {
                if (!previous.Matches(actor.UserId, planCode)) throw new PurchaseOrderException(409, "purchase_order_request_conflict");
                return new(previous, true);
            }
            BillingPlan plan;
            try { plan = catalog.GetEnabled(planCode); }
            catch (InvalidOperationException) { throw new PurchaseOrderException(409, "purchase_order_plan_unavailable"); }
            var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == actor.TenantId, ct)
                ?? throw new PurchaseOrderException(503, "subscription_pending");
            var order = SubscriptionPurchaseOrder.Create(subscription, plan, actor.TenantType, actor.UserId, requestId, clock.GetUtcNow());
            db.PurchaseOrders.Add(order);
            try { await db.SaveChangesAsync(ct); return new(order, false); }
            catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (attempt == 3) throw new PurchaseOrderException(409, "purchase_order_request_conflict");
            }
        }
        throw new PurchaseOrderException(409, "purchase_order_request_conflict");
    }
    public async Task<SubscriptionPurchaseOrder> CancelAsync(SubscriptionActor actor, Guid id, long expectedVersion, CancellationToken ct)
    {
        if (!CanManage(actor)) throw new PurchaseOrderException(403, "purchase_order_forbidden");
        await using var db = await contexts.CreateDbContextAsync(ct);
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(o => o.Id == id && o.TenantId == actor.TenantId, ct)
            ?? throw new PurchaseOrderException(404, "purchase_order_not_found");
        order.Cancel(actor.UserId, expectedVersion, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new PurchaseOrderException(409, "purchase_order_changed"); }
        return order;
    }
}
