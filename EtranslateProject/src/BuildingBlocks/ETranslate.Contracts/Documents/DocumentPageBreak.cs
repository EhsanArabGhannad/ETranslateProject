using System.Text.Json;

namespace ETranslate.Contracts.Documents;

public static class DocumentPageBreak
{
    public const int MaximumStarts = 49;
    public static bool IsStart(JsonElement node) => node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object &&
        attrs.TryGetProperty("pageBreakBefore", out var value) && value.ValueKind == JsonValueKind.True;
    public static int Count(JsonElement root) => root.TryGetProperty("content", out var children) && children.ValueKind == JsonValueKind.Array
        ? children.EnumerateArray().Count(IsStart) : 0;
}
