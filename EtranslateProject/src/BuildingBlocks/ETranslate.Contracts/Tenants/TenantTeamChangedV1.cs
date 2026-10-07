namespace ETranslate.Contracts.Tenants;

public sealed record TenantTeamChangedV1(Guid EventId, Guid TenantId, long TeamVersion, Guid ActorUserId,
    Guid TargetUserId, Guid? InvitationId, string Action, string? PreviousRole, string? Role, DateTimeOffset OccurredAtUtc);
