using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class TableWidthTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "table-width-cases.json")));
        foreach (var sample in file.RootElement.EnumerateArray()) yield return [sample.GetProperty("name").GetString()!, sample.GetProperty("columns").GetInt32(), sample.GetProperty("widths").GetRawText(), sample.GetProperty("valid").GetBoolean()];
    }
    private static string Body(int columns, JsonElement widths) => JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "table", attrs = new { columnWidths = widths }, content = new[] { new { type = "tableRow", content = Enumerable.Range(0, columns).Select(_ => new { type = "tableCell", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "İstanbul ترجمه" } } } } }).ToArray() } } } } });
    [Theory, MemberData(nameof(Cases))]
    public void V6_percentages_are_exact_and_old_vocabularies_do_not_accept_the_new_attribute(string name, int columns, string json, bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name)); using var value = JsonDocument.Parse(json); var body = Body(columns, value.RootElement);
        Assert.Equal(valid, DocumentTableWidths.IsValid(value.RootElement, columns));
        Assert.Equal(valid, DocumentContentDescriptor.RichV6.TryRead(body, out _));
        foreach (var version in new[] { DocumentContentDescriptor.Legacy, DocumentContentDescriptor.RichV1, DocumentContentDescriptor.RichV2, DocumentContentDescriptor.RichV3, DocumentContentDescriptor.RichV4, DocumentContentDescriptor.RichV5 }) Assert.False(version.TryRead(body, out _));
    }
    [Fact] public void Width_revision_is_immutable_and_cannot_be_downgraded()
    {
        var actor = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), actor, now);
        const string old = "{\"type\":\"doc\",\"content\":[]}";
        var before = document.AddDraftRevision(0, old, null, actor, now, DocumentContentDescriptor.RichFormat, 5);
        using var widths = JsonDocument.Parse("[25,50,25]"); var body = Body(3, widths.RootElement);
        var current = document.AddDraftRevision(1, body, "forged", actor, now, DocumentContentDescriptor.RichFormat, 6);
        Assert.Equal(body, current.EditorContentJson); Assert.Equal("İstanbul ترجمه\nİstanbul ترجمه\nİstanbul ترجمه", current.PlainText);
        Assert.Equal(old, before.EditorContentJson); Assert.Equal(DocumentContentDescriptor.RichV5, before.ContentDescriptor);
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(2, body, null, actor, now, DocumentContentDescriptor.RichFormat, 5));
        Assert.Equal(DocumentContentDescriptor.RichV6, document.AddDraftRevision(2, body, null, actor, now).ContentDescriptor);
    }
    [Fact] public void Print_uses_colgroup_percentages_and_preserves_old_prepared_html_when_absent()
    {
        using var widths = JsonDocument.Parse("[25,75]"); var body = Body(2, widths.RootElement);
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, body, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 6 };
        Assert.Contains("<colgroup><col style=\"width:25%\"><col style=\"width:75%\"></colgroup>", DraftPdfHtml.Prepare(input, "").Html);
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(input with { ContentSchemaVersion = 5 }, ""));
        using var nul = JsonDocument.Parse("null"); var old = Body(2, nul.RootElement).Replace("\"attrs\":{\"columnWidths\":null},", "");
        Assert.Equal(DraftPdfHtml.Prepare(input with { BodyJson = old, ContentSchemaVersion = 5 }, "").Html, DraftPdfHtml.Prepare(input with { BodyJson = old }, "").Html);
    }
    [Fact] public void Widths_are_table_only_and_vendor_cell_pixels_remain_unsupported()
    {
        foreach (var body in new[] {
            "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"columnWidths\":null}}]}",
            "{\"type\":\"doc\",\"content\":[{\"type\":\"table\",\"columnWidths\":[100],\"content\":[{\"type\":\"tableRow\",\"content\":[{\"type\":\"tableCell\",\"content\":[{\"type\":\"paragraph\"}]}]}]}]}",
            "{\"type\":\"doc\",\"content\":[{\"type\":\"table\",\"content\":[{\"type\":\"tableRow\",\"content\":[{\"type\":\"tableCell\",\"attrs\":{\"colwidth\":[100]},\"content\":[{\"type\":\"paragraph\"}]}]}]}]}"
        }) Assert.False(DocumentContentDescriptor.RichV6.TryRead(body, out _));
    }
}
