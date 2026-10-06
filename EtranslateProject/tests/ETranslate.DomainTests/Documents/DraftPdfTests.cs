using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class DraftPdfTests
{
    [Theory]
    [InlineData("A4", "Portrait", 210, 297)]
    [InlineData("A4", "Landscape", 297, 210)]
    [InlineData("A5", "Portrait", 148, 210)]
    [InlineData("Letter", "Portrait", 215.9, 279.4)]
    [InlineData("Legal", "Landscape", 355.6, 215.9)]
    public void Layout_RoundTrips_WithPhysicalDimensions(string size, string orientation, decimal width, decimal height)
    {
        var original = new DocumentPageLayout(size, orientation, 15.5m, 17, 18, 19);
        Assert.True(DocumentPageLayout.TryParse(original.ToJson(), out var parsed));
        Assert.Equal(original, parsed);
        Assert.Equal((width, height), parsed!.DimensionsMm);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"pageSize\":\"A4\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":0,\"right\":20,\"bottom\":20,\"left\":20}}")]
    [InlineData("{\"pageSize\":\"A4\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":\"25\",\"right\":20,\"bottom\":20,\"left\":20}}")]
    [InlineData("{\"pageSize\":\"A3\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":25,\"right\":20,\"bottom\":20,\"left\":20}}")]
    [InlineData("{\"pageSize\":\"A4\",\"pageSize\":\"A5\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":25,\"right\":20,\"bottom\":20,\"left\":20}}")]
    [InlineData("{\"pageSize\":\"A4\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":25,\"right\":20,\"bottom\":20,\"left\":20},\"css\":\"keep-me\"}")]
    public void Layout_RejectsUnsupportedOrAmbiguousJson(string json) => Assert.False(DocumentPageLayout.TryParse(json, out _));
    [Fact] public void NoTemplate_UsesDefaultA4() => Assert.Equal(DocumentPageLayout.Default, DraftPdfHtml.Prepare(Input(), "").Layout);
    [Fact] public void Content_IsEncoded_Directional_AndAlwaysMarkedDraft()
    {
        var prepared = DraftPdfHtml.Prepare(Input("""
            {"type":"doc","content":[{"type":"heading","attrs":{"level":2,"dir":"rtl"},"content":[{"type":"text","text":"<script>alert(1)</script>","marks":[{"type":"bold"}]}]},{"type":"paragraph","attrs":{"dir":"ltr"},"content":[{"type":"text","text":"Türkçe"}]}]}
            """), "");
        Assert.DoesNotContain("<script>", prepared.Html);
        Assert.Contains("&lt;script&gt;", prepared.Html);
        Assert.Contains("<h2 dir=\"rtl\"><strong>", prepared.Html);
        Assert.Contains("<p dir=\"ltr\">", prepared.Html);
        Assert.Contains("DRAFT - UNSIGNED", prepared.Html);
        Assert.Contains("script-src 'none'", prepared.Html);
    }
    [Theory]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"table\",\"content\":[]}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"https://example.org/a.png\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[],\"custom\":\"preserve\"}")]
    [InlineData("{\"type\":\"doc\",\"type\":\"doc\",\"content\":[]}")]
    public void UnknownContent_IsRejected_NotFlattened(string json)
        => Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(Input(json), ""));
    [Fact] public void HeaderAndFooterRoots_AreSupported_WithoutRelaxingBodySchema()
    {
        var input = Input() with { HeaderJson = "{\"type\":\"header\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Pinned header\"}]}]}",
            FooterJson = "{\"type\":\"footer\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Pinned footer\"}]}]}" };
        var html = DraftPdfHtml.Prepare(input, "").Html;
        Assert.Contains("Pinned header", html); Assert.Contains("Pinned footer", html);
        Assert.False(DocumentContentJson.TryGetPlainText(input.HeaderJson, out _));
    }
    [Fact] public void Image_MustBeAvailable_AndUsesOnlyEmbeddedManagedBytes()
    {
        var id = Guid.NewGuid();
        var input = Input($"{{\"type\":\"doc\",\"content\":[{{\"type\":\"image\",\"attrs\":{{\"assetId\":\"{id}\"}}}}]}}");
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(input, ""));
        input = input with { Images = new Dictionary<Guid, PdfImage> { [id] = new("image/png", [1, 2, 3]) } };
        Assert.Contains("data:image/png;base64,AQID", DraftPdfHtml.Prepare(input, "").Html);
        Assert.Equal([id], DraftPdfHtml.ImageReferences(input.BodyJson, "doc"));
    }
    [Theory]
    [InlineData("{\"text\":\"test\",\"opacity\":1}")]
    [InlineData("{\"text\":\"test\",\"rotation\":200}")]
    [InlineData("{\"text\":\"test\",\"css\":\"url(https://example.org)\"}")]
    [InlineData("{\"text\":\"a\",\"text\":\"b\"}")]
    public void Watermark_RejectsUnknownOrUnsafeValues(string json)
        => Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(Input() with { WatermarkJson = json }, ""));
    [Fact] public void RendererLengthLimit_IsExplicit()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { type = "doc", content = new[] {
            new { type = "paragraph", content = new[] { new { type = "text", text = new string('a', DraftPdfPolicy.MaximumTextLength + 1) } } } } });
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(Input(json), ""));
    }
    private static DraftPdfInput Input(string json = "{\"type\":\"doc\",\"content\":[]}")
        => new(Guid.NewGuid(), 1, null, json, null, null, null, null, new Dictionary<Guid, PdfImage>());
}
