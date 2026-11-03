using System.Text.Json;

namespace ETranslate.DomainTests.Documents;

internal static class EditorPerformanceSamples
{
    internal static readonly string[] Names = ["long-text", "wide-table", "mixed-images", "fifty-pages"];
    internal const string Header = """{"type":"header","content":[{"type":"paragraph","content":[{"type":"text","text":"PERFORMANCE QA LETTERHEAD"}]}]}""";
    internal const string Footer = """{"type":"footer","content":[{"type":"paragraph","content":[{"type":"text","text":"PERFORMANCE QA LAST FOOTER"}]}]}""";
    internal static object Paragraph(string text, bool page = false, string dir = "ltr") => new {
        type = "paragraph", attrs = new { dir, pageBreakBefore = page }, content = new[] { new { type = "text", text } }
    };
    private static object Table(int index, int rows, int columns) => new {
        type = "table", content = Enumerable.Range(0, rows).Select(row => new {
            type = "tableRow", content = Enumerable.Range(0, columns).Select(column => new {
                type = row == 0 ? "tableHeader" : "tableCell", content = new[] {
                    Paragraph($"PERF_CELL_{index:00}_{row:00}_{column:00} İstanbul ترجمه")
                }
            }).ToArray()
        }).ToArray()
    };
    internal static string Body(string name, Guid image)
    {
        List<object> content = [];
        if (name == "fifty-pages" || name == "over-pages") {
            for (var i = 0; i < 50; i++) content.Add(Paragraph($"PERF_PAGE_{i:000} İstanbul · ترجمه با نیم‌فاصله", i > 0, i % 2 == 0 ? "ltr" : "rtl"));
            if (name == "over-pages") content.Add(Paragraph(string.Concat(Enumerable.Repeat("Long final section İstanbul ترجمه. ", 300))));
        } else {
            content.Add(Paragraph("PERF_START İstanbul · ترجمه با نیم‌فاصله"));
            if (name == "long-text") {
                for (var i = 0; i < 170; i++) content.Add(Paragraph($"PERF_TEXT_{i:000} " + string.Concat(Enumerable.Repeat("İstanbul çeviri با نیم‌فاصله ve resmi belge. ", 11)), dir: i % 2 == 0 ? "ltr" : "rtl"));
            } else if (name == "wide-table") content.Add(Table(0, 50, 12));
            else if (name == "mixed-images") {
                for (var i = 0; i < 10; i++) {
                    for (var p = 0; p < 8; p++) content.Add(Paragraph($"PERF_MIX_{i:02}_{p:02} " + string.Concat(Enumerable.Repeat("Çeviri İstanbul · متن نمونهٔ ساختگی با نیم‌فاصله. ", 5)), dir: p % 2 == 0 ? "ltr" : "rtl"));
                    content.Add(new { type = "image", attrs = new { assetId = image, assetScope = "document", widthPercent = 30, align = "center" } });
                    if (i % 3 == 0) content.Add(Table(i, 15, 4));
                }
            } else throw new ArgumentException("Unknown performance fixture.");
        }
        content.Add(Paragraph("PERF_TAIL İstanbul · پایان سند"));
        return JsonSerializer.Serialize(new { type = "doc", content });
    }
}
