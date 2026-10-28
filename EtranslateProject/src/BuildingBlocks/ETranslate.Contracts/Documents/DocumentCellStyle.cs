using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ETranslate.Contracts.Documents;

public static partial class DocumentCellStyle
{
    public static readonly string[] Fields = ["backgroundColor", "borderColor", "borderWidthPt", "borderStyle"];
    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)] private static partial Regex Color();
    public static bool ValidValue(string field, JsonElement value)
    {
        if (!Fields.Contains(field)) return false;
        if (value.ValueKind == JsonValueKind.Null) return true;
        if (field == "borderWidthPt") return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && new[] { 0m, .5m, 1m, 1.5m, 2m, 3m }.Contains(number);
        if (field == "borderStyle") return value.ValueKind == JsonValueKind.String && value.GetString() is "solid" or "dashed" or "dotted";
        return value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 7 && Color().IsMatch(value.GetString()!);
    }
    public static string Css(JsonElement attrs)
    {
        if (attrs.ValueKind != JsonValueKind.Object) return "";
        var css = "";
        foreach (var field in Fields)
        {
            if (!attrs.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null || !ValidValue(field, value)) continue;
            css += field switch {
                "backgroundColor" => $"background-color:{value.GetString()};",
                "borderColor" => $"border-color:{value.GetString()};",
                "borderWidthPt" => $"border-width:{value.GetDecimal().ToString(CultureInfo.InvariantCulture)}pt;",
                "borderStyle" => $"border-style:{value.GetString()};", _ => ""
            };
        }
        if (attrs.TryGetProperty("borderWidthPt", out var width) && width.ValueKind == JsonValueKind.Number && width.TryGetDecimal(out var w) && w == 0) css += "border-style:hidden;";
        return css;
    }
}
