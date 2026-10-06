using System.Text.Json;

namespace ETranslate.Contracts.Documents;

public sealed record DocumentPageLayout(string PageSize, string Orientation,
    decimal Top, decimal Right, decimal Bottom, decimal Left)
{
    public static DocumentPageLayout Default { get; } = new("A4", "Portrait", 25, 20, 20, 20);
    public (decimal Width, decimal Height) DimensionsMm
    {
        get
        {
            var dimensions = PageSize switch
            {
                "A4" => (210m, 297m), "A5" => (148m, 210m),
                "Letter" => (215.9m, 279.4m), "Legal" => (215.9m, 355.6m),
                _ => throw new InvalidOperationException("Unsupported page size.")
            };
            return Orientation == "Landscape" ? (dimensions.Item2, dimensions.Item1) : dimensions;
        }
    }
    public bool IsValid => PageSize is "A4" or "A5" or "Letter" or "Legal" &&
        Orientation is "Portrait" or "Landscape" &&
        Top is >= 5 and <= 50 && Right is >= 5 and <= 50 && Bottom is >= 5 and <= 50 && Left is >= 5 and <= 50 &&
        DimensionsMm.Width - Left - Right >= 60 && DimensionsMm.Height - Top - Bottom >= 80;

    public string ToJson() => JsonSerializer.Serialize(new
    { pageSize = PageSize, orientation = Orientation, marginsMm = new { top = Top, right = Right, bottom = Bottom, left = Left } });

    public static bool TryParse(string? json, out DocumentPageLayout? layout)
    {
        layout = null;
        if (json is null) { layout = Default; return true; }
        if (json.Length > 10000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Fields(root, ["pageSize", "orientation", "marginsMm"]) ||
                root.GetProperty("pageSize").ValueKind != JsonValueKind.String ||
                root.GetProperty("orientation").ValueKind != JsonValueKind.String) return false;
            var margins = root.GetProperty("marginsMm");
            if (!Fields(margins, ["top", "right", "bottom", "left"])) return false;
            var value = new DocumentPageLayout(root.GetProperty("pageSize").GetString()!, root.GetProperty("orientation").GetString()!,
                margins.GetProperty("top").GetDecimal(), margins.GetProperty("right").GetDecimal(),
                margins.GetProperty("bottom").GetDecimal(), margins.GetProperty("left").GetDecimal());
            if (!value.IsValid) return false;
            layout = value; return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { return false; }
    }
    internal static bool Fields(JsonElement value, string[] names) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(field => field.Name).Order().SequenceEqual(names.Order());
}
