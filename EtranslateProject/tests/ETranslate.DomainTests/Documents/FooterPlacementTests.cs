using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Documents;

public sealed class FooterPlacementTests
{
    [Fact] public void Legacy_default_is_all_pages_and_json_stays_exact()
    {
        const string legacy = "{\"pageSize\":\"A4\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":25,\"right\":20,\"bottom\":20,\"left\":20}}";
        Assert.True(DocumentPageLayout.TryParse(legacy, out var value)); Assert.Equal("AllPages", value!.FooterPlacement);
        Assert.Equal(legacy, value.ToJson()); Assert.True(DocumentPageLayout.TryParse(null, out value)); Assert.Equal("AllPages", value!.FooterPlacement);
    }
    [Theory] [InlineData("AllPages")] [InlineData("LastPage")]
    public void Policy_round_trips_in_layout_and_form(string policy)
    {
        var layout = DocumentPageLayout.Default with { FooterPlacement = policy };
        Assert.True(layout.IsValid); Assert.True(DocumentPageLayout.TryParse(layout.ToJson(), out var read)); Assert.Equal(layout, read);
        var detail = new TemplateDetail(Guid.NewGuid(), "Footer", null, true, 1, new(Guid.NewGuid(), Guid.NewGuid(), 1, DocumentText.Empty,
            null, DocumentText.FromPlainText("Contact"), layout.ToJson(), null));
        var projection = TemplateProjection.From(detail, Guid.NewGuid(), out var form); Assert.Equal(policy, form.FooterPlacement);
        Assert.Equal(layout.ToJson(), projection.Build(form, detail.Revision, null).PageLayoutJson);
        form.FooterPlacement = policy == "LastPage" ? "AllPages" : "LastPage";
        var revised = projection.Build(form, detail.Revision, null); Assert.True(DocumentPageLayout.TryParse(revised.PageLayoutJson, out var changed));
        Assert.Equal(form.FooterPlacement, changed!.FooterPlacement); Assert.Equal(layout.ToJson(), detail.Revision.PageLayoutJson);
    }
    [Theory] [InlineData("null")] [InlineData("true")] [InlineData("42")] [InlineData("\"lastpage\"")]
    [InlineData("\"OddPages\"")] [InlineData("[]")]
    public void Unknown_or_wrong_type_policy_is_rejected_without_defaulting(string json)
    {
        var root = JsonNode.Parse(DocumentPageLayout.Default.ToJson())!; root["footerPlacement"] = JsonNode.Parse(json);
        Assert.False(DocumentPageLayout.TryParse(root.ToJsonString(), out _));
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(new(Guid.NewGuid(), 1, null, DocumentText.Empty, null, null, root.ToJsonString(), null, new Dictionary<Guid, PdfImage>()), ""));
    }
    [Fact] public void Duplicate_policy_or_unknown_layout_is_not_discarded()
    {
        var duplicate = DocumentPageLayout.Default.ToJson()[..^1] + ",\"footerPlacement\":\"AllPages\",\"footerPlacement\":\"LastPage\"}";
        Assert.False(DocumentPageLayout.TryParse(duplicate, out _));
        var detail = new TemplateDetail(Guid.NewGuid(), "Unknown", null, true, 1, new(Guid.NewGuid(), Guid.NewGuid(), 1, DocumentText.Empty, null, null, duplicate, null));
        var projection = TemplateProjection.From(detail, Guid.NewGuid(), out var form); Assert.False(projection.LayoutEditable);
        form.FooterPlacement = "LastPage"; Assert.Equal(duplicate, projection.Build(form, detail.Revision, null).PageLayoutJson);
    }
}
