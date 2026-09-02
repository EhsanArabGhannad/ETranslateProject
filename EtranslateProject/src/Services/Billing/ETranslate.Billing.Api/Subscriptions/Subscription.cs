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

    public static Subscription StartTrial(Guid tenantId, DateTimeOffset startedAtUtc)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new Subscription(tenantId, startedAtUtc);
    }

    public SubscriptionStatus GetEffectiveStatus(DateTimeOffset nowUtc) =>
        Status == SubscriptionStatus.Trialing && TrialEndsAtUtc <= nowUtc
            ? SubscriptionStatus.Expired
            : Status;

    public bool ExpireTrial(DateTimeOffset nowUtc)
    {
        if (GetEffectiveStatus(nowUtc) != SubscriptionStatus.Expired ||
            Status == SubscriptionStatus.Expired)
        {
            return false;
        }

        Status = SubscriptionStatus.Expired;
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
