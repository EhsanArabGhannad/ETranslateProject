using Microsoft.AspNetCore.Identity;

namespace ETranslate.IdentityAccess.Api.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<TenantMembership> Memberships { get; } = new List<TenantMembership>();
}
