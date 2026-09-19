using ETranslate.Documents.Api.Documents;

namespace ETranslate.DomainTests.Documents;

public sealed class DocumentTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private const string EmptyEditor = "{\"type\":\"doc\",\"content\":[]}";
    private const string A4Page = "{\"pageSize\":\"A4\",\"orientation\":\"Portrait\"}";

    [Fact]
    public void Create_StartsWithImmutableRevisionOne()
    {
        var template = CreateTemplate();

        var revision = Assert.Single(template.Revisions);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(1, template.CurrentRevision);
        Assert.Equal("Visa Translation", template.Name);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void AddRevision_IncrementsCurrentRevision()
    {
        var template = CreateTemplate();

        var revision = template.AddRevision(
            1,
            "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}",
            "{\"type\":\"header\"}",
            null,
            A4Page,
            "{\"text\":\"DRAFT\",\"opacity\":0.15}",
            Guid.NewGuid(),
            Now.AddMinutes(1));

        Assert.Equal(2, revision.RevisionNumber);
        Assert.Equal(2, template.CurrentRevision);
        Assert.Equal(2, template.Revisions.Count);
    }

    [Fact]
    public void AddRevision_RejectsStaleExpectedRevision()
    {
        var template = CreateTemplate();

        var exception = Assert.Throws<DocumentTemplateRevisionConflictException>(() =>
            template.AddRevision(
                0,
                EmptyEditor,
                null,
                null,
                A4Page,
                null,
                Guid.NewGuid(),
                Now.AddMinutes(1)));

        Assert.Equal(0, exception.ExpectedRevision);
        Assert.Equal(1, exception.ActualRevision);
        Assert.Single(template.Revisions);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("\"A4\"")]
    public void Create_RequiresObjectPageLayout(string pageLayout)
    {
        var exception = Assert.Throws<DocumentTemplateValidationException>(() =>
            DocumentTemplate.Create(
                Guid.NewGuid(),
                "Visa Translation",
                null,
                EmptyEditor,
                null,
                null,
                pageLayout,
                null,
                Guid.NewGuid(),
                Now));

        Assert.Contains(nameof(DocumentTemplateRevision.PageLayoutJson), exception.Errors.Keys);
    }

    [Fact]
    public void Create_NormalizesOptionalText()
    {
        var template = DocumentTemplate.Create(
            Guid.NewGuid(),
            "  Visa Translation  ",
            "  Standard A4 template  ",
            EmptyEditor,
            "   ",
            null,
            A4Page,
            null,
            Guid.NewGuid(),
            Now);

        Assert.Equal("Visa Translation", template.Name);
        Assert.Equal("Standard A4 template", template.Description);
        Assert.Null(template.Revisions.Single().HeaderContentJson);
    }

    [Fact]
    public void ArchivedTemplate_RejectsNewRevision()
    {
        var template = CreateTemplate();
        template.SetActive(false, Now.AddMinutes(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            template.AddRevision(
                1,
                EmptyEditor,
                null,
                null,
                A4Page,
                null,
                Guid.NewGuid(),
                Now.AddMinutes(2)));

        Assert.Contains("Archived", exception.Message);
        Assert.False(template.IsActive);
        Assert.Single(template.Revisions);
    }

    private static DocumentTemplate CreateTemplate() =>
        DocumentTemplate.Create(
            Guid.NewGuid(),
            "Visa Translation",
            "Standard A4 template",
            EmptyEditor,
            null,
            null,
            A4Page,
            null,
            Guid.NewGuid(),
            Now);
}
