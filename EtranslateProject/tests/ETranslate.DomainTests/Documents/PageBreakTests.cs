using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class PageBreakTests
{
    public static IEnumerable<object[]> Cases() { using var samples = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "page-break-cases.json"))); foreach (var sample in samples.RootElement.EnumerateArray()) yield return [sample.GetRawText()]; }
    [Theory, MemberData(nameof(Cases))]
    public void V10_only_allows_bounded_body_page_starts_and_preserves_older_vocabulary(string source)
    {
        using var sample = JsonDocument.Parse(source); var body = sample.RootElement.GetProperty("doc").GetRawText();
        Assert.Equal(sample.RootElement.GetProperty("valid").GetBoolean(), DocumentContentDescriptor.RichV10.TryRead(body, out _));
        for (var version = 1; version < 10; version++) Assert.Equal(sample.RootElement.GetProperty("legacyValid").GetBoolean(), new DocumentContentDescriptor(DocumentContentDescriptor.RichFormat, version).TryRead(body, out _));
    }
    [Fact] public void Manual_starts_cannot_enter_header_footer_even_through_legacy_doc_root_fallback()
    {
        const string body = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"pageBreakBefore\":true}}]}";
        Assert.True(DocumentContentDescriptor.RichV10.TryRead(body, out _, "doc"));
        foreach (var root in new[] { "header", "footer" }) { Assert.False(DocumentContentDescriptor.RichV10.TryRead(body, out _, root)); Assert.False(DocumentContentDescriptor.RichV10.TryRead(body.Replace("\"doc\"", $"\"{root}\""), out _, root)); }
    }
    [Fact] public void Saved_start_advances_schema_without_mutating_history_and_renders_a_semantic_print_break()
    {
        var actor = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), actor, now);
        const string old = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"BEFORE\"}]}]}";
        var before = document.AddDraftRevision(0, old, null, actor, now, DocumentContentDescriptor.RichFormat, 9);
        const string body = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"BEFORE\"}]},{\"type\":\"paragraph\",\"attrs\":{\"pageBreakBefore\":true},\"content\":[{\"type\":\"text\",\"text\":\"AFTER\"}]}]}";
        var saved = document.AddDraftRevision(1, body, "forged", actor, now, DocumentContentDescriptor.RichFormat, 10); Assert.Equal("BEFORE\nAFTER", saved.PlainText); Assert.Equal(old, before.EditorContentJson);
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(2, body, null, actor, now, DocumentContentDescriptor.RichFormat, 9));
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, body, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 };
        var html = DraftPdfHtml.Prepare(input, "").Html; Assert.Contains("data-et-page-start=\"true\"", html); Assert.Contains("break-before:page", html);
        Assert.Equal(DraftPdfHtml.Prepare(input with { BodyJson = old, ContentSchemaVersion = 9 }, "").Html, DraftPdfHtml.Prepare(input with { BodyJson = old }, "").Html);
    }
}
