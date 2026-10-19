using System.ComponentModel.DataAnnotations;
using ETranslate.SubscriptionAccess;

namespace ETranslate.Web.Models;

public sealed class LoginInput
{
    [Required(ErrorMessageResourceType = typeof(UiValidation), ErrorMessageResourceName = nameof(UiValidation.EPostaZorunludur)), EmailAddress(ErrorMessageResourceType = typeof(UiValidation), ErrorMessageResourceName = nameof(UiValidation.GecerliBirEPostaAdresiGirin))]
    public string Email { get; set; } = "";
    [Required(ErrorMessageResourceType = typeof(UiValidation), ErrorMessageResourceName = nameof(UiValidation.SifreZorunludur)), DataType(DataType.Password),
     StringLength(256, MinimumLength = 6, ErrorMessageResourceType = typeof(UiValidation), ErrorMessageResourceName = nameof(UiValidation.Sifre6256KarakterOlmalidir))]
    public string Password { get; set; } = "";
}
public sealed record TokenResponse(string AccessToken, int ExpiresIn);
public sealed record TenantView(Guid Id, string Name, string Type, string Role);
public sealed record JobView(Guid Id, string Title, string SourceLanguageCode, string TargetLanguageCode,
    string NotaryRequirement, string? NotaryProcessingMode, string? AcceptanceProfile, string? AcceptanceProfileOther,
    Guid? AssignedTranslatorUserId = null, long AssignmentVersion = 0);
public sealed record TemplateView(Guid Id, string Name, bool IsActive, int CurrentRevision);
public sealed record TemplateDetail(Guid Id, string Name, string? Description, bool IsActive, int CurrentRevision, TemplateRevision Revision);
public sealed record TemplateRevision(Guid Id, Guid TemplateId, int RevisionNumber, string EditorContentJson,
    string? HeaderContentJson, string? FooterContentJson, string PageLayoutJson, string? WatermarkJson);
public sealed record DocumentView(Guid Id, int CurrentDraftRevision, Guid? TemplateRevisionId,
    IReadOnlyList<SourceFileView>? SourceFiles = null, string ReviewStatus = "Draft", int ReviewRound = 0);
public sealed record SourceFileView(Guid Id, string OriginalFileName, string ContentType, long SizeBytes, string Sha256);
public sealed record DraftView(int RevisionNumber, string EditorContentJson, string? PlainText, Guid? TemplateRevisionId = null);
public sealed record DraftSummary(int RevisionNumber, DateTimeOffset CreatedAtUtc);
public sealed record AssetView(Guid Id);
public sealed record PdfVersionView(Guid Id, int RevisionNumber, Guid? TemplateRevisionId,
    string RendererVersion, long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc, string Kind);
public sealed record ReviewSourceView(Guid Id, string OriginalFileName, long SizeBytes, string Sha256);
public sealed record ReviewView(Guid Id, int Round, string Status, int RevisionNumber, Guid PdfVersionId, string PdfSha256,
    IReadOnlyList<ReviewSourceView> SourceFiles, Guid SubmittedByUserId, DateTimeOffset SubmittedAtUtc,
    Guid? DecidedByUserId, DateTimeOffset? DecidedAtUtc, string? DecisionNote,
    Guid? ReopenedByUserId, DateTimeOffset? ReopenedAtUtc, string? ReopenNote);
public sealed record ReviewStateView(string Status, int Round, Guid UserId, bool CanManage, bool CanReview, IReadOnlyList<ReviewView> Reviews)
{
    public bool Locked => Status is "AwaitingReview" or "Approved";
    public static string Label(string status) => status switch
    {
        "AwaitingReview" => UiText.Get("IncelemeBekliyor"), "Approved" => UiText.Get("IcOnayImzasiz"),
        "ChangesRequested" => UiText.Get("DuzeltmeGerekli"), "Withdrawn" => UiText.Get("GeriCekildi"), "Reopened" => UiText.Get("DuzeltmeIcinYenidenAc"), _ => UiText.Get("Taslak")
    };
}
public sealed class WorkspaceView
{
    public SubscriptionWriteStatus WriteStatus { get; set; } = SubscriptionWriteStatus.Unavailable;
    public bool CanWrite => WriteStatus == SubscriptionWriteStatus.Allowed;
    public List<TenantView> Tenants { get; set; } = [];
    public TenantView? Tenant { get; set; }
    public List<JobView> Jobs { get; set; } = [];
    public bool AssignedToMe { get; set; }
}
public sealed class EditorView
{
    public TenantView? Tenant { get; set; }
    public SubscriptionWriteStatus WriteStatus { get; set; } = SubscriptionWriteStatus.Unavailable;
    public bool CanWrite => WriteStatus == SubscriptionWriteStatus.Allowed;
    public Guid TenantId { get; set; }
    public JobView Job { get; set; } = null!;
    public DocumentView? Document { get; set; }
    public TemplateRevision? Template { get; set; }
    public List<TemplateView> Templates { get; set; } = [];
    public List<DraftSummary> History { get; set; } = [];
    public string EditorContentJson { get; set; } = DocumentText.Empty;
    public int ExpectedRevision { get; set; }
    public int ViewedRevision { get; set; }
    public List<PdfVersionView> PdfVersions { get; set; } = [];
    public ReviewStateView? Review { get; set; }
    public string? Message { get; set; }
    public bool Conflict { get; set; }
    public bool Historical { get; set; }
}
