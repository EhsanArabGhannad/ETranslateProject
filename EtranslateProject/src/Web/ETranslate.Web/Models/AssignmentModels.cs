namespace ETranslate.Web.Models;

public sealed record AssigneeView(Guid UserId, string Email, string Role);
public sealed record AssignmentChangeView(Guid Id, long Version, Guid ActorUserId, Guid? PreviousTranslatorUserId,
    Guid? TranslatorUserId, string? Note, DateTimeOffset OccurredAtUtc);
public sealed record AssignmentStateView(Guid TranslationJobId, Guid? AssignedTranslatorUserId, long AssignmentVersion,
    Guid? ChangedByUserId, DateTimeOffset? ChangedAtUtc, Guid UserId, bool CanManage, IReadOnlyList<AssignmentChangeView> History);
public sealed class AssignmentPageView
{
    public Guid TenantId { get; set; }
    public JobView Job { get; set; } = null!;
    public AssignmentStateView Assignment { get; set; } = null!;
    public List<AssigneeView> Candidates { get; set; } = [];
    public string? MemberLabel(Guid? userId) => userId.HasValue
        ? Candidates.Find(item => item.UserId == userId)?.Email ?? userId.Value.ToString()
        : "بدون تخصیص";
}
