using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class CellStyleTests
{
    public static IEnumerable<object[]> Cases() { using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cell-style-cases.json"))); foreach (var sample in file.RootElement.EnumerateArray()) yield return [sample.GetProperty("name").GetString()!, sample.GetProperty("attrs").GetRawText(), sample.GetProperty("valid").GetBoolean()]; }
    private static string Body(JsonElement attrs) => JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "table", attrs = new { columnWidths = new[] { 100 } }, content = new[] { new { type = "tableRow", content = new[] { new { type = "tableCell", attrs, content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "İstanbul ترجمه" } } } } } } } } } } });
    [Theory, MemberData(nameof(Cases))]
    public void V7_styles_are_bounded_and_keep_old_cell_vocabularies_strict(string name, string json, bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name)); using var attrs = JsonDocument.Parse(json); var body = Body(attrs.RootElement);
        Assert.Equal(valid, attrs.RootElement.EnumerateObject().All(field => DocumentCellStyle.ValidValue(field.Name, field.Value)));
        Assert.Equal(valid, DocumentContentDescriptor.RichV7.TryRead(body, out _));
        foreach (var version in new[] { DocumentContentDescriptor.Legacy, DocumentContentDescriptor.RichV1, DocumentContentDescriptor.RichV2, DocumentContentDescriptor.RichV3, DocumentContentDescriptor.RichV4, DocumentContentDescriptor.RichV5, DocumentContentDescriptor.RichV6 }) Assert.False(version.TryRead(body, out _));
    }
    [Fact] public void Styled_revision_is_immutable_and_cannot_be_downgraded()
    {
        var actor = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), actor, now);
        const string old = "{\"type\":\"doc\",\"content\":[]}"; var previous = document.AddDraftRevision(0, old, null, actor, now, DocumentContentDescriptor.RichFormat, 6);
        using var attrs = JsonDocument.Parse("{\"backgroundColor\":\"#bfdbfe\",\"borderWidthPt\":1.5}"); var body = Body(attrs.RootElement);
        var styled = document.AddDraftRevision(1, body, "forged", actor, now, DocumentContentDescriptor.RichFormat, 7);
        Assert.Equal("İstanbul ترجمه", styled.PlainText); Assert.Equal(old, previous.EditorContentJson); Assert.Equal(DocumentContentDescriptor.RichV6, previous.ContentDescriptor);
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(2, body, null, actor, now, DocumentContentDescriptor.RichFormat, 6));
        Assert.Equal(DocumentContentDescriptor.RichV7, document.AddDraftRevision(2, body, null, actor, now).ContentDescriptor);
    }
    [Fact] public void Print_uses_safe_cell_css_and_zero_width_hides_collapsed_edges()
    {
        using var attrs = JsonDocument.Parse("{\"backgroundColor\":\"#fef08a\",\"borderColor\":\"#b91c1c\",\"borderWidthPt\":0,\"borderStyle\":\"dotted\"}");
        var css = DocumentCellStyle.Css(attrs.RootElement); Assert.Contains("background-color:#fef08a;", css); Assert.EndsWith("border-style:hidden;", css);
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, Body(attrs.RootElement), null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 7 };
        Assert.Contains(css, DraftPdfHtml.Prepare(input, "").Html); Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(input with { ContentSchemaVersion = 6 }, ""));
        const string old = "{\"type\":\"doc\",\"content\":[{\"type\":\"table\",\"content\":[{\"type\":\"tableRow\",\"content\":[{\"type\":\"tableCell\",\"content\":[{\"type\":\"paragraph\"}]}]}]}]}";
        Assert.Equal(DraftPdfHtml.Prepare(input with { BodyJson = old, ContentSchemaVersion = 6 }, "").Html, DraftPdfHtml.Prepare(input with { BodyJson = old }, "").Html);
    }
}
