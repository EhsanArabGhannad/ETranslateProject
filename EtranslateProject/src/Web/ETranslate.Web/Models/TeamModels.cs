namespace ETranslate.Web.Models;

public sealed record TeamMemberView(Guid UserId, string Email, string Role, bool IsActive, DateTimeOffset JoinedAtUtc, bool CanEdit);
public sealed record TeamInvitationView(Guid Id, Guid TargetUserId, string Email, string Role, string Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, bool IsExpired, bool CanCancel);
public sealed record CreatedTeamInvitationView(Guid Id, Guid TenantId, Guid TargetUserId, string Email, string Role, DateTimeOffset ExpiresAtUtc, string Token);
public sealed record TeamAuditView(long TeamVersion, Guid ActorUserId, Guid TargetUserId, string Action, string? PreviousRole, string? Role, DateTimeOffset OccurredAtUtc);
public sealed record TeamView(Guid TenantId, string TenantName, Guid UserId, string Role, long TeamVersion,
    IReadOnlyList<TeamMemberView> Members, IReadOnlyList<TeamInvitationView> Invitations, IReadOnlyList<TeamAuditView> Audit);
public sealed class TeamPageView
{
    public TeamView Team { get; set; } = null!;
    public CreatedTeamInvitationView? Created { get; set; }
    public string? InviteLink { get; set; }
    public string? Message { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Translator";
    public static string RoleLabel(string? role) => role switch
    { "Owner" => "مالک", "Administrator" => "مدیر", "Translator" => "مترجم", "Reviewer" => "ناظر · فقط خواندن", _ => "—" };
    public static string ActionLabel(string action) => action switch
    { "Invited" => "صدور دعوت", "InvitationAccepted" => "پذیرش دعوت", "InvitationCancelled" => "لغو دعوت", "RoleChanged" => "تغییر نقش", "MemberActivated" => "فعال‌سازی عضو", "MemberDeactivated" => "غیرفعال‌سازی عضو", _ => action };
}
public sealed record RecipientInvitationView(Guid Id, Guid TenantId, string TenantName, string Role, string Status, DateTimeOffset ExpiresAtUtc, bool CanAccept);
public sealed class AcceptInvitationView
{
    public Guid InvitationId { get; set; }
    public RecipientInvitationView? Invitation { get; set; }
    public string? Message { get; set; }
}
