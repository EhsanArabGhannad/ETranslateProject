using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ETranslate.Contracts.Documents;

// Rich v2 accepts bounded application values, never arbitrary CSS or remote font URLs.
public static partial class DocumentTypography
{
    public static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[]{"fontFamily", "fontSize", "color", "highlight"});
    public static bool ValidValue(string field, JsonElement value) => Fields.Contains(field) && (value.ValueKind == JsonValueKind.Null || field switch
    {
        "fontFamily" => value.ValueKind == JsonValueKind.String && value.GetString() is "sans" or "serif" or "arabic",
        "fontSize" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var size) && size is >= 6 and <= 72,
        "color" or "highlight" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length==7 && HexColor().IsMatch(value.GetString()!),
        _ => false
    });
    public static bool ValidAttributes(JsonElement attrs)
    {
        if (attrs.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(); var nonempty = false;
        foreach (var field in attrs.EnumerateObject())
        {
            if (!seen.Add(field.Name) || !ValidValue(field.Name, field.Value)) return false;
            nonempty |= field.Value.ValueKind != JsonValueKind.Null;
        }
        return nonempty;
    }
    public static string FontFamily(string token) => token switch
    {
        "sans" => "'Noto Sans','Noto Sans Arabic',sans-serif",
        "serif" => "'Noto Serif','Noto Sans Arabic',serif",
        "arabic" => "'Noto Sans Arabic','Noto Sans',sans-serif",
        _ => throw new ArgumentException("Unsupported font family.")
    };
    public static string Css(JsonElement attrs)
    {
        if (!ValidAttributes(attrs)) throw new ArgumentException("Unsupported typography attributes.");
        List<string> rules = [];
        foreach (var name in Fields)
        {
            if (!attrs.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            rules.Add(name switch {
                "fontFamily" => "font-family:"+FontFamily(value.GetString()!),
                "fontSize" => "font-size:"+value.GetInt32().ToString(CultureInfo.InvariantCulture)+"pt",
                "color" => "color:"+value.GetString(), _ => "background-color:"+value.GetString()
            });
        }
        return string.Join(';',rules);
    }
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)] private static partial Regex HexColor();
}
