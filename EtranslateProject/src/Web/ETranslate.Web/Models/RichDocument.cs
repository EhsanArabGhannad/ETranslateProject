using System.Text;
using System.Text.Json;

namespace ETranslate.Web.Models;

// Versioned JSON stays the source of truth. The rich UI only edits this explicit vocabulary.
public static class RichDocument
{
    public static bool TryGetPlainText(string json, out string? plainText)
    {
        plainText = null;
        if (json.Length > 2_000_000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var remaining = 20_000;
            if (!Validate(document.RootElement, null, 0, ref remaining)) return false;
            var lines = new List<string>();
            Extract(document.RootElement, lines);
            plainText = string.Join('\n', lines);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    private static bool Validate(JsonElement node, string? parent, int depth, ref int remaining)
    {
        if (depth > 32 || --remaining < 0 || node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String) return false;
        var type = kind.GetString()!;
        if (type is not ("doc" or "paragraph" or "heading" or "text" or "hardBreak" or "image" or
            "bulletList" or "orderedList" or "listItem" or "blockquote")) return false;
        if (parent is null ? type != "doc" : !CanContain(parent, type)) return false;
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) return false;
            if (field.Name is "type") continue;
            if (field.Name == "text" && type == "text" && field.Value.ValueKind == JsonValueKind.String) continue;
            if (field.Name == "marks" && type == "text" && MarksValid(field.Value)) continue;
            if (field.Name == "attrs" && type != "text" && AttributesValid(type, field.Value)) continue;
            if (field.Name == "content" && type is not ("text" or "image" or "hardBreak") &&
                field.Value.ValueKind == JsonValueKind.Array) continue;
            return false;
        }
        if (type == "text")
            return node.TryGetProperty("text", out var text) &&
                (text.GetString()!.Length > 0 || !node.TryGetProperty("marks", out var marks) || marks.GetArrayLength() == 0);
        if (type == "image") return node.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("assetId", out var id) &&
            id.ValueKind == JsonValueKind.String && Guid.TryParseExact(id.GetString(), "D", out _);
        if (type == "hardBreak") return true;
        if (type == "heading" && (!node.TryGetProperty("attrs", out var headingAttrs) || !headingAttrs.TryGetProperty("level", out _))) return false;
        if (!node.TryGetProperty("content", out var children)) return type == "paragraph" || type == "heading";
        if (type is "bulletList" or "orderedList" or "listItem" or "blockquote" && children.GetArrayLength() == 0) return false;
        if (type == "listItem" && (!children[0].TryGetProperty("type", out var first) || first.GetString() != "paragraph")) return false;
        foreach (var child in children.EnumerateArray())
            if (!Validate(child, type, depth + 1, ref remaining)) return false;
        return true;
    }
    private static bool CanContain(string parent, string child) => parent switch
    {
        "paragraph" or "heading" => child is "text" or "hardBreak",
        "bulletList" or "orderedList" => child == "listItem",
        "doc" or "listItem" or "blockquote" => child is "paragraph" or "heading" or "bulletList" or "orderedList" or "image" or "blockquote",
        _ => false
    };
    private static bool AttributesValid(string type, JsonElement attrs)
    {
        if (attrs.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>();
        foreach (var field in attrs.EnumerateObject())
        {
            if (!seen.Add(field.Name)) return false;
            if (field.Name == "dir" && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "auto" or "rtl" or "ltr") continue;
            if (type == "heading" && field.Name == "level" && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var level) && level is >= 1 and <= 6) continue;
            if (type == "orderedList" && field.Name == "start" && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var start) && start is >= 1 and <= 10_000) continue;
            if (type == "orderedList" && field.Name == "type" && (field.Value.ValueKind == JsonValueKind.Null ||
                field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "1" or "a" or "A" or "i" or "I")) continue;
            if (type == "image" && field.Name == "assetId" && field.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(field.Value.GetString(), "D", out _)) continue;
            return false;
        }
        return true;
    }
    private static bool MarksValid(JsonElement marks)
    {
        if (marks.ValueKind != JsonValueKind.Array) return false;
        var seen = new HashSet<string>();
        foreach (var mark in marks.EnumerateArray())
        {
            if (mark.ValueKind != JsonValueKind.Object || mark.EnumerateObject().Count() != 1 ||
                !mark.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() is not ("bold" or "italic" or "underline" or "strike") || !seen.Add(type.GetString()!)) return false;
        }
        return true;
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
