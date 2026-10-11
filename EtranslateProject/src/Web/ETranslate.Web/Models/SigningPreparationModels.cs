namespace ETranslate.Web.Models;
using ETranslate.SubscriptionAccess;

public sealed record SigningArtifactView(Guid ReviewId, int ReviewRound, Guid PdfVersionId, int RevisionNumber, string Sha256, string Kind);
public sealed record SigningProviderView(string Provider, bool RealSigningEnabled, bool EImza, bool MobilImza);
public sealed record PlannedStageView(int Order, string Role, Guid SignerUserId, string ProposedStatement, string RequestedMethod);
public sealed record SigningPreparationView(Guid Id, string Status, long Version, Guid ReviewId, int ReviewRound, Guid PdfVersionId,
    int RevisionNumber, string PdfSha256, string ArtifactKind, string SignaturePolicy, Guid CreatedByUserId, DateTimeOffset CreatedAtUtc,
    Guid? CancelledByUserId, DateTimeOffset? CancelledAtUtc, string? CancellationReason, bool IsCurrentArtifact, IReadOnlyList<PlannedStageView> Stages);
public sealed record SigningStateView(Guid UserId, bool CanManage, string SignaturePolicy, bool ArtifactServiceAvailable,
    string? ReviewStatus, SigningArtifactView? Artifact, SigningProviderView Provider, IReadOnlyList<SigningPreparationView> Preparations);
public sealed class SigningProposalForm
{
    public Guid ExpectedReviewId { get; set; }
    public Guid ExpectedPdfVersionId { get; set; }
    public Guid TranslatorUserId { get; set; }
    public string TranslatorStatement { get; set; } = "";
    public string TranslatorMethod { get; set; } = "EImza";
    public Guid? OfficeSignerUserId { get; set; }
    public string? OfficeStatement { get; set; }
    public string? OfficeMethod { get; set; }
}
public sealed class SigningPageView
{
    public SubscriptionWriteStatus WriteStatus { get; set; } = SubscriptionWriteStatus.Unavailable;
    public bool CanWrite => WriteStatus == SubscriptionWriteStatus.Allowed;
    public Guid TenantId { get; set; }
    public JobView Job { get; set; } = null!;
    public SigningStateView State { get; set; } = null!;
    public List<AssigneeView> Candidates { get; set; } = [];
    public List<SigningPreviewView> Previews { get; set; } = [];
    public SigningProposalForm Form { get; set; } = new();
    public string? Error { get; set; }
    public bool CanPrepare => CanWrite && State.CanManage && State.ArtifactServiceAvailable && State.Artifact is not null && !State.Preparations.Any(item => item.Status == "Prepared");
    public string MemberLabel(Guid id) => Candidates.Find(item => item.UserId == id)?.Email ?? id.ToString();
}
public sealed record SigningPreviewView(Guid Id, Guid PreparationId, Guid SourcePdfVersionId, string SourcePdfSha256,
    string Sha256, long SizeBytes, string Kind, string RendererVersion, DateTimeOffset CreatedAtUtc);
