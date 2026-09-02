namespace ETranslate.Contracts.Tenants;

public sealed record TenantCreatedV1(
    Guid EventId,
    Guid TenantId,
    Guid OwnerUserId,
    string TenantType,
    DateTimeOffset OccurredAtUtc);
