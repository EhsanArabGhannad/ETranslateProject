using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ETranslate.Contracts.Documents;

namespace ETranslate.Documents.Api.Rendering;

public static class DraftPdfHtml
{
    public static PreparedPdf Prepare(DraftPdfInput input, string fontCss)
    {
        if (!DocumentPageLayout.TryParse(input.LayoutJson, out var layout))
            throw new PdfRenderValidationException("Unsupported page layout; no PDF was generated.");
        var textLength = 0;
        string Part(string? json, string expected)
        {
            if (json is null) return "";
            using var document = ParseContent(json, expected, out var text);
            textLength += text.Length;
            if (textLength > DraftPdfPolicy.MaximumTextLength) throw new PdfRenderValidationException("Document exceeds the draft PDF text limit.");
            var html = new StringBuilder();
            Render(document.RootElement, html, input.Images);
            return html.ToString();
        }
        var body = Part(input.BodyJson, "doc");
        var header = Part(input.HeaderJson, "header");
        var footer = Part(input.FooterJson, "footer");
        var watermark = Watermark(input.WatermarkJson);
        var width = layout!.DimensionsMm.Width - layout.Left - layout.Right;
        var title = $"Translation draft - revision {input.RevisionNumber}";
        var css = $$"""
            {{fontCss}}
            * { box-sizing:border-box; }
            html { -webkit-print-color-adjust:exact; print-color-adjust:exact; }
            body { width:{{Number(width)}}mm; margin:0; font-family:'Noto Sans','Noto Sans Arabic',sans-serif; font-size:11pt; line-height:1.65; color:#20282b; }
            p { margin:0 0 3mm; min-height:1.65em; white-space:pre-wrap; overflow-wrap:anywhere; orphans:3; widows:3; }
            h1,h2,h3,h4,h5,h6 { margin:0 0 4mm; line-height:1.5; break-after:avoid; overflow-wrap:anywhere; white-space:pre-wrap; }
            h1 {font-size:20pt;} h2 {font-size:17pt;} h3 {font-size:15pt;} h4,h5,h6 {font-size:12pt;}
            ul,ol {padding-inline-start:8mm; margin:0 0 4mm;} li>p {margin-bottom:1mm;}
            blockquote {margin:3mm 0; padding-inline-start:4mm; border-inline-start:1mm solid #bbc4c9;}
            img {display:block; max-width:40mm; max-height:20mm; width:auto; height:auto; object-fit:contain; break-inside:avoid;}
            .letterhead,.letterfoot {width:{{Number(width)}}mm; font-size:9pt; line-height:1.5;}
            .letterhead {position:fixed; top:0; padding-bottom:3mm; border-bottom:.2mm solid #bcc6ca;}
            .letterfoot {position:fixed; bottom:0; padding-top:3mm; border-top:.2mm solid #bcc6ca;}
            .letterhead:empty,.letterfoot:empty {display:none;}
            .letterhead p,.letterfoot p {margin-bottom:1mm;}
            .watermark {position:fixed; inset:35% 0 auto; text-align:center; font-size:32pt; z-index:-1; overflow-wrap:anywhere;}
            .draft-watermark {position:fixed; top:48%; left:0; width:100%; text-align:center; color:#8b3131; opacity:.10; font-size:28pt; transform:rotate(-30deg); z-index:-1;}
            """;
        var htmlDocument = $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; font-src data:; style-src 'unsafe-inline'; script-src 'none'; base-uri 'none'"><title>{{Encode(title)}}</title><style>{{css}}</style></head>
            <body><header class="letterhead">{{header}}</header><footer class="letterfoot">{{footer}}</footer>{{watermark}}<div class="draft-watermark" dir="ltr">DRAFT - UNSIGNED</div><main>{{body}}</main></body></html>
            """;
        return new PreparedPdf(htmlDocument, layout);
    }

    public static IReadOnlyCollection<Guid> ImageReferences(string? json, string expectedRoot)
    {
        if (json is null) return [];
        using var document = ParseContent(json, expectedRoot, out _);
        var ids = new HashSet<Guid>();
        void Visit(JsonElement node)
        {
            if (node.GetProperty("type").GetString() == "image") ids.Add(node.GetProperty("attrs").GetProperty("assetId").GetGuid());
            if (node.TryGetProperty("content", out var children)) foreach (var child in children.EnumerateArray()) Visit(child);
        }
        Visit(document.RootElement); return ids;
    }
    private static JsonDocument ParseContent(string json, string expected, out string text)
    {
        var root = expected;
        // Existing simple templates store a doc root; explicitly support it alongside header/footer roots.
        if (!DocumentContentJson.TryGetPlainText(json, out var extracted, root) &&
            (expected == "doc" || !DocumentContentJson.TryGetPlainText(json, out extracted)))
            throw new PdfRenderValidationException("Unsupported document content; nothing was flattened or omitted.");
        text = extracted!;
        return JsonDocument.Parse(json);
    }
    private static void Render(JsonElement node, StringBuilder html, IReadOnlyDictionary<Guid, PdfImage> images)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "text")
        {
            var value = Encode(node.GetProperty("text").GetString()!);
            if (node.TryGetProperty("marks", out var marks)) foreach (var mark in marks.EnumerateArray())
            {
                var tag = mark.GetProperty("type").GetString() switch { "bold" => "strong", "italic" => "em", "underline" => "u", "strike" => "s", _ => throw new PdfRenderValidationException("Unsupported mark.") };
                value = $"<{tag}>{value}</{tag}>";
            }
            html.Append(value); return;
        }
        if (type == "hardBreak") { html.Append("<br>"); return; }
        node.TryGetProperty("attrs", out var attrs);
        var direction = attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("dir", out var dir) ? dir.GetString() : "auto";
        if (type == "image")
        {
            var id = attrs.GetProperty("assetId").GetGuid();
            if (!images.TryGetValue(id, out var image) || image.ContentType is not ("image/png" or "image/jpeg") ||
                image.Bytes.LongLength is <= 0 or > 5 * 1024 * 1024)
                throw new PdfRenderValidationException("A managed template image is unavailable.");
            html.Append($"<img alt=\"Template image\" src=\"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}\">"); return;
        }
        var tagName = type switch
        {
            "paragraph" => "p", "heading" => $"h{attrs.GetProperty("level").GetInt32()}",
            "bulletList" => "ul", "orderedList" => "ol", "listItem" => "li", "blockquote" => "blockquote", _ => "div"
        };
        html.Append($"<{tagName} dir=\"{direction}\"");
        if (type == "orderedList" && attrs.ValueKind == JsonValueKind.Object)
        {
            if (attrs.TryGetProperty("start", out var start)) html.Append($" start=\"{start.GetInt32()}\"");
            if (attrs.TryGetProperty("type", out var listType) && listType.ValueKind == JsonValueKind.String) html.Append($" type=\"{listType.GetString()}\"");
        }
        html.Append('>');
        if (node.TryGetProperty("content", out var content)) foreach (var child in content.EnumerateArray()) Render(child, html, images);
        html.Append($"</{tagName}>");
    }
    private static string Watermark(string? json)
    {
        if (json is null) return "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(field => field.Name).Distinct().Count() != value.EnumerateObject().Count() ||
                value.EnumerateObject().Any(field => field.Name is not ("text" or "opacity" or "rotation")) ||
                !value.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String || text.GetString()!.Length > 200)
                throw new PdfRenderValidationException("Unsupported watermark settings.");
            var opacity = value.TryGetProperty("opacity", out var alpha) ? alpha.GetDecimal() : .12m;
            var rotation = value.TryGetProperty("rotation", out var angle) ? angle.GetDecimal() : -35m;
            if (opacity is < 0 or > .3m || rotation is < -90 or > 90) throw new PdfRenderValidationException("Watermark values are out of range.");
            return $"<div class=\"watermark\" dir=\"auto\" style=\"opacity:{Number(opacity)};transform:rotate({Number(rotation)}deg)\">{Encode(text.GetString()!)}</div>";
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new PdfRenderValidationException("Unsupported watermark settings."); }
    }
    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
