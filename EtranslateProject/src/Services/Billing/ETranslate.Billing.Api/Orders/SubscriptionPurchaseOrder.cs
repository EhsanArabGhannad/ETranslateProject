using ETranslate.Billing.Api.Subscriptions;

namespace ETranslate.Billing.Api.Orders;

public enum PurchaseOrderStatus { Draft, Canceled, Expired }

// A nonpayable price snapshot, never a payment or entitlement grant.
public sealed class SubscriptionPurchaseOrder
{
    public const string OrderCurrency = "TRY";
    public const int LifetimeMinutes = 30;
    private SubscriptionPurchaseOrder() { }
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid SubscriptionId { get; private init; }
    public Guid RequestId { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public string PlanCode { get; private init; } = "";
    public string PlanName { get; private init; } = "";
    public string TenantType { get; private init; } = "";
    public int Months { get; private init; }
    public decimal? AmountTry { get; private init; }
    public string Currency { get; private init; } = OrderCurrency;
    public string Intent { get; private init; } = "";
    public PurchaseOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset ExpiresAtUtc { get; private init; }
    public DateTimeOffset? CanceledAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public long Version { get; private set; }

    public static SubscriptionPurchaseOrder Create(Subscription subscription, BillingPlan plan, string verifiedTenantType,
        Guid userId, Guid requestId, DateTimeOffset now)
    {
        _ = new BillingPlanCatalog([plan]);
        if (userId == Guid.Empty || requestId == Guid.Empty || !plan.Enabled || plan.TenantType != verifiedTenantType ||
            subscription.TenantType is not null && subscription.TenantType != verifiedTenantType)
            throw new PurchaseOrderException(409, "purchase_order_plan_not_eligible");
        return new()
        {
            Id = Guid.NewGuid(), TenantId = subscription.TenantId, SubscriptionId = subscription.Id, RequestId = requestId,
            CreatedByUserId = userId, PlanCode = plan.Code, PlanName = plan.Name, TenantType = verifiedTenantType,
            Months = plan.Months, AmountTry = plan.PriceTry, Currency = OrderCurrency,
            Intent = subscription.HasValidPaidWindow ? "Renewal" : "Purchase", Status = PurchaseOrderStatus.Draft,
            CreatedAtUtc = now.ToUniversalTime(), ExpiresAtUtc = now.ToUniversalTime().AddMinutes(LifetimeMinutes)
        };
    }
    public PurchaseOrderStatus EffectiveStatus(DateTimeOffset now) => Status == PurchaseOrderStatus.Draft && now >= ExpiresAtUtc ? PurchaseOrderStatus.Expired : Status;
    public bool Matches(Guid userId, string planCode) => CreatedByUserId == userId && PlanCode == planCode;
    public void Cancel(Guid userId, long expectedVersion, DateTimeOffset now)
    {
        if (userId == Guid.Empty || expectedVersion < 0) throw new PurchaseOrderException(400, "purchase_order_invalid_cancel");
        if (Status == PurchaseOrderStatus.Canceled) return;
        if (Version != expectedVersion || EffectiveStatus(now) != PurchaseOrderStatus.Draft)
            throw new PurchaseOrderException(409, "purchase_order_not_open");
        var next = checked(Version + 1);
        Status = PurchaseOrderStatus.Canceled; CanceledAtUtc = now.ToUniversalTime(); CanceledByUserId = userId; Version = next;
    }
}

public sealed class PurchaseOrderException(int status, string code) : Exception(code)
{ public int Status { get; } = status; public string Code { get; } = code; }
