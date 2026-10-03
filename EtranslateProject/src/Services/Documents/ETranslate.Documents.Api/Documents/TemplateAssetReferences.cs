using System.Text.Json;

namespace ETranslate.Documents.Api.Documents;

public static class TemplateAssetReferences
{
    // The editor uses assetId for managed images. Rendering must resolve these IDs
    // through authorized storage, never fetch arbitrary URLs from editor JSON.
    public static IReadOnlyCollection<Guid> Parse(params string?[] contents)
    {
        var ids = new HashSet<Guid>();
        foreach (var content in contents)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            try
            {
                using var document = JsonDocument.Parse(content);
                Visit(document.RootElement, ids);
            }
            catch (JsonException)
            {
                throw Invalid("Content must be valid JSON.");
            }
        }
        return ids;
    }

    private static void Visit(JsonElement element, HashSet<Guid> ids)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) Visit(child, ids);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("assetId", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.String ||
                        !property.Value.TryGetGuid(out var id) || id == Guid.Empty)
                        throw Invalid("assetId must be a non-empty GUID.");
                    ids.Add(id);
                }
                else Visit(property.Value, ids);
            }
        }
    }

    private static DocumentTemplateValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["assetId"] = [message] });
}
