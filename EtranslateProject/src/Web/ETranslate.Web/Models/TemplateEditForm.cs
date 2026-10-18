using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;

namespace ETranslate.Web.Models;

public sealed class TemplateEditForm
{
    public Guid TenantId { get; set; }
    public Guid? TemplateId { get; set; }
    public int ExpectedRevision { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(10000)] public string? Header { get; set; }
    [StringLength(2000)] public string? Footer { get; set; }
    [StringLength(50000)] public string? StarterText { get; set; }
    [StringLength(100)] public string? Watermark { get; set; }
    public string PageSize { get; set; } = "A4";
    public string Orientation { get; set; } = "Portrait";
    public string FooterPlacement { get; set; } = "AllPages";
    public decimal MarginTop { get; set; } = 25;
    public decimal MarginRight { get; set; } = 20;
    public decimal MarginBottom { get; set; } = 20;
    public decimal MarginLeft { get; set; } = 20;
    public string LogoMode { get; set; } = "Add";
    public Guid? PendingAssetId { get; set; }
    [StringLength(100000)] public string? HeaderLineAlignmentsJson { get; set; }
    [StringLength(30000)] public string? FooterLineAlignmentsJson { get; set; }
    public string? ImageAlignment { get; set; }
    public string? ImageLayout { get; set; }
    public DocumentPageLayout Layout => new(PageSize, Orientation, MarginTop, MarginRight, MarginBottom, MarginLeft) { FooterPlacement = FooterPlacement };
}

// Only project truly plain parts. Never turn rich/unknown content into editable plain text.
public sealed class TemplateProjection
{
    public bool HeaderEditable { get; private init; }
    public bool FooterEditable { get; private init; }
    public bool BodyEditable { get; private init; }
    public bool LayoutEditable { get; private init; }
    public bool WatermarkEditable { get; private init; }
    public bool CanAppendLogo { get; private init; }
    public IReadOnlyList<Guid> Images { get; private init; } = [];
    public static TemplateProjection From(TemplateDetail? detail, Guid tenantId, out TemplateEditForm form)
    {
        form = new() { TenantId = tenantId, TemplateId = detail?.Id, ExpectedRevision = detail?.CurrentRevision ?? 0, Name = detail?.Name ?? "" };
        var revision = detail?.Revision;
        var header = revision?.HeaderContentJson;
        var headerPlain = HeaderText(header, out var imageIds);
        TemplateRegion.Read(header, "header", out _, out var headerAlignments, out _);
        TemplateRegion.Read(revision?.FooterContentJson, "footer", out var footerPlain, out var footerAlignments, out _);
        form.HeaderLineAlignmentsJson = JsonSerializer.Serialize(headerAlignments);
        form.FooterLineAlignmentsJson = JsonSerializer.Serialize(footerAlignments);
        var bodyPlain = revision is null ? "" : PlainText(revision.EditorContentJson);
        if (headerPlain?.Length > 10000) headerPlain = null;
        if (footerPlain?.Length > 2000) footerPlain = null;
        if (bodyPlain?.Length > 50000) bodyPlain = null;
        form.Header = headerPlain; form.Footer = footerPlain; form.StarterText = bodyPlain;
        if (headerPlain is not null && imageIds.Count > 0)
        {
            var imageAttrs = HeaderRoot(header)!["content"]!.AsArray().OfType<JsonObject>().Where(n => n["type"]?.ToString() == "image").Select(n => n["attrs"]!).ToArray();
            var layouts = imageAttrs.Select(a => a["layout"]?.ToString()).Distinct().ToArray();
            var aligns = imageAttrs.Select(a => a["align"]?.ToString()).Distinct().ToArray();
            if (layouts.Length == 1) form.ImageLayout = layouts[0];
            if (aligns.Length == 1) form.ImageAlignment = aligns[0];
        }
        var layoutValid = DocumentPageLayout.TryParse(revision?.PageLayoutJson, out var layout);
        if (layoutValid) { form.PageSize = layout!.PageSize; form.Orientation = layout.Orientation; form.FooterPlacement = layout.FooterPlacement; form.MarginTop = layout.Top; form.MarginRight = layout.Right; form.MarginBottom = layout.Bottom; form.MarginLeft = layout.Left; }
        var watermarkValid = TryWatermark(revision?.WatermarkJson, out var watermark);
        form.Watermark = watermark;
        return new() { HeaderEditable = headerPlain is not null, FooterEditable = footerPlain is not null, BodyEditable = bodyPlain is not null,
            LayoutEditable = layoutValid, WatermarkEditable = watermarkValid, CanAppendLogo = Appendable(header), Images = imageIds };
    }

