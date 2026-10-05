using System.ComponentModel.DataAnnotations;

namespace ETranslate.Web.Models;

public sealed class LoginInput
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(256, MinimumLength = 6)] public string Password { get; set; } = "";
}
public sealed record TokenResponse(string AccessToken, int ExpiresIn);
public sealed record TenantView(Guid Id, string Name, string Type, string Role);
public sealed record JobView(Guid Id, string Title, string SourceLanguageCode, string TargetLanguageCode,
    string NotaryRequirement, string? NotaryProcessingMode, string? AcceptanceProfile, string? AcceptanceProfileOther);
public sealed record TemplateView(Guid Id, string Name, bool IsActive, int CurrentRevision);
public sealed record TemplateDetail(Guid Id, string Name, string? Description, bool IsActive, int CurrentRevision, TemplateRevision Revision);
public sealed record TemplateRevision(Guid Id, Guid TemplateId, int RevisionNumber, string EditorContentJson,
    string? HeaderContentJson, string? FooterContentJson, string PageLayoutJson, string? WatermarkJson);
public sealed record DocumentView(Guid Id, int CurrentDraftRevision, Guid? TemplateRevisionId,
    IReadOnlyList<SourceFileView>? SourceFiles = null);
public sealed record SourceFileView(Guid Id, string OriginalFileName, string ContentType, long SizeBytes, string Sha256);
public sealed record DraftView(int RevisionNumber, string EditorContentJson, string? PlainText);
public sealed record DraftSummary(int RevisionNumber, DateTimeOffset CreatedAtUtc);
public sealed record AssetView(Guid Id);
public sealed class WorkspaceView
{
    public List<TenantView> Tenants { get; set; } = [];
    public TenantView? Tenant { get; set; }
    public List<JobView> Jobs { get; set; } = [];
    public List<TemplateView> Templates { get; set; } = [];
}
public sealed class EditorView
{
    public Guid TenantId { get; set; }
    public JobView Job { get; set; } = null!;
    public DocumentView? Document { get; set; }
    public TemplateRevision? Template { get; set; }
    public List<TemplateView> Templates { get; set; } = [];
    public List<DraftSummary> History { get; set; } = [];
    public string EditorContentJson { get; set; } = DocumentText.Empty;
    public int ExpectedRevision { get; set; }
    public string? Message { get; set; }
    public bool Conflict { get; set; }
    public bool Historical { get; set; }
}
