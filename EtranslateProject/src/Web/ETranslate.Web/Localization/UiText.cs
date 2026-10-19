global using ETranslate.Web.Localization;
using System.Globalization;
using System.Text.Json;

namespace ETranslate.Web.Localization;

/// <summary>Turkish product copy shared by Razor/BFF and bundled clients. No document content is localized here.</summary>
public static class UiText
{
    private static readonly IReadOnlyDictionary<string, string> Text = Load();
    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("ETranslate.Web.Resources.interface.tr.json")
            ?? throw new InvalidOperationException("Turkish interface resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static string Get(string key) => Text.TryGetValue(key, out var value) ? value
        : throw new KeyNotFoundException($"Unknown interface key: {key}");
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);
    public static string MoneyTry(decimal amount) => amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " TRY";
}
