using ETranslate.SubscriptionAccess;

namespace ETranslate.Web.Models;

public sealed class SettingsPageView
{
    public List<TenantView> Tenants { get; set; } = [];
    public TenantView? Tenant { get; set; }
    public List<TemplateView> Templates { get; set; } = [];
    public SubscriptionWriteStatus WriteStatus { get; set; } = SubscriptionWriteStatus.Unavailable;
    public bool CanWrite => WriteStatus == SubscriptionWriteStatus.Allowed && Tenant?.Role is "Owner" or "Administrator" or "Translator";
    public TemplateDetail? Selected { get; set; }
    public TemplateEditForm Form { get; set; } = new();
    public TemplateProjection Projection { get; set; } = TemplateProjection.From(null, Guid.Empty, out _);
    public string? Message { get; set; }
    public bool Conflict { get; set; }
    public bool CanSave => CanWrite && !Conflict && Selected?.IsActive != false && (Selected?.Revision.ContentDescriptor.IsSupported ?? true);
}
