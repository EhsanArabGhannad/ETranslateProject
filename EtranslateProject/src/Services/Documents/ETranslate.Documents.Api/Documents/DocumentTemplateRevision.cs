namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentTemplateRevision
{
    private DocumentTemplateRevision()
    {
    }

    internal DocumentTemplateRevision(
        Guid templateId,
        int revisionNumber,
        string editorContentJson,
        string? headerContentJson,
        string? footerContentJson,
        string pageLayoutJson,
        string? watermarkJson,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        TemplateId = templateId;
        RevisionNumber = revisionNumber;
        EditorContentJson = editorContentJson;
        HeaderContentJson = headerContentJson;
        FooterContentJson = footerContentJson;
        PageLayoutJson = pageLayoutJson;
        WatermarkJson = watermarkJson;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private init; }
    public Guid TemplateId { get; private init; }
    public int RevisionNumber { get; private init; }
    public string EditorContentJson { get; private init; } = string.Empty;
    public string? HeaderContentJson { get; private init; }
    public string? FooterContentJson { get; private init; }
    public string PageLayoutJson { get; private init; } = string.Empty;
    public string? WatermarkJson { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
}
