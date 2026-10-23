using System.Diagnostics;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class FreePaginationLabFactAttribute : FactAttribute
{
    public FreePaginationLabFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_FREE_PAGINATION_TESTS") != "1")
            Skip = "Run Verify-FreePagination.ps1; opt-in synthetic editor experiment only.";
    }
}

public sealed class FreePaginationLabTests
{
    [FreePaginationLabFact(Timeout = 120_000)]
    public async Task Free_engine_editing_and_layout_findings_are_measured_without_changing_production_documents()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_FREE_PAGINATION_ROOT")!);
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_FREE_PAGINATION_OUTPUT")!);
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ETRANSLATE_FREE_PAGINATION_NODE")!)
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("serve.mjs"); start.ArgumentList.Add("--port"); start.ArgumentList.Add("0");
        using var server = Process.Start(start)!;
        try
        {
            using var ready = JsonDocument.Parse((await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
            var origin = new Uri(ready.RootElement.GetProperty("url").GetString()!);
            Assert.True(origin.IsLoopback && origin.Scheme == "http");
            using var client = new HttpClient();
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(new Uri(origin, "/package-lock.json"))).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(new Uri(origin, "/../../appsettings.json"))).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await client.PostAsync(origin, new StringContent("never saved"))).StatusCode);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1440, Height = 1100 } });
            var external = new List<string>(); var errors = new List<string>();
            await context.RouteAsync("**/*", async route =>
            {
                if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
                { external.Add(route.Request.Url); await route.AbortAsync(); }
                else await route.ContinueAsync();
            });
            var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error);
            page.SetDefaultTimeout(10_000);
            await page.GotoAsync(origin.ToString());
            await page.WaitForFunctionAsync("() => !!window.freePaginationLab");
            var baseline = await Stable(page);
            var baselineJson = await Json(page);
            Assert.True(baseline.GetProperty("sourceTextPreserved").GetBoolean());
            Assert.True(baseline.GetProperty("productionCompatible").GetBoolean());
            Assert.True(baseline.GetProperty("pages").GetInt32() > 1);
            Assert.Equal(48, baseline.GetProperty("markers").EnumerateObject().Count(p => p.Name.StartsWith("MARK_", StringComparison.Ordinal)));
            Assert.True(DocumentContentJson.TryGetPlainText(baselineJson, out var plain));
            Assert.Contains("فارسی", plain); Assert.Contains("İstanbul", plain);
            await Shot(page, output, "text-page1");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sayfa 2", Exact = true }).ClickAsync();
            Assert.Equal(baselineJson, await Json(page));
            await Shot(page, output, "text-page2");

            // Actual keyboard insertion into the same editable document, followed by undo/redo.
            await page.Locator(".ProseMirror > p").First.ClickAsync();
            await page.Keyboard.PressAsync("Home");
            await page.Keyboard.InsertTextAsync("KEYBOARD_EDIT آزمون ");
            await page.WaitForFunctionAsync("() => window.freePaginationLab.flat().includes('KEYBOARD_EDIT')");
            Assert.Contains("KEYBOARD_EDIT", await Json(page));
            await page.Keyboard.PressAsync("Control+z");
            Assert.Equal(baselineJson, await Json(page));
            await page.Keyboard.PressAsync("Control+Shift+z");
            Assert.Contains("KEYBOARD_EDIT", await Json(page));
            await page.EvaluateAsync("() => window.freePaginationLab.undo()");
            Assert.Equal(baselineJson, await Json(page));
            await page.Locator(".ProseMirror > p").Nth(21).ClickAsync();
            await page.Keyboard.InsertTextAsync("PAGE2_EDIT آزمون صفحه دوم ");
            await page.WaitForFunctionAsync("() => window.freePaginationLab.flat().includes('PAGE2_EDIT')");
            await page.Keyboard.PressAsync("Control+z");
            Assert.Equal(baselineJson, await Json(page));
            await page.EvaluateAsync("() => window.freePaginationLab.roundTrip()");
            await Stable(page); Assert.Equal(baselineJson, await Json(page));

            await page.EvaluateAsync("() => window.freePaginationLab.prepend(Array.from({length:40},(_,i) => ({type:'paragraph',attrs:{dir:'ltr'},content:[{type:'text',text:'INSERTED_'+i+' - Additional synthetic paragraph. '.repeat(4)}]})))");
            var grown = await Stable(page);
            Assert.True(grown.GetProperty("pages").GetInt32() > baseline.GetProperty("pages").GetInt32());
            await page.EvaluateAsync("() => window.freePaginationLab.undo()");
            var shrunk = await Stable(page); Assert.Equal(baselineJson, await Json(page));

            var zooms = new Dictionary<string, JsonElement>();
            var selectionBeforeZoom = await page.EvaluateAsync<string>("() => JSON.stringify(window.freePaginationLab.selection())");
            foreach (var zoom in new[] { "0.75", "1.25", "1" })
            {
                await page.Locator("#zoom").SelectOptionAsync(zoom); zooms[zoom] = await Stable(page);
                Assert.Equal(baselineJson, await Json(page));
                Assert.Equal(selectionBeforeZoom, await page.EvaluateAsync<string>("() => JSON.stringify(window.freePaginationLab.selection())"));
            }
            await page.SetViewportSizeAsync(390, 844);
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth+1"));
            await Shot(page, output, "mobile");
            await page.SetViewportSizeAsync(1440, 1100);

            var samples = new Dictionary<string, JsonElement>();
            var sampleJson = new Dictionary<string, string>();
            foreach (var id in new[] { "simple-table", "long-table", "merged", "tall-cell", "long-paragraph", "word" })
            {
                await page.EvaluateAsync("id => window.freePaginationLab.load(id)", id);
                samples[id] = await Stable(page); sampleJson[id] = await Json(page);
                if (id != "word") Assert.True(samples[id].GetProperty("sourceTextPreserved").GetBoolean());
                if (samples[id].GetProperty("layoutIssue").ValueKind != JsonValueKind.Null)
                    Assert.True(samples[id].GetProperty("maximumObservedPages").GetInt32() > 50);
                await Shot(page, output, id);
                Assert.Equal(samples[id].GetProperty("productionCompatible").GetBoolean(), DocumentContentJson.TryGetPlainText(sampleJson[id], out _));
            }
            Assert.Equal(46, samples["long-table"].GetProperty("markers").EnumerateObject().Count(p => p.Name.StartsWith("MARK_", StringComparison.Ordinal)));
            Assert.Equal(6, samples["word"].GetProperty("styleGaps").GetArrayLength());
            Assert.False(samples["merged"].GetProperty("productionCompatible").GetBoolean());
            Assert.Equal("page-count-runaway", samples["tall-cell"].GetProperty("layoutIssue").GetString());
            Assert.Equal(0, samples["tall-cell"].GetProperty("pages").GetInt32());
            Assert.Empty(errors); Assert.Empty(external);

            // Synthetic text-only reference from the unchanged production PDF renderer.
            using var renderer = new ChromiumDraftPdfRenderer();
            var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, baselineJson, null, null,
                DocumentPageLayout.Default.ToJson(), null, new Dictionary<Guid, PdfImage>()), CancellationToken.None);
            int pdfPages;
            using (var stream = new MemoryStream(bytes)) using (var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import)) pdfPages = pdf.PageCount;
            await File.WriteAllBytesAsync(Path.Combine(output, "text-reference.pdf"), bytes);
            var tableOutcomes = new Dictionary<string, string>();
            foreach (var id in new[] { "simple-table", "long-table", "merged", "tall-cell" })
            {
                try
                {
                    await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, sampleJson[id], null, null,
                        DocumentPageLayout.Default.ToJson(), null, new Dictionary<Guid, PdfImage>()), CancellationToken.None);
                    tableOutcomes[id] = "accepted";
                }
                catch (PdfRenderValidationException) { tableOutcomes[id] = "rejected-by-production-renderer"; }
            }
            var report = new { RunUtc = DateTimeOffset.UtcNow, LocalOnly = true, ProductionIntegrationApproved = false, RealWordClipboardTested = false, RealDocxTested = false,
                Text = baseline, Grown = grown, AfterUndo = shrunk, Zoom = zooms, Samples = samples, TableRenderer = tableOutcomes,
                ProductionPdfPages = pdfPages, KeyboardEditUndoRedoPassed = true, InMemoryRoundTripPassed = true,
                ExternalRequests = external.Count, BoundaryParity = "Run compare_pdf.py; equal counts alone are insufficient." };
            await File.WriteAllTextAsync(Path.Combine(output, "browser-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); } }
    }

    private static async Task<string> Json(IPage page) => await page.EvaluateAsync<string>("() => JSON.stringify(window.freePaginationLab.json())");
    private static async Task Shot(IPage page, string output, string name) => await page.ScreenshotAsync(new() { Path = Path.Combine(output, name + ".png"), FullPage = true });
    private static async Task<JsonElement> Stable(IPage page)
    {
        // Poll for a stable layout, do not replace a failed engine with a silent timeout/pass.
        try { await page.WaitForFunctionAsync("() => { const r=window.freePaginationLab.report(); return ((r.pages>0 && r.pages<=50) || r.layoutIssue!==null) && r.millisecondsSinceChange>350; }", null, new() { Timeout = 10_000 }); }
        catch (TimeoutException error) { throw new InvalidOperationException("Unstable engine layout: " + await page.EvaluateAsync<string>("() => JSON.stringify(window.freePaginationLab.report())"), error); }
        return await page.EvaluateAsync<JsonElement>("() => window.freePaginationLab.report()");
    }
}

