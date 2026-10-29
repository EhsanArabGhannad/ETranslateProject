using System.Text;
using System.Text.Json;

namespace ETranslate.Contracts.Documents;

// Versioned JSON stays the source of truth. The rich UI only edits this explicit vocabulary.
public static class DocumentContentJson
{
    public static bool TryGetPlainText(string json, out string? plainText, string rootType = "doc", int schemaVersion = 1)
    {
        plainText = null;
        if (json.Length > 2_000_000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var remaining = 20_000;
            if (schemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10) || rootType is not ("doc" or "header" or "footer") || !Validate(document.RootElement, null, 0, ref remaining, rootType, schemaVersion)) return false;
            var lines = new List<string>();
            Extract(document.RootElement, lines);
            plainText = string.Join('\n', lines);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    private static bool Validate(JsonElement node, string? parent, int depth, ref int remaining, string rootType, int version)
    {
        if (depth > 32 || --remaining < 0 || node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String) return false;
        var type = kind.GetString()!;
        if (type is not ("doc" or "header" or "footer" or "paragraph" or "heading" or "text" or "hardBreak" or "image" or
            "bulletList" or "orderedList" or "listItem" or "blockquote" or "table" or "tableRow" or "tableCell" or "tableHeader")) return false;
        if (parent is null ? type != rootType : !CanContain(parent, type)) return false;
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) return false;
            if (field.Name is "type") continue;
            if (field.Name == "text" && type == "text" && field.Value.ValueKind == JsonValueKind.String) continue;
            if (field.Name == "marks" && type == "text" && MarksValid(field.Value, version)) continue;
            if (field.Name == "attrs" && type != "text" && AttributesValid(type, field.Value, rootType != "doc", version)) continue;
            if (field.Name == "content" && type is not ("text" or "image" or "hardBreak") &&
                field.Value.ValueKind == JsonValueKind.Array) continue;
            return false;
        }
        if (DocumentPageBreak.IsStart(node) && (parent != "doc" || rootType != "doc" || type is not ("paragraph" or "heading"))) return false;
        if (type == "text")
            return node.TryGetProperty("text", out var text) &&
                (text.GetString()!.Length > 0 || !node.TryGetProperty("marks", out var marks) || marks.GetArrayLength() == 0);
        if (type == "image") return node.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("assetId", out var id) &&
            id.ValueKind == JsonValueKind.String && Guid.TryParseExact(id.GetString(), "D", out _);
        if (type == "hardBreak") return true;
        if (type == "heading" && (!node.TryGetProperty("attrs", out var headingAttrs) || !headingAttrs.TryGetProperty("level", out _))) return false;
        if (!node.TryGetProperty("content", out var children)) return type == "paragraph" || type == "heading";
        if (type is "bulletList" or "orderedList" or "listItem" or "blockquote" or "table" or "tableRow" or "tableCell" or "tableHeader" && children.GetArrayLength() == 0) return false;
        if (type == "listItem" && (!children[0].TryGetProperty("type", out var first) || first.GetString() != "paragraph")) return false;
        foreach (var child in children.EnumerateArray())
            if (!Validate(child, type, depth + 1, ref remaining, rootType, version)) return false;
        if (type == "table")
        {
            if (children.GetArrayLength() > 50) return false;
            var columns = DocumentTableShape.Columns(children[0]);
            if (columns is < 1 or > 12 || children.EnumerateArray().Any(row => DocumentTableShape.Columns(row) != columns)) return false;
            if (node.TryGetProperty("attrs", out var tableAttrs) && tableAttrs.TryGetProperty("columnWidths", out var widths) && !DocumentTableWidths.IsValid(widths, columns)) return false;
        }
        if (type == "doc" && DocumentPageBreak.Count(node) > DocumentPageBreak.MaximumStarts) return false;
        if (type == "tableRow" && children.GetArrayLength() > 12) return false;
        return true;
    }
    private static bool CanContain(string parent, string child) => parent switch
    {
        "paragraph" or "heading" => child is "text" or "hardBreak",
        "bulletList" or "orderedList" => child == "listItem",
        "table" => child == "tableRow",
        "tableRow" => child is "tableCell" or "tableHeader",
        "tableCell" or "tableHeader" => child is "paragraph" or "heading" or "bulletList" or "orderedList" or "blockquote",
        "doc" when child == "table" => true,
        "doc" or "header" or "footer" or "listItem" or "blockquote" => child is "paragraph" or "heading" or "bulletList" or "orderedList" or "image" or "blockquote",
        _ => false
    };
    private static bool AttributesValid(string type, JsonElement attrs, bool letterhead, int version)
    {
        if (attrs.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>();
        foreach (var field in attrs.EnumerateObject())
        {
            if (!seen.Add(field.Name)) return false;
            if (version >= 10 && !letterhead && type is "paragraph" or "heading" && field.Name == "pageBreakBefore" && field.Value.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False) continue;
            if (version >= 9 && !letterhead && type == "image" && DocumentBodyImage.ValidValue(field.Name, field.Value)) continue;
            if (version >= 6 && type == "table" && field.Name == "columnWidths" && DocumentTableWidths.IsValid(field.Value)) continue;
            if(version>=3 && type is "paragraph" or "heading" && DocumentParagraph.ValidValue(field.Name,field.Value,version))continue;
            if (letterhead && type is "paragraph" or "heading" && field.Name == "textAlign" &&
                field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "left" or "center" or "right") continue;
            if (letterhead && type == "image" && field.Value.ValueKind == JsonValueKind.String &&
                (field.Name == "align" && field.Value.GetString() is "left" or "center" or "right" ||
                 field.Name == "layout" && field.Value.GetString() is "logo" or "banner")) continue;
            if (field.Name == "dir" && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "auto" or "rtl" or "ltr") continue;
            if (type == "heading" && field.Name == "level" && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var level) && level is >= 1 and <= 6) continue;
            if (type == "orderedList" && field.Name == "start" && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var start) && start is >= 1 and <= 10_000) continue;
            if (type == "orderedList" && field.Name == "type" && (field.Value.ValueKind == JsonValueKind.Null ||
                field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "1" or "a" or "A" or "i" or "I")) continue;
            if (type == "image" && field.Name == "assetId" && field.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(field.Value.GetString(), "D", out _)) continue;
            if (type is "tableCell" or "tableHeader")
            {
                if (version >= 7 && DocumentCellStyle.ValidValue(field.Name, field.Value)) continue;
                if (version >= 8 && field.Name == "colspan" && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetDecimal(out var columns) && columns is >= 1 and <= 12 && decimal.Truncate(columns) == columns) continue;
                if (field.Name is "colspan" or "rowspan" && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var span) && span == 1) continue;
                if (field.Name is "colwidth" or "align" && field.Value.ValueKind == JsonValueKind.Null) continue;
            }
            return false;
        }
        return true;
    }
    private static bool MarksValid(JsonElement marks, int version)
    {
        if (marks.ValueKind != JsonValueKind.Array) return false;
        var seen = new HashSet<string>();
        foreach (var mark in marks.EnumerateArray())
        {
            if (mark.ValueKind != JsonValueKind.Object || !mark.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || !seen.Add(type.GetString()!)) return false;
            if (version >= 2 && type.GetString() == "typography")
            {
                if (mark.EnumerateObject().Count()!=2 || !mark.TryGetProperty("attrs",out var attrs) || !DocumentTypography.ValidAttributes(attrs)) return false;
            }
            else if (mark.EnumerateObject().Count()!=1 || !(type.GetString() is "bold" or "italic" or "underline" or "strike" || version>=4 && DocumentInlineScript.IsScript(type.GetString()))) return false;
        }
        return !(seen.Contains("superscript") && seen.Contains("subscript"));
    }
    private static void Extract(JsonElement node, List<string> lines)
    {
        if (node.GetProperty("type").GetString() is "paragraph" or "heading")
        {
            var line = new StringBuilder();
            if (node.TryGetProperty("content", out var inline))
                foreach (var child in inline.EnumerateArray())
                    line.Append(child.GetProperty("type").GetString() == "hardBreak" ? "\n" : child.GetProperty("text").GetString());
            lines.Add(line.ToString());
        }
        else if (node.TryGetProperty("content", out var children))
            foreach (var child in children.EnumerateArray()) Extract(child, lines);
    }
}
