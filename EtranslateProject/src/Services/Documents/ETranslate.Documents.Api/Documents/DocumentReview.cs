namespace ETranslate.Documents.Api.Documents;

public enum DocumentReviewStatus { Draft = 0, AwaitingReview = 1, Approved = 2, ChangesRequested = 3, Withdrawn = 4, Reopened = 5 }

// An internal review, never a signature or notarial approval. Decisions retain their original actors/timestamps.
public sealed class DocumentReview
{
    private DocumentReview() { }
    internal DocumentReview(Guid documentId, int round, DocumentPdfVersion pdf, string sourceFilesJson,
        Guid actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); DocumentId = documentId; Round = round; PdfVersionId = pdf.Id;
        DraftRevisionId = pdf.DraftRevisionId; RevisionNumber = pdf.RevisionNumber; PdfSha256 = pdf.Sha256;
        SourceFilesJson = sourceFilesJson; SubmittedByUserId = actor; SubmittedAtUtc = now;
        Status = DocumentReviewStatus.AwaitingReview;
    }
    public Guid Id { get; private init; }
    public Guid DocumentId { get; private init; }
    public int Round { get; private init; }
    public Guid PdfVersionId { get; private init; }
    public Guid DraftRevisionId { get; private init; }
    public int RevisionNumber { get; private init; }
    public string PdfSha256 { get; private init; } = "";
    public string SourceFilesJson { get; private init; } = "[]";
    public Guid SubmittedByUserId { get; private init; }
    public DateTimeOffset SubmittedAtUtc { get; private init; }
    public DocumentReviewStatus Status { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }
    public Guid? ReopenedByUserId { get; private set; }
    public DateTimeOffset? ReopenedAtUtc { get; private set; }
    public string? ReopenNote { get; private set; }

    internal void Decide(DocumentReviewStatus decision, Guid actor, string? note, DateTimeOffset now)
    {
        if (Status != DocumentReviewStatus.AwaitingReview ||
            decision is not (DocumentReviewStatus.Approved or DocumentReviewStatus.ChangesRequested or DocumentReviewStatus.Withdrawn))
            throw new DocumentReviewConflictException("Review is no longer awaiting a decision.");
        note = ValidateNote(note, required: decision != DocumentReviewStatus.Approved);
        Status = decision; DecidedByUserId = actor; DecidedAtUtc = now; DecisionNote = note;
    }
    internal void Reopen(Guid actor, string? note, DateTimeOffset now)
    {
        if (Status != DocumentReviewStatus.Approved) throw new DocumentReviewConflictException("Only approved reviews can be reopened.");
        note = ValidateNote(note, required: true);
        Status = DocumentReviewStatus.Reopened; ReopenedByUserId = actor; ReopenedAtUtc = now; ReopenNote = note;
    }
    private static string? ValidateNote(string? note, bool required)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 2000 || required && note is null)
            throw new DocumentValidationException(new Dictionary<string, string[]> { ["note"] = ["A reason is required (maximum 2000 characters)."] });
        return note;
    }
}

public sealed class DocumentReviewConflictException(string message) : InvalidOperationException(message);
