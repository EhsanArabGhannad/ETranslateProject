namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentDraftRevision
{
    private DocumentDraftRevision()
    {
    }

    internal DocumentDraftRevision(
        Guid documentId,
        int revisionNumber,
        string editorContentJson,
        string? plainText,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        DocumentId = documentId;
        RevisionNumber = revisionNumber;
        EditorContentJson = editorContentJson;
        PlainText = plainText;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public int RevisionNumber { get; private init; }
    public string EditorContentJson { get; private init; } = string.Empty;
    public string? PlainText { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
}
