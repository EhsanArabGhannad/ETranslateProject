namespace ETranslate.Billing.Api.Subscriptions;

public sealed class Subscription
{
    public const int TrialLengthInDays = 30;
    public const string TrialPlanCode = "trial";

    private Subscription()
    {
    }

    private Subscription(Guid tenantId, DateTimeOffset startedAtUtc)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        PlanCode = TrialPlanCode;
        Status = SubscriptionStatus.Trialing;
        TrialStartedAtUtc = startedAtUtc;
        TrialEndsAtUtc = startedAtUtc.AddDays(TrialLengthInDays);
        CreatedAtUtc = startedAtUtc;
        UpdatedAtUtc = startedAtUtc;
    }

    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public string PlanCode { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset? TrialStartedAtUtc { get; private set; }
    public DateTimeOffset? TrialEndsAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string? TenantType { get; private set; }
    public string? PaidPlanName { get; private set; }
    public DateTimeOffset? PaidStartsAtUtc { get; private set; }
    public DateTimeOffset? PaidEndsAtUtc { get; private set; }
    public DateTimeOffset? BillingAnchorUtc { get; private set; }
    public int PaidMonths { get; private set; }
    public long Version { get; private set; }

    public static Subscription StartTrial(Guid tenantId, DateTimeOffset startedAtUtc, string? tenantType = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (tenantType is not (null or "IndependentTranslator" or "TranslationOffice")) throw new ArgumentException("Invalid tenant type.");
        return new Subscription(tenantId, startedAtUtc) { TenantType = tenantType };
    }

    public bool HasValidPaidWindow => PlanCode != TrialPlanCode && !string.IsNullOrWhiteSpace(PaidPlanName) &&
        PaidMonths is > 0 and <= 1200 && BillingAnchorUtc is { } anchor && PaidStartsAtUtc == anchor &&
        PaidEndsAtUtc is { } end && end > anchor && anchor.Year <= 9999 - (PaidMonths / 12 + 1) && end == anchor.AddMonths(PaidMonths);

    public SubscriptionStatus GetEffectiveStatus(DateTimeOffset nowUtc)
    {
        if (Status == SubscriptionStatus.Active)
        {
            if (!HasValidPaidWindow || PaidEndsAtUtc <= nowUtc) return SubscriptionStatus.Expired;
            if (PaidStartsAtUtc <= nowUtc) return SubscriptionStatus.Active;
            // A prepaid period starts after, not instead of, the remaining free trial.
            return TrialStartedAtUtc <= nowUtc && nowUtc < TrialEndsAtUtc && TrialEndsAtUtc == PaidStartsAtUtc
                ? SubscriptionStatus.Trialing : SubscriptionStatus.Expired;
        }
        return Status == SubscriptionStatus.Trialing && TrialEndsAtUtc <= nowUtc
            ? SubscriptionStatus.Expired : Status;
    }

    public (DateTimeOffset Start, DateTimeOffset End) AllocatePaidPeriod(BillingPlan plan, DateTimeOffset now)
    {
        _ = new BillingPlanCatalog([plan]);
        if (!plan.Enabled || TenantType is null || TenantType != plan.TenantType) throw new InvalidOperationException("Tenant type must be reconciled and match the enabled plan.");
        if (Status is SubscriptionStatus.Canceled or SubscriptionStatus.PastDue) throw new InvalidOperationException("Canceled or disputed subscriptions require reconciliation.");
        now = now.ToUniversalTime();
        var continuing = HasValidPaidWindow && PaidEndsAtUtc > now;
        if (continuing && PlanCode != plan.Code) throw new InvalidOperationException("Mid-period plan changes are not supported.");
        var anchor = continuing ? BillingAnchorUtc!.Value : GetEffectiveStatus(now) == SubscriptionStatus.Trialing ? TrialEndsAtUtc!.Value.ToUniversalTime() : now;
        var oldMonths = continuing ? PaidMonths : 0;
        var newMonths = checked(oldMonths + plan.Months);
        if (newMonths > 1200) throw new InvalidOperationException("Paid period limit exceeded.");
        var start = continuing ? PaidEndsAtUtc!.Value : anchor;
        var end = anchor.AddMonths(newMonths); // Always use the original day anchor: Jan 31 -> Feb 28 -> Mar 31.
        var nextVersion = checked(Version + 1);
        PlanCode = plan.Code; PaidPlanName = plan.Name; Status = SubscriptionStatus.Active;
        BillingAnchorUtc = anchor; PaidStartsAtUtc = anchor; PaidEndsAtUtc = end; PaidMonths = newMonths;
        UpdatedAtUtc = now; Version = nextVersion;
        return (start, end);
    }

    public bool ExpireTrial(DateTimeOffset nowUtc)
    {
        if (GetEffectiveStatus(nowUtc) != SubscriptionStatus.Expired ||
            Status == SubscriptionStatus.Expired)
        {
            return false;
        }

        Status = SubscriptionStatus.Expired;
        UpdatedAtUtc = nowUtc;
        Version = checked(Version + 1);
        return true;
    }
}
