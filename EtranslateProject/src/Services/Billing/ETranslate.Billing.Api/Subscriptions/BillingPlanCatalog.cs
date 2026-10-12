namespace ETranslate.Billing.Api.Subscriptions;

public sealed record BillingPlan(string Code, string Name, string TenantType, int Months = 1, bool Enabled = true);

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
        foreach (var plan in values)
            if (string.IsNullOrWhiteSpace(plan.Code) || plan.Code.Length > 80 || plan.Code == Subscription.TrialPlanCode ||
                plan.Code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')) ||
                string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 200 ||
                plan.TenantType is not ("IndependentTranslator" or "TranslationOffice") || plan.Months is < 1 or > 12)
                throw new ArgumentException("Invalid subscription plan configuration.");
        if (values.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Duplicate plan codes.");
        Plans = Array.AsReadOnly(values);
    }
    public BillingPlan GetEnabled(string code) => Plans.SingleOrDefault(p => p.Code == code && p.Enabled)
        ?? throw new InvalidOperationException("Plan is unavailable.");
}
