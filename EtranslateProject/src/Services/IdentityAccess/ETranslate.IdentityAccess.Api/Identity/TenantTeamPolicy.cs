namespace ETranslate.IdentityAccess.Api.Identity;

public static class TenantTeamPolicy
{
    // Reviewer is the existing read-only observer role, not an approval permission.
    public static bool IsMemberRole(TenantRole role) => role is TenantRole.Administrator or TenantRole.Translator or TenantRole.Reviewer;
    public static bool CanManage(TenantMembership actor) => actor.IsActive && (actor.Role is TenantRole.Owner or TenantRole.Administrator);
    public static bool CanAssign(TenantMembership actor, TenantRole role) => CanManage(actor) && IsMemberRole(role) &&
        (actor.Role == TenantRole.Owner || role is TenantRole.Translator or TenantRole.Reviewer);
    public static bool CanChange(TenantMembership actor, TenantMembership target, TenantRole newRole) =>
        actor.TenantId == target.TenantId && actor.UserId != target.UserId &&
        CanAssign(actor, target.Role) && CanAssign(actor, newRole);
}
