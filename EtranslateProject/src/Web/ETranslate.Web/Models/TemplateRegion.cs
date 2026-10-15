using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;

namespace ETranslate.Web.Models;

// A deliberately small, lossless editing vocabulary. Rich/unknown regions stay server-owned.
public static class TemplateRegion
{
    public static bool AlignmentsValid(string? json)
    {
        if (json is null) return true;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.ValueKind == JsonValueKind.Array &&
            doc.RootElement.GetArrayLength() <= 10001 && doc.RootElement.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && v.GetString() is "auto" or "left" or "center" or "right"); }
        catch (JsonException) { return false; }
    }
    public static string[] Alignments(string? json) => json is null ? [] : JsonSerializer.Deserialize<string[]>(json)!;
    public static bool Read(string? json, string region, out string? text, out string[] alignments, out Guid[] images)
    {
        text = null; alignments = []; images = [];
        if (json is null) { text = ""; return true; }
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null || root["type"]?.ToString() is not ("doc" or "header" or "footer") ||
                root["type"]!.ToString() != "doc" && root["type"]!.ToString() != region ||
                root.Any(p => p.Key is not ("type" or "content")) || root["content"] is not JsonArray blocks ||
                !DocumentContentJson.TryGetPlainText(json, out _, root["type"]!.ToString())) return false;
            var paragraphs = new JsonArray(); var ids = new List<Guid>(); var aligns = new List<string>(); var imageSeen = false;
            foreach (var block in blocks)
            {
                if (block?["type"]?.ToString() == "image")
                {
                    if (region != "header" || block["attrs"] is not JsonObject imageAttrs || imageAttrs.Any(a => a.Key is not ("assetId" or "align" or "layout"))) return false;
                    imageSeen = true; ids.Add(Guid.Parse(block["attrs"]!["assetId"]!.ToString()));
                }
                else
                {
                    if (imageSeen || block?["type"]?.ToString() != "paragraph") return false;
                    var clone = block.DeepClone(); aligns.Add(clone["attrs"]?["textAlign"]?.ToString() ?? "auto");
                    if (clone["attrs"] is JsonObject attrs) { attrs.Remove("textAlign"); if (attrs.Count == 0) clone.AsObject().Remove("attrs"); }
                    paragraphs.Add(clone);
                }
            }
            text = DocumentText.TryPlainText(new JsonObject { ["type"] = "doc", ["content"] = paragraphs }.ToJsonString());
            if (text is null) return false;
            alignments = aligns.ToArray(); images = ids.ToArray(); return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException) { return false; }
    }
    public static string? Build(string? original, string region, string? text, string? alignmentJson)
    {
        Read(original, region, out var oldText, out var oldAlignments, out _);
        var desired = alignmentJson is null ? oldAlignments : Alignments(alignmentJson);
        if ((oldText ?? "") == (text ?? "") && oldAlignments.Select((a, i) => a == desired.ElementAtOrDefault(i)).All(v => v) &&
            desired.Skip(oldAlignments.Length).All(a => a == "auto")) return original;
        var root = original is null ? new JsonObject { ["type"] = region, ["content"] = new JsonArray() } : JsonNode.Parse(original)!.AsObject();
        var images = ((JsonArray)root["content"]!).Where(b => b?["type"]?.ToString() == "image").Select(b => b!.DeepClone()).ToArray();
        var paragraphs = string.IsNullOrEmpty(text) ? new JsonArray() : JsonNode.Parse(DocumentText.FromPlainText(text))!["content"]!.AsArray().DeepClone().AsArray();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var align = desired.ElementAtOrDefault(i) ?? "auto";
            if (align != "auto") paragraphs[i]!["attrs"] = new JsonObject { ["textAlign"] = align };
        }
        foreach (var image in images) paragraphs.Add(image);
        root["content"] = paragraphs;
        // New presentation attrs are accepted only in header/footer, never in rich document bodies.
        if (desired.Any(a => a != "auto")) root["type"] = region;
        return paragraphs.Count == 0 ? null : root.ToJsonString();
    }
}
