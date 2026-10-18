using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Documents;

public sealed class LateTemplateTests
{
    private static readonly Guid Actor = Guid.NewGuid(), Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static DocumentTemplate Template(string name = "Target", Guid? tenant = null) => DocumentTemplate.Create(tenant ?? Tenant, name, null,
        "{\"type\":\"doc\",\"starter\":\"Never replace saved translation\"}", null, null, "{}", null, Actor, Now);
    [Fact] public void Late_attachment_and_replacement_keep_exact_body_and_pin_every_revision()
    {
        var document = TranslationDocument.Create(Tenant, Guid.NewGuid(), Actor, Now);
        const string json = "{ \"type\":\"doc\", \"custom\":true, \"content\":[] }";
        var first = document.AddDraftRevision(0, json, "Keep plain text", Actor, Now);
        var a = Template("A"); var b = Template("B");
        var second = document.ChangeTemplate(1, a, a.Revisions.Single(), first, Actor, Now);
        var third = document.ChangeTemplate(2, b, b.Revisions.Single(), second, Actor, Now);
        Assert.Null(first.TemplateRevisionId); Assert.Equal(a.Revisions.Single().Id, second.TemplateRevisionId);
        Assert.Equal(b.Revisions.Single().Id, third.TemplateRevisionId); Assert.Equal(third.TemplateRevisionId, document.TemplateRevisionId);
        Assert.All(document.DraftRevisions, r => { Assert.Equal(json, r.EditorContentJson); Assert.Equal("Keep plain text", r.PlainText); });
        var fourth = document.AddDraftRevision(3, "{\"newBody\":true}", null, Actor, Now);
        Assert.Equal(third.TemplateRevisionId, fourth.TemplateRevisionId);
    }
    [Fact] public void Initial_application_still_uses_starter_and_snapshots_its_pin()
    {
        var document = TranslationDocument.Create(Tenant, Guid.NewGuid(), Actor, Now); var template = Template();
        var first = document.ApplyTemplate(template, template.Revisions.Single(), Actor, Now);
        Assert.Equal(template.Revisions.Single().Id, first.TemplateRevisionId); Assert.Equal(template.Revisions.Single().EditorContentJson, first.EditorContentJson);
    }
    [Fact] public void Stale_same_archived_foreign_or_unsaved_changes_are_rejected()
    {
        var document = TranslationDocument.Create(Tenant, Guid.NewGuid(), Actor, Now); var a = Template();
        var first = document.ApplyTemplate(a, a.Revisions.Single(), Actor, Now); var target = Template("New");
        Assert.Throws<DocumentRevisionConflictException>(() => document.ChangeTemplate(0, target, target.Revisions.Single(), first, Actor, Now));
        Assert.Throws<InvalidOperationException>(() => document.ChangeTemplate(1, a, a.Revisions.Single(), first, Actor, Now));
        target.SetActive(false, Now);
        Assert.Throws<InvalidOperationException>(() => document.ChangeTemplate(1, target, target.Revisions.Single(), first, Actor, Now));
        var foreign = Template(tenant: Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => document.ChangeTemplate(1, foreign, foreign.Revisions.Single(), first, Actor, Now));
        Assert.Single(document.DraftRevisions); Assert.Equal(a.Revisions.Single().Id, document.TemplateRevisionId);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Awaiting_and_approved_reviews_lock_template_changes_until_reopened(bool approved)
    {
        var document = TranslationDocument.Create(Tenant, Guid.NewGuid(), Actor, Now); var target = Template();
        var first = document.AddDraftRevision(0, "{}", null, Actor, Now);
        var pdf = new DocumentPdfVersion(document.Id, first.Id, 1, null, "test", "test.pdf", 100, new string('a',64), Actor, Now);
        var review = document.SubmitReview(1, 0, pdf, null, "[]", Actor, Now);
        if (approved) document.DecideReview(review, DocumentReviewStatus.Approved, Actor, null, Now);
        Assert.Throws<DocumentReviewConflictException>(() => document.ChangeTemplate(1, target, target.Revisions.Single(), first, Actor, Now));
        if (approved) {
            document.ReopenReview(review, Actor, "Correct layout", Now);
            document.ChangeTemplate(1, target, target.Revisions.Single(), first, Actor, Now);
            Assert.Equal(2, document.CurrentDraftRevision); Assert.Equal(pdf.TemplateRevisionId, first.TemplateRevisionId);
        }
    }
    [Fact] public void Draft_template_snapshot_has_restrictive_foreign_key()
    {
        using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer("Server=fixture;Database=fixture").Options);
        var model = db.Model.FindEntityType(typeof(DocumentDraftRevision))!;
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(model.GetForeignKeys(), k => k.Properties.Single().Name == "TemplateRevisionId").DeleteBehavior);
    }
}
