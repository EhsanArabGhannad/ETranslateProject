using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Documents;

public sealed class DocumentReviewTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-06T12:00:00Z");
    private static TranslationDocument Document()
    {
        var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), Actor, Now);
        document.AddDraftRevision(0, "{\"type\":\"doc\",\"content\":[]}", null, Actor, Now);
        return document;
    }
    private static DocumentPdfVersion Pdf(TranslationDocument document) => new(document.Id, document.DraftRevisions.Last().Id,
        document.CurrentDraftRevision, document.TemplateRevisionId, "test", "test/sample.pdf", 100, new string('a', 64), Actor, Now);
    private static DocumentReview Submit(TranslationDocument document, DocumentReview? previous = null) =>
        document.SubmitReview(document.CurrentDraftRevision, document.ReviewRound, Pdf(document), previous, "[]", Actor, Now);

    [Fact] public void Submission_PinsPdf_AndLocksBodyAndSources()
    {
        var document = Document(); var pdf = Pdf(document);
        var review = document.SubmitReview(1, 0, pdf, null, "[]", Actor, Now);
        Assert.Equal(pdf.Id, review.PdfVersionId); Assert.Equal(pdf.Sha256, review.PdfSha256);
        Assert.Equal(Actor, review.SubmittedByUserId); Assert.Equal(1, review.Round);
        Assert.True(document.IsReviewLocked);
        Assert.Throws<DocumentReviewConflictException>(() => document.AddDraftRevision(1, "{}", null, Actor, Now));
        Assert.Throws<DocumentReviewConflictException>(() => document.AddSourceFile(Guid.NewGuid(), "file.png", "image/png", 10, new string('a', 64), "file.png", Actor, Now));
        Assert.Throws<DocumentReviewConflictException>(() => Submit(document));
    }
    [Fact] public void SourceUploads_AlwaysTouchParent_EvenAtIdenticalClockTicks()
    {
        var document = Document(); var before = document.UpdatedAtUtc;
        document.AddSourceFile(Guid.NewGuid(), "file.png", "image/png", 10, new string('a', 64), "file.png", Actor, Now);
        Assert.True(document.UpdatedAtUtc > before);
        before = document.UpdatedAtUtc;
        document.AddSourceFile(Guid.NewGuid(), "file2.png", "image/png", 10, new string('a', 64), "file2.png", Actor, Now);
        Assert.True(document.UpdatedAtUtc > before);
    }
    [Theory] [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(1, 1)]
    public void Submission_RejectsStaleRevisionOrRound(int revision, int round)
    {
        var document = Document(); Assert.Throws<DocumentReviewConflictException>(() => document.SubmitReview(revision, round, Pdf(document), null, "[]", Actor, Now));
        Assert.Equal(DocumentReviewStatus.Draft, document.ReviewStatus); Assert.Equal(0, document.ReviewRound);
    }
    [Fact] public void Submission_RejectsPdfFromAnotherDocument()
    {
        var document = Document(); Assert.Throws<DocumentReviewConflictException>(() => document.SubmitReview(1, 0, Pdf(Document()), null, "[]", Actor, Now));
    }
    [Fact] public void Submission_RejectsPdfFromOlderRevision()
    {
        var document = Document(); var pdf = Pdf(document); document.AddDraftRevision(1, "{}", null, Actor, Now);
        Assert.Throws<DocumentReviewConflictException>(() => document.SubmitReview(2, 0, pdf, null, "[]", Actor, Now));
    }
    [Fact] public void Approval_IsInternal_Locked_AndCannotBeDecidedTwice()
    {
        var document = Document(); var review = Submit(document); var reviewer = Guid.NewGuid();
        document.DecideReview(review, DocumentReviewStatus.Approved, reviewer, "  checked  ", Now.AddMinutes(1));
        Assert.True(document.IsReviewLocked); Assert.Equal("checked", review.DecisionNote); Assert.Equal(reviewer, review.DecidedByUserId);
        Assert.Throws<DocumentReviewConflictException>(() => document.DecideReview(review, DocumentReviewStatus.ChangesRequested, Actor, "other", Now));
        Assert.Equal(DocumentReviewStatus.Approved, review.Status);
    }
    [Theory] [InlineData(DocumentReviewStatus.ChangesRequested)] [InlineData(DocumentReviewStatus.Withdrawn)]
    public void ReturnOrWithdrawal_RequiresReason_WithoutPartiallyChangingState(DocumentReviewStatus status)
    {
        var document = Document(); var review = Submit(document);
        Assert.Throws<DocumentValidationException>(() => document.DecideReview(review, status, Actor, " ", Now));
        Assert.Equal(DocumentReviewStatus.AwaitingReview, review.Status); Assert.True(document.IsReviewLocked);
        Assert.Null(review.DecidedByUserId);
    }
    [Fact] public void Decision_RejectsOversizedNote()
    {
        var document = Document(); var review = Submit(document);
        Assert.Throws<DocumentValidationException>(() => document.DecideReview(review, DocumentReviewStatus.Approved, Actor, new string('a', 2001), Now));
    }
    [Fact] public void ReturnedCorrections_RequireNewSavedRevision_AndPreserveEarlierReview()
    {
        var document = Document(); var review = Submit(document);
        document.DecideReview(review, DocumentReviewStatus.ChangesRequested, Actor, "Fix date", Now);
        Assert.False(document.IsReviewLocked); Assert.Throws<DocumentReviewConflictException>(() => Submit(document, review));
        document.AddDraftRevision(1, "{}", null, Actor, Now);
        var second = Submit(document, review);
        Assert.Equal(2, second.Round); Assert.Equal(2, second.RevisionNumber);
        Assert.Equal("Fix date", review.DecisionNote); Assert.Equal(DocumentReviewStatus.ChangesRequested, review.Status);
        Assert.Throws<DocumentReviewConflictException>(() => document.DecideReview(review, DocumentReviewStatus.Approved, Actor, null, Now));
    }
    [Fact] public void Withdrawal_CanResubmitSameRevision_WithNewReviewIdentity()
    {
        var document = Document(); var review = Submit(document);
        document.DecideReview(review, DocumentReviewStatus.Withdrawn, Actor, "Wrong submission", Now);
        var second = Submit(document, review); Assert.NotEqual(review.Id, second.Id); Assert.Equal(2, second.Round); Assert.Equal(1, second.RevisionNumber);
    }
    [Fact] public void Reopening_PreservesApprovalAudit_AndRequiresNewRevision()
    {
        var document = Document(); var review = Submit(document); var reviewer = Guid.NewGuid();
        document.DecideReview(review, DocumentReviewStatus.Approved, reviewer, "Approved", Now);
        Assert.Throws<DocumentValidationException>(() => document.ReopenReview(review, Actor, null, Now));
        document.ReopenReview(review, Actor, "Correction needed", Now.AddMinutes(1));
        Assert.False(document.IsReviewLocked); Assert.Equal(reviewer, review.DecidedByUserId); Assert.Equal("Approved", review.DecisionNote);
        Assert.Equal(Actor, review.ReopenedByUserId); Assert.Equal("Correction needed", review.ReopenNote);
        Assert.Throws<DocumentReviewConflictException>(() => Submit(document, review));
        document.AddDraftRevision(1, "{}", null, Actor, Now); Assert.Equal(2, Submit(document, review).Round);
    }
    [Fact] public void Decisions_CannotUseReviewFromOtherDocument()
    {
        var document = Document(); Submit(document); var otherReview = Submit(Document());
        Assert.Throws<DocumentReviewConflictException>(() => document.DecideReview(otherReview, DocumentReviewStatus.Approved, Actor, null, Now));
    }
    [Theory] [InlineData("Owner", true, true)] [InlineData("Administrator", true, true)]
    [InlineData("Translator", true, false)] [InlineData("Viewer", false, false)] [InlineData("Unknown", false, false)]
    public void ReviewerPermissions_AreSeparateFromDraftEditing(string role, bool write, bool review)
    {
        var actor = new TenantActor(Actor, role); Assert.Equal(write, actor.CanManageDocuments); Assert.Equal(review, actor.CanReviewDocuments);
    }
    [Fact] public void Database_UsesRowVersionAcrossAllDocumentWrites_AndUniqueReviewRounds()
    {
        using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer("Server=test;Database=test;Integrated Security=True").Options);
        var document = db.Model.FindEntityType(typeof(TranslationDocument))!;
        Assert.True(document.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Contains(db.Model.FindEntityType(typeof(DocumentReview))!.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual(["DocumentId", "Round"]));
    }
}
