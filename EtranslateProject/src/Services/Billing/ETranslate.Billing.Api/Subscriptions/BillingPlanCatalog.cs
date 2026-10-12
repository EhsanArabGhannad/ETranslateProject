namespace ETranslate.Billing.Api.Subscriptions;

public sealed record BillingPlan(string Code, string Name, string TenantType, int Months = 1, bool Enabled = true, decimal? PriceTry = null);

public sealed class BillingPlansOptions
{
    public List<BillingPlan> Plans { get; set; } = [];
}

public sealed class BillingPlanCatalog
{
    public IReadOnlyList<BillingPlan> Plans { get; }
    public BillingPlanCatalog(IEnumerable<BillingPlan> plans)
    {
        var values = plans.ToArray();
        if (values.Length > 100) throw new ArgumentException("Too many plan profiles.");
        foreach (var plan in values)
            if (string.IsNullOrWhiteSpace(plan.Code) || plan.Code.Length > 80 || plan.Code == Subscription.TrialPlanCode ||
                plan.Code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')) ||
                string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 200 ||
                plan.TenantType is not ("IndependentTranslator" or "TranslationOffice") || plan.Months is < 1 or > 12 ||
                plan.PriceTry is { } price && (price <= 0 || price > 9999999999999999.99m || decimal.Round(price, 2) != price))
                throw new ArgumentException("Invalid subscription plan configuration.");
        if (values.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Duplicate plan codes.");
        Plans = Array.AsReadOnly(values);
    }
    public BillingPlan GetEnabled(string code) => Plans.SingleOrDefault(p => p.Code == code && p.Enabled)
        ?? throw new InvalidOperationException("Plan is unavailable.");
}
