using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class HorizontalSpanTests
{
    public static IEnumerable<object[]> Cases() { using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "horizontal-span-cases.json"))); foreach (var sample in file.RootElement.EnumerateArray()) yield return [sample.GetProperty("name").GetString()!, sample.GetRawText(), sample.GetProperty("valid").GetBoolean()]; }
    private static string Body(JsonElement sample)
    {
        var rows = sample.GetProperty("rows").EnumerateArray().Select(row => new { type = "tableRow", content = row.EnumerateArray().Select(span => new { type = "tableCell", attrs = new { colspan = span, rowspan = sample.TryGetProperty("rowspan", out var vertical) ? vertical.Clone() : JsonSerializer.SerializeToElement(1), colwidth = sample.TryGetProperty("colwidth", out var pixels) ? pixels.Clone() : JsonSerializer.SerializeToElement<object?>(null) }, content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "İstanbul ترجمه" } } } } }).ToArray() }).ToArray();
        return JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "table", attrs = new { columnWidths = sample.TryGetProperty("widths", out var widths) ? widths.Clone() : JsonSerializer.SerializeToElement<object?>(null) }, content = rows } } });
    }
    [Theory, MemberData(nameof(Cases))]
    public void V8_counts_logical_columns_and_only_allows_bounded_horizontal_spans(string name, string json, bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name)); using var sample = JsonDocument.Parse(json); var body = Body(sample.RootElement);
        Assert.Equal(valid, DocumentContentDescriptor.RichV8.TryRead(body, out _));
        if (sample.RootElement.GetProperty("rows").EnumerateArray().SelectMany(row => row.EnumerateArray()).Any(span => span.ValueKind != JsonValueKind.Number || span.GetDecimal() != 1))
            foreach (var descriptor in new[] { DocumentContentDescriptor.Legacy, DocumentContentDescriptor.RichV1, DocumentContentDescriptor.RichV2, DocumentContentDescriptor.RichV3, DocumentContentDescriptor.RichV4, DocumentContentDescriptor.RichV5, DocumentContentDescriptor.RichV6, DocumentContentDescriptor.RichV7 }) Assert.False(descriptor.TryRead(body, out _));
    }
    [Fact] public void Merged_revision_is_immutable_and_prints_semantic_spans_without_downgrading()
    {
        var actor = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), actor, now);
        const string old = "{\"type\":\"doc\",\"content\":[]}"; var previous = document.AddDraftRevision(0, old, null, actor, now, DocumentContentDescriptor.RichFormat, 7);
        using var sample = JsonDocument.Parse("{\"rows\":[[2,1],[1,1,1]],\"widths\":[25,50,25]}"); var body = Body(sample.RootElement);
        var revision = document.AddDraftRevision(1, body, "forged", actor, now, DocumentContentDescriptor.RichFormat, 8);
        Assert.Equal(DocumentContentDescriptor.RichV8, revision.ContentDescriptor); Assert.Equal(old, previous.EditorContentJson);
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(2, body, null, actor, now, DocumentContentDescriptor.RichFormat, 7));
        Assert.Equal(DocumentContentDescriptor.RichV8, document.AddDraftRevision(2, body, null, actor, now).ContentDescriptor);
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, body, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 8 };
        Assert.Contains("colspan=\"2\"", DraftPdfHtml.Prepare(input, "").Html);
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(input with { ContentSchemaVersion = 7 }, ""));
        Assert.Equal(DraftPdfHtml.Prepare(input with { BodyJson = old, ContentSchemaVersion = 7 }, "").Html, DraftPdfHtml.Prepare(input with { BodyJson = old }, "").Html);
    }
}
