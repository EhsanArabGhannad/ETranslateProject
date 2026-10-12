namespace ETranslate.Web.Models;

public sealed record PurchasePlanView(string Code, string Name, string TenantType, int Months, bool Enabled, decimal? PriceTry)
{
    public bool IsValidFor(string type) => !string.IsNullOrWhiteSpace(Code) && Code.Length <= 80 && Code != "trial" &&
        !string.IsNullOrWhiteSpace(Name) && Name.Length <= 200 && TenantType == type && Enabled && Months is >= 1 and <= 12 &&
        (PriceTry is null || PriceTry > 0 && PriceTry <= 9999999999999999.99m && decimal.Round(PriceTry.Value, 2) == PriceTry);
}
public sealed record PurchasePlanCatalogView(Guid TenantId, string TenantType, bool EligibilityKnown, string Currency,
    string PeriodTimeZone, bool PurchaseEnabled, bool PricingConfigured, IReadOnlyList<PurchasePlanView> Plans)
{
    public bool IsValidFor(TenantView tenant) => TenantId == tenant.Id && TenantType == tenant.Type && EligibilityKnown &&
        Currency == "TRY" && PeriodTimeZone == "UTC" && !PurchaseEnabled && Plans is not null && Plans.Count <= 100 &&
        Plans.All(p => p is not null && p.IsValidFor(tenant.Type)) && Plans.Select(p => p.Code).Distinct().Count() == Plans.Count &&
        PricingConfigured == Plans.Any(p => p.PriceTry.HasValue);
}
public sealed record PurchaseOrderView(Guid Id, Guid TenantId, string PlanCode, string PlanName, int Months, decimal? AmountTry,
    string Currency, string Intent, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? CanceledAtUtc, long Version, bool CheckoutEnabled, bool PaymentReceived, DateTimeOffset ObservedAtUtc)
{
    public bool IsValidFor(Guid tenantId) => Id != Guid.Empty && TenantId == tenantId && !string.IsNullOrWhiteSpace(PlanCode) && PlanCode.Length <= 80 &&
        !string.IsNullOrWhiteSpace(PlanName) && PlanName.Length <= 200 && Months is >= 1 and <= 12 && Currency == "TRY" &&
        Intent is "Purchase" or "Renewal" && Status is "Draft" or "Canceled" or "Expired" && !CheckoutEnabled && !PaymentReceived &&
        CreatedAtUtc != default && ObservedAtUtc != default && ExpiresAtUtc - CreatedAtUtc == TimeSpan.FromMinutes(30) && Version >= 0 &&
        (AmountTry is null || AmountTry > 0 && AmountTry <= 9999999999999999.99m && decimal.Round(AmountTry.Value, 2) == AmountTry) &&
        (Status != "Draft" || ObservedAtUtc < ExpiresAtUtc && Version == 0 && CanceledAtUtc is null) &&
        (Status != "Expired" || ObservedAtUtc >= ExpiresAtUtc && CanceledAtUtc is null) &&
        (Status != "Canceled" || Version > 0 && CanceledAtUtc >= CreatedAtUtc && CanceledAtUtc < ExpiresAtUtc);
    public string StatusLabel => Status switch { "Draft" => "پیش‌سفارش · بدون پرداخت", "Canceled" => "لغوشده", _ => "منقضی‌شده" };
}
public sealed class PurchaseOrdersPageView
{
    public TenantView Tenant { get; set; } = null!;
    public PurchasePlanCatalogView Catalog { get; set; } = null!;
    public IReadOnlyList<PurchaseOrderView> Orders { get; set; } = [];
    public Guid RequestId { get; set; } = Guid.NewGuid();
}
