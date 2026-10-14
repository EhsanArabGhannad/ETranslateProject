using ETranslate.SubscriptionAccess;

namespace ETranslate.Web.Models;

public sealed class SettingsPageView
{
    public List<TenantView> Tenants { get; set; } = [];
    public TenantView? Tenant { get; set; }
    public List<TemplateView> Templates { get; set; } = [];
    public SubscriptionWriteStatus WriteStatus { get; set; } = SubscriptionWriteStatus.Unavailable;
    public bool CanWrite => WriteStatus == SubscriptionWriteStatus.Allowed && Tenant?.Role is "Owner" or "Administrator" or "Translator";
}