    private static JsonObject? HeaderRoot(string? json)
    {
        if (json is null) return JsonNode.Parse(DocumentText.Empty)!.AsObject();
        try { var root = JsonNode.Parse(json) as JsonObject; return root?["type"]?.GetValue<string>() is "doc" or "header" && root["content"] is JsonArray ? root : null; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return null; }
    }
    private static string? PlainText(string json)
    { try { return DocumentText.TryPlainText(json); } catch (ArgumentException) { return null; } }
    private static bool Appendable(string? json) => HeaderRoot(json) is not null;
    private static string? HeaderText(string? json, out IReadOnlyList<Guid> images)
    {
        try { return HeaderTextCore(json, out images); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { images = []; return null; }
    }
    private static string? HeaderTextCore(string? json, out IReadOnlyList<Guid> images)
    {
        var supported = TemplateRegion.Read(json, "header", out var plain, out _, out var ids);
        images = ids; return supported ? plain : null;
    }
    private static bool TryWatermark(string? json, out string? text)
    {
        text = ""; if (json is null) return true;
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("text" or "opacity" or "rotation")) ||
                root.EnumerateObject().Select(p => p.Name).Distinct().Count() != root.EnumerateObject().Count() ||
                !root.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 100) return false;
            text = value.GetString(); return true;
        }
        catch (JsonException) { return false; }
    }
    public TemplateContent Build(TemplateEditForm form, TemplateRevision? original, Guid? imageId)
    {
        var header = original?.HeaderContentJson;
        if (HeaderEditable) header = TemplateRegion.Build(header, "header", form.Header, form.HeaderLineAlignmentsJson);
        if (imageId.HasValue)
        {
            var root = HeaderRoot(header) ?? throw new InvalidOperationException("Header cannot contain a managed logo.");
            var content = (JsonArray)root["content"]!;
            if (form.LogoMode == "Replace")
            {
                if (!HeaderEditable) throw new InvalidOperationException("Unknown header images cannot be replaced by the simple form.");
                for (var i = content.Count - 1; i >= 0; i--) if (content[i]?["type"]?.ToString() == "image") content.RemoveAt(i);
            }
            if (!content.Any(b => b is JsonObject o && o["type"]?.ToString() == "image" && o["attrs"] is JsonObject attrs && attrs["assetId"]?.ToString() == imageId.Value.ToString()))
                content.Add(new JsonObject { ["type"] = "image", ["attrs"] = new JsonObject { ["assetId"] = imageId.Value.ToString() } });
            header = root.ToJsonString();
        }
        if (HeaderEditable && (form.ImageAlignment is not null || form.ImageLayout is not null) && header is not null)
        {
            var root = HeaderRoot(header)!;
            var changed = false;
            foreach (var image in ((JsonArray)root["content"]!).OfType<JsonObject>().Where(b => b["type"]?.ToString() == "image"))
            {
                if (form.ImageAlignment is not null && image["attrs"]!["align"]?.ToString() != form.ImageAlignment) { image["attrs"]!["align"] = form.ImageAlignment; changed = true; }
                if (form.ImageLayout is not null && image["attrs"]!["layout"]?.ToString() != form.ImageLayout) { image["attrs"]!["layout"] = form.ImageLayout; changed = true; }
            }
            if (changed) { root["type"] = "header"; header = root.ToJsonString(); }
        }
        var watermark = original?.WatermarkJson;
        if (WatermarkEditable && (original is null || !TryWatermark(watermark, out var oldText) || oldText != (form.Watermark ?? "")))
        {
            if (string.IsNullOrWhiteSpace(form.Watermark)) watermark = null;
            else
            {
                var root = watermark is null ? new JsonObject { ["opacity"] = .12m, ["rotation"] = -35 } : JsonNode.Parse(watermark)!.AsObject();
                root["text"] = form.Watermark; watermark = root.ToJsonString();
            }
        }
        var body = original?.EditorContentJson;
        if (BodyEditable && (original is null || DocumentText.TryPlainText(body!) != (form.StarterText ?? ""))) body = DocumentText.FromPlainText(form.StarterText ?? "");
        var footer = original?.FooterContentJson;
        if (FooterEditable) footer = TemplateRegion.Build(footer, "footer", form.Footer, form.FooterLineAlignmentsJson);
        var layout = original?.PageLayoutJson;
        if (LayoutEditable && (original is null || !DocumentPageLayout.TryParse(layout, out var oldLayout) || oldLayout != form.Layout)) layout = form.Layout.ToJson();
        return new(body!, header, footer, layout!, watermark);
    }
}
public sealed record TemplateContent(string EditorContentJson, string? HeaderContentJson, string? FooterContentJson, string PageLayoutJson, string? WatermarkJson);
