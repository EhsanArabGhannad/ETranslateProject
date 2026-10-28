using System.Text.Json;
using ETranslate.Contracts.Documents;

namespace ETranslate.Documents.Api.Documents;

public static class DocumentImageReferences
{
    public static IReadOnlyCollection<Guid> Parse(string content)
    {
        using var json = JsonDocument.Parse(content);
        var ids = new HashSet<Guid>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "image" && node.TryGetProperty("attrs", out var attrs) &&
                DocumentBodyImage.IsDocumentImage(attrs) && attrs.TryGetProperty("assetId", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var guid)) ids.Add(guid);
            if (node.TryGetProperty("content", out var children) && children.ValueKind == JsonValueKind.Array) foreach (var child in children.EnumerateArray()) Visit(child);
        }
        Visit(json.RootElement); return ids;
    }
}
