namespace ETranslate.Web.Models;

// View models are populated by protected server APIs. Query strings alone are not navigation context.
public sealed record AppNavigation(Guid? TenantId, string? TenantName, bool CanManageTeam, string Section)
{
    public static AppNavigation From(object? model, string? controller)
    {
        var tenant = model switch
        {
            WorkspaceView page => page.Tenant,
            SettingsPageView page => page.Tenant,
            EditorView page => page.Tenant,
            SubscriptionPageView page => page.Tenant,
            PurchaseOrdersPageView page => page.Tenant,
            _ => null
        };
        Guid? id = tenant?.Id;
        string? name = tenant?.Name;
        var manage = tenant?.Role is "Owner" or "Administrator";
        if (model is TeamPageView { Team: { } team }) { id = team.TenantId; name = team.TenantName; manage = team.Role is "Owner" or "Administrator"; }
        if (model is AssignmentPageView assignment) { id = assignment.TenantId; manage = assignment.Assignment?.CanManage == true; }
        if (model is SigningPageView signing) { id = signing.TenantId; manage = signing.State?.CanManage == true; }
        var section = controller switch
        {
            "Settings" => "settings", "Team" => "team", "Subscription" or "PurchaseOrders" => "subscription",
            "Workspace" when model is WorkspaceView { AssignedToMe: true } => "tasks",
            "Workspace" => "jobs", _ => ""
        };
        return id is null || id == Guid.Empty ? new(null, null, false, section) : new(id, name, manage, section);
    }
}
