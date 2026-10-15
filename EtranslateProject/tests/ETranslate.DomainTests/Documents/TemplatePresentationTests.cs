using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Documents;

public sealed class TemplatePresentationTests
{
    private static readonly Guid Asset = Guid.NewGuid();
    [Theory] [InlineData("left")] [InlineData("center")] [InlineData("right")]
    public void Region_alignment_and_banner_round_trip_without_relaxing_body_schema(string align)
    {
        var projection = TemplateProjection.From(null, Guid.NewGuid(), out var form);
        form.Header = "Office\nAddress"; form.HeaderLineAlignmentsJson = $"[\"{align}\",\"right\"]";
        form.Footer = "Contact"; form.FooterLineAlignmentsJson = "[\"center\"]";
        form.ImageAlignment = align; form.ImageLayout = "banner";
        var built = projection.Build(form, null, Asset);
        Assert.True(DocumentContentJson.TryGetPlainText(built.HeaderContentJson!, out var header, "header")); Assert.Equal(form.Header, header);
        Assert.False(DocumentContentJson.TryGetPlainText(built.HeaderContentJson!, out _));
        var detail = new TemplateDetail(Guid.NewGuid(), "Banner", null, true, 1, new(Guid.NewGuid(), Guid.NewGuid(), 1, built.EditorContentJson,
            built.HeaderContentJson, built.FooterContentJson, built.PageLayoutJson, null));
        var loaded = TemplateProjection.From(detail, form.TenantId, out var read);
        Assert.True(loaded.HeaderEditable); Assert.True(loaded.FooterEditable);
        Assert.Equal("banner", read.ImageLayout); Assert.Equal(align, read.ImageAlignment);
        Assert.Equal(form.HeaderLineAlignmentsJson, read.HeaderLineAlignmentsJson); Assert.Equal(form.FooterLineAlignmentsJson, read.FooterLineAlignmentsJson);
        var exact = loaded.Build(read, detail.Revision, null);
        Assert.Equal(built.HeaderContentJson, exact.HeaderContentJson); Assert.Equal(built.FooterContentJson, exact.FooterContentJson);
        var html = DraftPdfHtml.Prepare(new(Guid.NewGuid(), 1, null, built.EditorContentJson, built.HeaderContentJson, built.FooterContentJson, built.PageLayoutJson, null,
            new Dictionary<Guid, PdfImage> { [Asset] = new("image/png", [1,2,3]) }), "").Html;
        Assert.Contains($"text-align:{align}", html); Assert.Contains($"template-banner image-{align}", html);
        Assert.Contains("max-height:30mm", html);
    }
    [Theory] [InlineData("[\"justify\"]")] [InlineData("[\"right; color:red\"]")] [InlineData("{}")]
    [InlineData("[null]")] [InlineData("[1]")] [InlineData("[")]
    public void Alignment_inputs_are_explicitly_allowlisted(string json) => Assert.False(TemplateRegion.AlignmentsValid(json));
    [Fact] public void Unknown_metadata_is_not_stripped_to_enable_editing()
    {
        var json = "{\"type\":\"header\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"textAlign\":\"center\",\"style\":\"keep\"},\"content\":[]}]}";
        Assert.False(TemplateRegion.Read(json, "header", out _, out _, out _));
        Assert.False(DocumentContentJson.TryGetPlainText(json, out _, "header"));
    }
    [Fact] public void Body_with_presentation_attrs_is_not_accepted_as_rich_editable_content()
    {
        var json = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"textAlign\":\"center\"},\"content\":[]}]}";
        Assert.False(DocumentContentJson.TryGetPlainText(json, out _));
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(new(Guid.NewGuid(),1,null,json,null,null,null,null,new Dictionary<Guid,PdfImage>()), ""));
    }
    [Fact] public void Old_revisions_are_exact_and_alignment_only_change_retains_images()
    {
        var original = $"{{\"type\":\"doc\",\"content\":[{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"Keep\"}}]}},{{\"type\":\"image\",\"attrs\":{{\"assetId\":\"{Asset}\"}}}}]}}";
        Assert.Equal(original, TemplateRegion.Build(original, "header", "Keep", "[\"auto\"]"));
        var edited = TemplateRegion.Build(original, "header", "Keep", "[\"center\"]");
        Assert.Contains(Asset.ToString(), edited); Assert.Contains("textAlign", edited); Assert.DoesNotContain("textAlign", original);
    }
}
