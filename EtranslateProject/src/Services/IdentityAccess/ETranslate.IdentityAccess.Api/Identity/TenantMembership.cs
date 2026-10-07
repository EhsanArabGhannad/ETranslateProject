namespace ETranslate.IdentityAccess.Api.Identity;

public sealed class TenantMembership
{
    private TenantMembership()
    {
    }

    private TenantMembership(
        Guid tenantId,
        Guid userId,
        TenantRole role,
        DateTimeOffset joinedAtUtc)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid TenantId { get; private init; }
    public Tenant Tenant { get; private init; } = null!;
    public Guid UserId { get; private init; }
    public ApplicationUser User { get; private init; } = null!;
    public TenantRole Role { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private init; }
    public bool IsActive { get; private set; } = true;

    public static TenantMembership CreateOwner(
        Guid tenantId,
        Guid userId,
        DateTimeOffset joinedAtUtc) =>
        new(tenantId, userId, TenantRole.Owner, joinedAtUtc);

    public static TenantMembership CreateMember(Guid tenantId, Guid userId, TenantRole role, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        if (!TenantTeamPolicy.IsMemberRole(role)) throw new ArgumentException("Owner cannot be assigned through team management.");
        return new(tenantId, userId, role, now);
    }
    public void ChangeRole(TenantRole role)
    {
        if (Role == TenantRole.Owner || !TenantTeamPolicy.IsMemberRole(role)) throw new ArgumentException("Owner is protected.");
        Role = role;
    }
    public void SetActive(bool active)
    {
        if (Role == TenantRole.Owner) throw new ArgumentException("Owner is protected.");
        IsActive = active;
    }
    public void Rejoin(TenantRole role)
    {
        if (IsActive) throw new InvalidOperationException("Membership is already active.");
        ChangeRole(role); IsActive = true;
    }
}
