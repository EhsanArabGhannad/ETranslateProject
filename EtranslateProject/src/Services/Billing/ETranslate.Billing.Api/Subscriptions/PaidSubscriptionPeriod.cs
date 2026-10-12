namespace ETranslate.Billing.Api.Subscriptions;

// Immutable allocation from a trusted adapter. A reference alone is NOT payment verification.
public sealed class PaidSubscriptionPeriod
{
    private PaidSubscriptionPeriod() { }
    public Guid Id { get; private init; }
    public Guid SubscriptionId { get; private init; }
    public Guid TenantId { get; private init; }
    public string Provider { get; private init; } = "";
    public string PaymentReference { get; private init; } = "";
    public string PlanCode { get; private init; } = "";
    public string PlanName { get; private init; } = "";
    public int Months { get; private init; }
    public DateTimeOffset ConfirmedAtUtc { get; private init; }
    public DateTimeOffset AppliedAtUtc { get; private init; }
    public DateTimeOffset StartsAtUtc { get; private init; }
    public DateTimeOffset EndsAtUtc { get; private init; }
    public static PaidSubscriptionPeriod Create(Subscription subscription, BillingPlan plan, ConfirmedSubscriptionPayment payment,
        DateTimeOffset appliedAt, DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = Guid.NewGuid(), SubscriptionId = subscription.Id, TenantId = subscription.TenantId,
        Provider = payment.Provider, PaymentReference = payment.PaymentReference, PlanCode = plan.Code,
        PlanName = plan.Name, Months = plan.Months, ConfirmedAtUtc = payment.ConfirmedAtUtc.ToUniversalTime(),
        AppliedAtUtc = appliedAt, StartsAtUtc = start, EndsAtUtc = end
    };
    public bool Matches(ConfirmedSubscriptionPayment payment) => TenantId == payment.TenantId && Provider == payment.Provider &&
        PaymentReference == payment.PaymentReference && PlanCode == payment.PlanCode && ConfirmedAtUtc == payment.ConfirmedAtUtc;
}

// In-process trusted input only. No HTTP route, message consumer or public activation token exists.
public sealed record ConfirmedSubscriptionPayment(Guid TenantId, string PlanCode, string Provider,
    string PaymentReference, DateTimeOffset ConfirmedAtUtc)
{
    public void Validate(DateTimeOffset now)
    {
        if (TenantId == Guid.Empty || string.IsNullOrWhiteSpace(PlanCode) || PlanCode.Length > 80 ||
            string.IsNullOrWhiteSpace(Provider) || Provider.Length > 40 || Provider.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) ||
            string.IsNullOrWhiteSpace(PaymentReference) || PaymentReference.Length > 128 || PaymentReference != PaymentReference.Trim() ||
            PaymentReference.Any(char.IsControl) || ConfirmedAtUtc == default || ConfirmedAtUtc > now.AddMinutes(5))
            throw new ArgumentException("Invalid trusted payment allocation input.");
    }
}
