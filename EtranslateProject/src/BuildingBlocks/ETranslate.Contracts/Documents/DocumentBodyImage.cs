using System.Globalization;
using System.Text.Json;

namespace ETranslate.Contracts.Documents;

public static class DocumentBodyImage
{
    public static bool ValidValue(string field, JsonElement value) => field switch
    {
        "assetScope" => value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "document",
        "widthPercent" => value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var width) && width is >= 5 and <= 100 && decimal.Truncate(width) == width,
        "align" => value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() is "left" or "center" or "right",
        _ => false
    };

    public static bool IsDocumentImage(JsonElement attrs) => attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("assetScope", out var scope) && scope.ValueKind == JsonValueKind.String && scope.GetString() == "document";

    // Existing template images keep their earlier dimensions until explicitly resized.
    public static string Css(JsonElement attrs)
    {
        var sized = attrs.TryGetProperty("widthPercent", out var width) && width.ValueKind == JsonValueKind.Number;
        var css = sized || IsDocumentImage(attrs) ? $"display:block;width:{(sized ? width.GetDecimal() : 50).ToString(CultureInfo.InvariantCulture)}%;max-width:100%;max-height:none;height:auto;" : "";
        if (attrs.TryGetProperty("align", out var align) && align.ValueKind == JsonValueKind.String)
            css += align.GetString() switch { "left" => "margin-left:0;margin-right:auto;", "center" => "margin-left:auto;margin-right:auto;", "right" => "margin-left:auto;margin-right:0;", _ => "" };
        return css;
    }
}
