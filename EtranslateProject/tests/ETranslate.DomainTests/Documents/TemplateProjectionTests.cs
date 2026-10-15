using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Documents;

public sealed class TemplateProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Template = Guid.NewGuid(), Asset = Guid.NewGuid();
    private static TemplateDetail Detail(string? body = null, string? header = null, string? footer = null, string? layout = null, string? watermark = null)
        => new(Template, "Name", null, true, 2, new(Guid.NewGuid(), Template, 2, body ?? DocumentText.FromPlainText("Starter"),
            header ?? DocumentText.FromPlainText("Header"), footer ?? DocumentText.FromPlainText("Footer"), layout ?? DocumentPageLayout.Default.ToJson(), watermark));
    [Fact] public void Plain_fields_are_loaded_and_unchanged_parts_remain_exact()
    {
        var original = Detail(watermark: "{\"rotation\":-10,\"text\":\"DRAFT\",\"opacity\":0.2}");
        var projection = TemplateProjection.From(original, Tenant, out var form);
        Assert.Equal("Header", form.Header); Assert.Equal("Footer", form.Footer); Assert.Equal("Starter", form.StarterText); Assert.Equal("DRAFT", form.Watermark);
        var content = projection.Build(form, original.Revision, null);
        Assert.Equal(original.Revision.EditorContentJson, content.EditorContentJson); Assert.Equal(original.Revision.HeaderContentJson, content.HeaderContentJson);
        Assert.Equal(original.Revision.FooterContentJson, content.FooterContentJson); Assert.Equal(original.Revision.PageLayoutJson, content.PageLayoutJson); Assert.Equal(original.Revision.WatermarkJson, content.WatermarkJson);
    }
    [Theory] [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":2},\"content\":[{\"type\":\"text\",\"text\":\"Rich\"}]}]}")]
    [InlineData("[{\"custom\":\"unknown\"}]")] [InlineData("{\"type\":\"doc\",\"type\":\"doc\",\"content\":[]}")]
    public void Rich_unknown_or_duplicate_body_is_never_flattened(string body)
    {
        var original = Detail(body: body); var projection = TemplateProjection.From(original, Tenant, out var form);
        Assert.False(projection.BodyEditable); form.StarterText = "Forged replacement";
        Assert.Equal(body, projection.Build(form, original.Revision, null).EditorContentJson);
    }
    [Fact] public void Header_logos_are_kept_replaced_only_explicitly_and_not_duplicated()
    {
        var root = JsonNode.Parse(DocumentText.FromPlainText("Header"))!;
        root["content"]!.AsArray().Add(new JsonObject { ["type"] = "image", ["attrs"] = new JsonObject { ["assetId"] = Asset.ToString() } });
        var original = Detail(header: root.ToJsonString()); var projection = TemplateProjection.From(original, Tenant, out var form);
        Assert.True(projection.HeaderEditable); Assert.Equal("Header", form.Header); Assert.Equal([Asset], projection.Images);
        form.Header = "Updated";
        var kept = projection.Build(form, original.Revision, Asset);
        Assert.Single(JsonNode.Parse(kept.HeaderContentJson!)!["content"]!.AsArray(), n => n!["type"]!.ToString() == "image");
        form.LogoMode = "Replace"; var replacement = Guid.NewGuid();
        var replaced = projection.Build(form, original.Revision, replacement);
        Assert.DoesNotContain(Asset.ToString(), replaced.HeaderContentJson); Assert.Contains(replacement.ToString(), replaced.HeaderContentJson);
        Assert.Contains(Asset.ToString(), original.Revision.HeaderContentJson);
    }
    [Fact] public void Interleaved_header_and_unknown_footer_layout_watermark_are_preserved()
    {
        const string header = "{\"type\":\"doc\",\"custom\":42,\"content\":[]}";
        var original = Detail(header: header, footer: "[]", layout: "{\"customLayout\":true}", watermark: "{\"text\":\"Keep\",\"custom\":true}");
        var projection = TemplateProjection.From(original, Tenant, out var form);
        Assert.False(projection.HeaderEditable); Assert.False(projection.FooterEditable); Assert.False(projection.LayoutEditable); Assert.False(projection.WatermarkEditable);
        form.Header = "tamper"; form.Footer = "tamper"; form.Watermark = "tamper";
        var content = projection.Build(form, original.Revision, null);
        Assert.Equal(header, content.HeaderContentJson); Assert.Equal("[]", content.FooterContentJson); Assert.Equal(original.Revision.PageLayoutJson, content.PageLayoutJson); Assert.Equal(original.Revision.WatermarkJson, content.WatermarkJson);
    }
    [Fact] public void Watermark_text_edit_retains_other_settings_and_empty_regions_do_not_reserve_blank_lines()
    {
        var original = Detail(watermark: "{\"text\":\"OLD\",\"opacity\":0.2,\"rotation\":10}");
        var projection = TemplateProjection.From(original, Tenant, out var form); form.Watermark = "NEW";
        var changed = JsonNode.Parse(projection.Build(form, original.Revision, null).WatermarkJson!)!;
        Assert.Equal(.2m, changed["opacity"]!.GetValue<decimal>()); Assert.Equal(10, changed["rotation"]!.GetValue<int>());
        var fresh = TemplateProjection.From(null, Tenant, out var blank).Build(blank, null, null);
        Assert.Null(fresh.HeaderContentJson); Assert.Null(fresh.FooterContentJson);
    }
}
