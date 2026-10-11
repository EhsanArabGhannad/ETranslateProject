namespace ETranslate.Web.Models;

public sealed record SubscriptionView(Guid TenantId, string? PlanCode, string Status,
    DateTimeOffset? TrialStartedAtUtc, DateTimeOffset? TrialEndsAtUtc, DateTimeOffset ObservedAtUtc, int? RemainingTrialDays)
{
    public bool IsValidFor(Guid tenantId)
    {
        if (tenantId == Guid.Empty || TenantId != tenantId || ObservedAtUtc == default) return false;
        if (Status == "Pending") return PlanCode is null && TrialStartedAtUtc is null && TrialEndsAtUtc is null && RemainingTrialDays is null;
        if (Status is not ("Trialing" or "Active" or "PastDue" or "Canceled" or "Expired") || string.IsNullOrWhiteSpace(PlanCode) || PlanCode.Length > 80) return false;
        if (PlanCode != "trial") return RemainingTrialDays is null;
        if (TrialStartedAtUtc is not { } start || TrialEndsAtUtc is not { } end || end - start != TimeSpan.FromDays(30)) return false;
        var days = Status == "Trialing" ? (int)Math.Clamp(Math.Ceiling((end - ObservedAtUtc).TotalDays), 0, 30) : 0;
        return RemainingTrialDays == days && (Status != "Trialing" || end > ObservedAtUtc) && (Status != "Expired" || end <= ObservedAtUtc);
    }
    public string StatusLabel => Status switch
    {
        "Pending" => "در انتظار ایجاد اشتراک", "Trialing" => "دورهٔ آزمایشی فعال", "Active" => "فعال",
        "PastDue" => "سررسید گذشته", "Canceled" => "لغوشده", "Expired" => "منقضی‌شده", _ => "نامشخص"
    };
    public static string IstanbulTime(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
public sealed class SubscriptionPageView
{
    public TenantView Tenant { get; set; } = null!;
    public SubscriptionView? Subscription { get; set; }
    public string? Error { get; set; }
}
