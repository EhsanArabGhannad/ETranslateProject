using ETranslate.Documents.Api.Documents;

namespace ETranslate.DomainTests.Documents;

public sealed class TranslationDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstDraftRevision_StartsAtOne()
    {
        var document = CreateDocument();

        var revision = document.AddDraftRevision(
            expectedCurrentRevision: 0,
            editorContentJson: "{\"type\":\"doc\",\"content\":[]}",
            plainText: null,
            createdByUserId: Guid.NewGuid(),
            createdAtUtc: Now.AddMinutes(1));

        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(1, document.CurrentDraftRevision);
        Assert.Single(document.DraftRevisions);
    }

    [Fact]
    public void DraftRevision_RequiresExpectedCurrentRevision()
    {
        var document = CreateDocument();
        document.AddDraftRevision(
            0,
            "{\"type\":\"doc\"}",
            null,
            Guid.NewGuid(),
            Now.AddMinutes(1));

        var exception = Assert.Throws<DocumentRevisionConflictException>(() =>
            document.AddDraftRevision(
                0,
                "{\"type\":\"doc\",\"version\":2}",
                null,
                Guid.NewGuid(),
                Now.AddMinutes(2)));

        Assert.Equal(0, exception.ExpectedRevision);
        Assert.Equal(1, exception.ActualRevision);
        Assert.Single(document.DraftRevisions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("\"scalar-json\"")]
    public void DraftRevision_RejectsInvalidEditorContent(string content)
    {
        var document = CreateDocument();

        var exception = Assert.Throws<DocumentValidationException>(() =>
            document.AddDraftRevision(0, content, null, Guid.NewGuid(), Now));

        Assert.Contains(nameof(DocumentDraftRevision.EditorContentJson), exception.Errors.Keys);
    }

    [Fact]
    public void SourceFile_StripsClientPathAndNormalizesHash()
    {
        var document = CreateDocument();
        var hash = new string('A', 64);

        var sourceFile = document.AddSourceFile(
            Guid.NewGuid(),
            "C:\\fakepath\\passport.pdf",
            "application/pdf",
            1024,
            hash,
            "tenant/document/sources/file.pdf",
            Guid.NewGuid(),
            Now.AddMinutes(1));

        Assert.Equal("passport.pdf", sourceFile.OriginalFileName);
        Assert.Equal(hash.ToLowerInvariant(), sourceFile.Sha256);
        Assert.Single(document.SourceFiles);
    }

    [Fact]
    public void SourceFile_RejectsUnsupportedContentType()
    {
        var document = CreateDocument();

        var exception = Assert.Throws<DocumentValidationException>(() =>
            document.AddSourceFile(
                Guid.NewGuid(),
                "source.exe",
                "application/octet-stream",
                1024,
                new string('a', 64),
                "tenant/document/sources/file.exe",
                Guid.NewGuid(),
                Now));

        Assert.Contains(nameof(SourceFile.ContentType), exception.Errors.Keys);
    }

    private static TranslationDocument CreateDocument() =>
        TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
}
