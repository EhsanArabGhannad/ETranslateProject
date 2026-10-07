namespace ETranslate.IdentityAccess.Api.Identity;

public sealed class TenantTeamAudit
{
    private TenantTeamAudit() { }
    public TenantTeamAudit(Guid tenantId, long version, Guid actor, Guid target, Guid? invitationId,
        string action, TenantRole? previousRole, TenantRole? role, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); TenantId = tenantId; TeamVersion = version; ActorUserId = actor; TargetUserId = target;
        InvitationId = invitationId; Action = action; PreviousRole = previousRole; Role = role; OccurredAtUtc = now;
    }
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public long TeamVersion { get; private init; }
    public Guid ActorUserId { get; private init; }
    public Guid TargetUserId { get; private init; }
    public Guid? InvitationId { get; private init; }
    public string Action { get; private init; } = "";
    public TenantRole? PreviousRole { get; private init; }
    public TenantRole? Role { get; private init; }
    public DateTimeOffset OccurredAtUtc { get; private init; }
}
