using System.Text.Json;
using System.Text.Json.Nodes;

namespace ETranslate.Web.Models;

// A deliberately small editing format. Never flatten rich/unknown documents silently.
public static class DocumentText
{
    public const string Empty = "{\"type\":\"doc\",\"content\":[]}";
    public static string FromPlainText(string text) => JsonSerializer.Serialize(new
    {
        type = "doc",
        content = text.Replace("\r\n", "\n").Split('\n').Select(line => new
        {
            type = "paragraph", content = new[] { new { type = "text", text = line } }
        })
    });
    public static string? TryPlainText(string json)
    {
        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null || node["type"]?.GetValue<string>() != "doc" ||
                node.Any(pair => pair.Key is not ("type" or "content")) || node["content"] is not JsonArray blocks) return null;
            var lines = new List<string>();
            foreach (var block in blocks)
            {
                if (block is not JsonObject paragraph || paragraph["type"]?.GetValue<string>() != "paragraph" ||
                    paragraph.Any(pair => pair.Key is not ("type" or "content"))) return null;
                var line = "";
                if (paragraph["content"] is JsonArray children)
                    foreach (var child in children)
                    {
                        if (child is not JsonObject item || item["type"]?.GetValue<string>() != "text" ||
                            item.Any(pair => pair.Key is not ("type" or "text")) || item["text"] is not JsonValue value ||
                            !value.TryGetValue<string>(out var text)) return null;
                        line += text;
                    }
                else if (paragraph.ContainsKey("content")) return null;
                lines.Add(line);
            }
            return string.Join('\n', lines);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }
}
