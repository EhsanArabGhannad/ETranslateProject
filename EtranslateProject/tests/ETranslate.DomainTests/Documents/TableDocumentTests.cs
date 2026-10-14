using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class TableDocumentTests
{
    private static JsonObject Document(int rows = 2, int columns = 3)
    {
        var content = new JsonArray();
        for (var row = 0; row < rows; row++)
        {
            var cells = new JsonArray();
            for (var col = 0; col < columns; col++) cells.Add(new JsonObject { ["type"] = row == 0 ? "tableHeader" : "tableCell",
                ["attrs"] = new JsonObject { ["colspan"] = 1, ["rowspan"] = 1, ["colwidth"] = null, ["align"] = null },
                ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"R{row}C{col} فارسی Türkçe <script>" }) }) });
            content.Add(new JsonObject { ["type"] = "tableRow", ["content"] = cells });
        }
        return new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(new JsonObject { ["type"] = "table", ["attrs"] = new JsonObject { ["dir"] = "rtl" }, ["content"] = content }) };
    }
    [Theory]
    [InlineData(50, 12, true)] [InlineData(51, 12, false)] [InlineData(50, 13, false)] [InlineData(0, 3, false)] [InlineData(2, 0, false)]
    public void Table_bounds_are_explicit(int rows, int columns, bool valid)
        => Assert.Equal(valid, DocumentContentJson.TryGetPlainText(Document(rows, columns).ToJsonString(), out _));

    [Fact] public void Table_plaintext_is_row_major_and_print_has_semantic_encoded_cells()
    {
        var json = Document().ToJsonString();
        Assert.True(DocumentContentJson.TryGetPlainText(json, out var text));
        Assert.True(text!.IndexOf("R0C2", StringComparison.Ordinal) < text.IndexOf("R1C0", StringComparison.Ordinal));
        var html = DraftPdfHtml.Prepare(Input(json), "").Html;
        Assert.Contains("<table dir=\"rtl\"><thead><tr", html); Assert.Contains("</thead><tbody>", html);
        Assert.Contains("<th dir=\"auto\">", html); Assert.Contains("<td dir=\"auto\">", html);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>", html);
        Assert.Contains("table-header-group", html); Assert.Contains("break-inside:avoid", html);
        Assert.Contains("DRAFT - UNSIGNED", html);
    }
    [Theory] [InlineData("colspan", "2")] [InlineData("rowspan", "2")] [InlineData("colwidth", "[100]")]
    [InlineData("align", "\"center\"")] [InlineData("style", "\"color:red\"")] [InlineData("colspan", "\"1\"")]
    public void Unsupported_attributes_cannot_be_flattened_or_exported(string key, string value)
    {
        var doc = Document(); doc["content"]![0]!["content"]![0]!["content"]![0]!["attrs"]![key] = JsonNode.Parse(value);
        var json = doc.ToJsonString();
        Assert.False(DocumentContentJson.TryGetPlainText(json, out _));
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(Input(json), ""));
    }
    [Fact] public void Uneven_and_nested_tables_are_rejected_without_mutating_source()
    {
        var uneven = Document(); ((JsonArray)uneven["content"]![0]!["content"]![1]!["content"]!).RemoveAt(0);
        Assert.False(DocumentContentJson.TryGetPlainText(uneven.ToJsonString(), out _));
        var nested = Document(); ((JsonArray)nested["content"]![0]!["content"]![0]!["content"]![0]!["content"]!).Add(Document()["content"]![0]!.DeepClone());
        Assert.False(DocumentContentJson.TryGetPlainText(nested.ToJsonString(), out _));
    }
    private static DraftPdfInput Input(string json) => new(Guid.NewGuid(), 1, null, json, null, null, null, null, new Dictionary<Guid, PdfImage>());
}
