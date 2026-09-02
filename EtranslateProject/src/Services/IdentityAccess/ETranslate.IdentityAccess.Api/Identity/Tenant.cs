namespace ETranslate.IdentityAccess.Api.Identity;

public sealed class Tenant
{
    private Tenant()
    {
    }

    private Tenant(Guid id, string name, string slug, TenantType type, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Type = type;
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public Guid Id { get; private init; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantType Type { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public bool IsActive { get; private set; }

    public ICollection<TenantMembership> Memberships { get; } = new List<TenantMembership>();

    public static Tenant Create(string name, string slug, TenantType type, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return new Tenant(Guid.NewGuid(), name.Trim(), slug, type, createdAtUtc);
    }
}
