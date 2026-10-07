using System.Security.Cryptography;
using System.Text;

namespace ETranslate.IdentityAccess.Api.Identity;

public enum TenantInvitationStatus { Pending = 0, Accepted = 1, Cancelled = 2, Expired = 3 }

public sealed class TenantInvitation
{
    private TenantInvitation() { }
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid TargetUserId { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public TenantRole Role { get; private init; }
    public string TokenHash { get; private init; } = "";
    public TenantInvitationStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset ExpiresAtUtc { get; private init; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public static (TenantInvitation Invitation, string Token) Create(Guid tenantId, Guid targetUserId, Guid creator, TenantRole role, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(creator, Guid.Empty);
        if (!TenantTeamPolicy.IsMemberRole(role)) throw new ArgumentException("Invalid invited role.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new TenantInvitation { Id = Guid.NewGuid(), TenantId = tenantId, TargetUserId = targetUserId,
            CreatedByUserId = creator, Role = role, TokenHash = Hash(token), CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) }, token);
    }
    public bool Matches(Guid userId, string? token) => userId == TargetUserId && token is { Length: 43 } &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(TokenHash), Convert.FromHexString(Hash(token)));
    public bool IsPending(DateTimeOffset now) => Status == TenantInvitationStatus.Pending && now < ExpiresAtUtc;
    public void Accept(DateTimeOffset now)
    {
        if (!IsPending(now)) throw new InvalidOperationException("Invitation is no longer usable.");
        Status = TenantInvitationStatus.Accepted; CompletedAtUtc = now;
    }
    public void Cancel(DateTimeOffset now)
    {
        if (Status != TenantInvitationStatus.Pending) throw new InvalidOperationException("Invitation is no longer pending.");
        Status = TenantInvitationStatus.Cancelled; CompletedAtUtc = now;
    }
    public void Expire(DateTimeOffset now)
    {
        if (Status != TenantInvitationStatus.Pending || now < ExpiresAtUtc) throw new InvalidOperationException("Invitation has not expired.");
        Status = TenantInvitationStatus.Expired; CompletedAtUtc = now;
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
