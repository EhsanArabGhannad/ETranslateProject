using ETranslate.Documents.Api.Documents;

namespace ETranslate.DomainTests.Documents;

public sealed class TemplateApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AppliedRevision_RemainsPinnedAfterTemplateChangesAndArchival()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var template = CreateTemplate(tenant, user);
        var original = template.Revisions.Single();
        var document = TranslationDocument.Create(tenant, Guid.NewGuid(), user, Now);

        var draft = document.ApplyTemplate(template, original, user, Now);
        template.AddRevision(1, "{\"type\":\"doc\",\"changed\":true}", null, null, "{}", null, user, Now);
        template.SetActive(false, Now);
        document.AddDraftRevision(1, "{\"type\":\"doc\",\"edited\":true}", null, user, Now);

        Assert.Equal(original.Id, document.TemplateRevisionId);
        Assert.Equal(original.EditorContentJson, draft.EditorContentJson);
        Assert.Equal(2, document.CurrentDraftRevision);
        Assert.Equal(2, template.CurrentRevision);
    }

    [Fact]
    public void ApplyTemplate_RejectsOtherTenant()
    {
        var user = Guid.NewGuid();
        var template = CreateTemplate(Guid.NewGuid(), user);
        var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), user, Now);
        Assert.Throws<InvalidOperationException>(() => document.ApplyTemplate(template, template.Revisions.Single(), user, Now));
        Assert.Null(document.TemplateRevisionId);
        Assert.Equal(0, document.CurrentDraftRevision);
    }

    [Fact]
    public void ApplyTemplate_RejectsOverwriteOfExistingDraft()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var template = CreateTemplate(tenant, user);
        var document = TranslationDocument.Create(tenant, Guid.NewGuid(), user, Now);
        document.AddDraftRevision(0, "{\"text\":\"keep my work\"}", null, user, Now);
        Assert.Throws<InvalidOperationException>(() => document.ApplyTemplate(template, template.Revisions.Single(), user, Now));
        Assert.Null(document.TemplateRevisionId);
        Assert.Single(document.DraftRevisions);
    }

    [Fact]
    public void ApplyTemplate_RejectsArchivedTemplateAndRepeatApplication()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var template = CreateTemplate(tenant, user);
        var document = TranslationDocument.Create(tenant, Guid.NewGuid(), user, Now);
        var revision = template.Revisions.Single();
        template.SetActive(false, Now);
        Assert.Throws<InvalidOperationException>(() => document.ApplyTemplate(template, revision, user, Now));
        template.SetActive(true, Now);
        document.ApplyTemplate(template, revision, user, Now);
        Assert.Throws<InvalidOperationException>(() => document.ApplyTemplate(template, revision, user, Now));
    }

    [Fact]
    public void AssetReferences_CollectNestedIdsAndRemoveDuplicates()
    {
        var id = Guid.NewGuid();
        var json = $"{{\"content\":[{{\"attrs\":{{\"assetId\":\"{id}\"}}}}]}}";
        Assert.Equal(id, Assert.Single(TemplateAssetReferences.Parse(json, json, null)));
    }

    [Theory]
    [InlineData("{\"assetId\":null}")]
    [InlineData("{\"assetId\":123}")]
    [InlineData("{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"assetId\":\"not-an-id\"}")]
    public void AssetReferences_RejectInvalidIdentifiers(string json) =>
        Assert.Throws<DocumentTemplateValidationException>(() => TemplateAssetReferences.Parse(json));

    [Theory]
    [InlineData("image/svg+xml", 100)]
    [InlineData("application/pdf", 100)]
    [InlineData("image/png", 5242881)]
    [InlineData("image/png", 0)]
    public void Asset_RejectsUnsupportedTypeOrSize(string type, long size) =>
        Assert.Throws<DocumentTemplateValidationException>(() => TemplateAsset.Create(
            Guid.NewGuid(), Guid.NewGuid(), "logo", type, size, new string('a', 64), "template/logo", Guid.NewGuid(), Now));

    private static DocumentTemplate CreateTemplate(Guid tenant, Guid user) =>
        DocumentTemplate.Create(tenant, "Office", null, "{\"type\":\"doc\"}", null, null, "{}", null, user, Now);
}
