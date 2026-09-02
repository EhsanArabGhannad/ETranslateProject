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

    public static TenantMembership CreateOwner(
        Guid tenantId,
        Guid userId,
        DateTimeOffset joinedAtUtc) =>
        new(tenantId, userId, TenantRole.Owner, joinedAtUtc);
}
