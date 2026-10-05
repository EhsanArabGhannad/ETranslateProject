using System.Text.Json;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Documents;

public sealed class RichDocumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "document-cases.json")));
        foreach (var fixture in fixtures.RootElement.EnumerateArray())
            yield return [fixture.GetProperty("name").GetString()!, fixture.GetProperty("document").GetRawText(), fixture.GetProperty("valid").GetBoolean()];
    }
    [Theory, MemberData(nameof(Cases))]
    public void SharedSchemaPreservationCases(string name, string json, bool expected)
        => Assert.True(RichDocument.TryGetPlainText(json, out _) == expected, name);

    [Fact] public void ExtractsRichMultilingualPlainTextWithoutFlatteningStoredJson()
    {
        const string json = """
            {"type":"doc","content":[{"type":"heading","attrs":{"level":2,"dir":"rtl"},"content":[{"type":"text","text":"ترجمه","marks":[{"type":"bold"}]}]},{"type":"paragraph","content":[{"type":"text","text":"Türkçe"},{"type":"hardBreak"},{"type":"text","text":"<script>literal</script>"}]}]}
            """;
        Assert.True(RichDocument.TryGetPlainText(json, out var text));
        Assert.Equal("ترجمه\nTürkçe\n<script>literal</script>", text);
        Assert.Contains("\"bold\"", json);
    }
    [Theory]
    [InlineData("{\"type\":\"doc\",\"type\":\"doc\",\"content\":[]}")]
    [InlineData("{\"type\":\"doc\",\"attrs\":{\"dir\":\"ltr\",\"dir\":\"rtl\"},\"content\":[]}")]
    [InlineData("not json")]
    public void DuplicateOrInvalidJsonIsNotRichEditable(string json) => Assert.False(RichDocument.TryGetPlainText(json, out _));

    [Theory]
    [InlineData("application/pdf", 26214400, true)]
    [InlineData("image/png", 100, true)]
    [InlineData("image/jpeg", 100, true)]
    [InlineData("image/tiff", 100, true)]
    [InlineData("image/svg+xml", 100, false)]
    [InlineData("text/html", 100, false)]
    [InlineData("application/pdf", 26214401, false)]
    [InlineData("application/pdf", 0, false)]
    public void SourceUploadGuard(string type, long size, bool allowed) => Assert.Equal(allowed, SourceFileRules.IsAllowed(type, size));
}
