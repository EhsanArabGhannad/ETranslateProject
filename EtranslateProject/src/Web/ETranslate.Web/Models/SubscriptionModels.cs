namespace ETranslate.Web.Models;

public sealed record SubscriptionView(Guid TenantId, string? PlanCode, string Status,
    DateTimeOffset? TrialStartedAtUtc, DateTimeOffset? TrialEndsAtUtc, DateTimeOffset ObservedAtUtc, int? RemainingTrialDays,
    string? PaidPlanCode = null, string? PaidPlanName = null, DateTimeOffset? PaidStartsAtUtc = null,
    DateTimeOffset? PaidEndsAtUtc = null, int? PaidMonths = null, int? RemainingPaidDays = null)
{
    public bool IsValidFor(Guid tenantId)
    {
        if (tenantId == Guid.Empty || TenantId != tenantId || ObservedAtUtc == default) return false;
        var noPaid = PaidPlanCode is null && PaidPlanName is null && PaidStartsAtUtc is null && PaidEndsAtUtc is null && PaidMonths is null && RemainingPaidDays is null;
        if (Status == "Pending") return PlanCode is null && TrialStartedAtUtc is null && TrialEndsAtUtc is null && RemainingTrialDays is null && noPaid;
        if (Status is not ("Trialing" or "Active" or "PastDue" or "Canceled" or "Expired") || string.IsNullOrWhiteSpace(PlanCode) || PlanCode.Length > 80) return false;
        if (!noPaid)
        {
            if (string.IsNullOrWhiteSpace(PaidPlanCode) || PaidPlanCode.Length > 80 || PaidPlanCode == "trial" ||
                string.IsNullOrWhiteSpace(PaidPlanName) || PaidPlanName.Length > 200 || PaidMonths is not (> 0 and <= 1200) ||
                PaidStartsAtUtc is not { } paidStart || PaidEndsAtUtc is not { } paidEnd ||
                paidStart.Year > 9999 - (PaidMonths.Value / 12 + 1) || paidEnd != paidStart.AddMonths(PaidMonths.Value)) return false;
            var paidDays = (int)Math.Max(0, Math.Ceiling((paidEnd - (ObservedAtUtc < paidStart ? paidStart : ObservedAtUtc)).TotalDays));
            if (RemainingPaidDays != paidDays || (Status == "Active" && !(paidStart <= ObservedAtUtc && ObservedAtUtc < paidEnd)) ||
                (Status == "Expired" && ObservedAtUtc < paidEnd)) return false;
        }
        if (Status == "Active" && noPaid) return false;
        if (PlanCode != "trial") return RemainingTrialDays is null && (Status != "Active" || PlanCode == PaidPlanCode);
        if (TrialStartedAtUtc is not { } start || TrialEndsAtUtc is not { } end || end - start != TimeSpan.FromDays(30)) return false;
        var days = Status == "Trialing" ? (int)Math.Clamp(Math.Ceiling((end - ObservedAtUtc).TotalDays), 0, 30) : 0;
        return RemainingTrialDays == days && (Status != "Trialing" || end > ObservedAtUtc) && (Status != "Expired" || end <= ObservedAtUtc) &&
            (noPaid || Status != "Trialing" || PaidStartsAtUtc == end);
    }
    public string StatusLabel => Status switch
    {
        "Pending" => UiText.Get("AbonelikOlusturulmasiBekleniyor"), "Trialing" => UiText.Get("DenemeDonemiEtkin"), "Active" => UiText.Get("Etkin"),
        "PastDue" => UiText.Get("OdemeGecikmis"), "Canceled" => UiText.Get("IptalEdildi"), "Expired" => UiText.Get("SuresiDoldu"), _ => UiText.Get("Bilinmiyor")
    };
    public static string IstanbulTime(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
public sealed class SubscriptionPageView
{
    public TenantView Tenant { get; set; } = null!;
    public SubscriptionView? Subscription { get; set; }
    public string? Error { get; set; }
}
