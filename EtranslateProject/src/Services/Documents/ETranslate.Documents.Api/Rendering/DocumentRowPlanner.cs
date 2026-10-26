using System.Text.Json;
using Microsoft.Playwright;

namespace ETranslate.Documents.Api.Rendering;

public static class DocumentRowPlanner
{
    private static readonly Lazy<string> Script = new(() => {
        using var stream = typeof(DocumentRowPlanner).Assembly.GetManifestResourceStream("ETranslate.Documents.RowPlan")
            ?? throw new InvalidOperationException("The packaged document row planner is missing.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    });
    public static async Task<int> ApplyAsync(IPage page, double width, double height, double columnGap, CancellationToken token)
    {
        // Application code only: never a script or coordinates supplied by a client/document.
        var result = await page.EvaluateAsync<JsonElement>("geometry => { " + Script.Value + """
            ; try {const plan=NativeTableRows.prepareRowPlan(document.querySelector('main'),geometry);return {ok:true,pages:plan.pages};}
            catch {return {ok:false};}
            }
            """, new { contentWidth = width, contentHeight = height, columnGap }).WaitAsync(token);
        if (!result.GetProperty("ok").GetBoolean()) throw new PdfRenderValidationException("Table pagination is unsupported for this layout; content was not changed.");
        return result.GetProperty("pages").GetInt32();
    }
}
