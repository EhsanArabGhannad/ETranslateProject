using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Documents;

public sealed class AppNavigationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static TenantView Membership(string role = "Owner") => new(Tenant, "Office", "TranslationOffice", role);
    [Theory] [InlineData("Owner", true)] [InlineData("Administrator", true)] [InlineData("Translator", false)] [InlineData("Reviewer", false)]
    public void Manager_navigation_is_not_a_permission_grant(string role, bool manage)
    {
        var navigation = AppNavigation.From(new SettingsPageView { Tenant = Membership(role) }, "Settings");
        Assert.Equal(Tenant, navigation.TenantId); Assert.Equal(manage, navigation.CanManageTeam); Assert.Equal("settings", navigation.Section);
    }
    [Fact] public void All_protected_page_models_preserve_explicit_scope()
    {
        object[] pages = [new WorkspaceView { Tenant = Membership() }, new EditorView { Tenant = Membership(), TenantId = Tenant },
            new SettingsPageView { Tenant = Membership() }, new SubscriptionPageView { Tenant = Membership() },
            new PurchaseOrdersPageView { Tenant = Membership() }, new AssignmentPageView { TenantId = Tenant }, new SigningPageView { TenantId = Tenant }];
        Assert.All(pages, page => Assert.Equal(Tenant, AppNavigation.From(page, "Workspace").TenantId));
        Assert.Equal("tasks", AppNavigation.From(new WorkspaceView { Tenant = Membership(), AssignedToMe = true }, "Workspace").Section);
        Assert.Equal("subscription", AppNavigation.From(pages[4], "PurchaseOrders").Section);
    }
    [Fact] public void No_scope_or_unaccepted_invitation_does_not_leak_a_tenant_into_menu()
    {
        Assert.Null(AppNavigation.From(new LoginInput(), "Account").TenantId);
        Assert.Null(AppNavigation.From(new AcceptInvitationView { Invitation = new(Guid.NewGuid(), Tenant, "Foreign", "Translator", "Pending", DateTimeOffset.UtcNow.AddDays(7), true) }, "Team").TenantId);
        var empty = AppNavigation.From(new SettingsPageView { Tenant = Membership() with { Id = Guid.Empty } }, "Settings");
        Assert.Null(empty.TenantId); Assert.False(empty.CanManageTeam);
    }
}
