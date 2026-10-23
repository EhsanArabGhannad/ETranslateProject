using System.Diagnostics;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using Microsoft.Playwright;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ETranslate.DomainTests.Documents;

public sealed class CanvasEditorTrialFactAttribute : FactAttribute
{
    public CanvasEditorTrialFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_CANVAS_TRIAL_TESTS") != "1")
            Skip = "Run Verify-CanvasEditorTrial.ps1; local synthetic canvas research only.";
    }
}

public sealed class CanvasEditorTrialTests
{
    [CanvasEditorTrialFact(Timeout = 120_000)]
    public async Task Canvas_engine_text_input_pagination_and_export_paths_are_measured_in_isolation()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_CANVAS_TRIAL_ROOT")!);
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_CANVAS_TRIAL_OUTPUT")!);
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ETRANSLATE_CANVAS_TRIAL_NODE")!)
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
            Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await client.PostAsync(origin, new StringContent("not saved"))).StatusCode);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1440, Height = 1100 } });
            var external = new List<string>(); var errors = new List<string>();
            await context.RouteAsync("**/*", async route =>
            {
                if (Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
                { external.Add(route.Request.Url); await route.AbortAsync(); }
                else await route.ContinueAsync();
            });
            var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error); page.SetDefaultTimeout(10_000);
            await page.GotoAsync(origin.ToString());
            try { await page.WaitForFunctionAsync("() => !!window.canvasTrial"); }
            catch (TimeoutException error) { throw new InvalidOperationException(await page.Locator("#status").InnerTextAsync(), error); }
            var rtl = await Stable(page);
            Assert.True(rtl.GetProperty("sourceTextPreserved").GetBoolean());
            await Shot(page, output, "rtl-reference-and-canvas");
            var rtlCursors = await page.EvaluateAsync<JsonElement>("() => window.canvasTrial.rtlCursors()");
            await File.WriteAllTextAsync(Path.Combine(output, "rtl-observation.json"), JsonSerializer.Serialize(new { Rtl = rtl, Cursors = rtlCursors }, new JsonSerializerOptions { WriteIndented = true }));
            var rtlData = await Data(page);
            Assert.False(DocumentContentJson.TryGetPlainText(rtlData, out _));
            Assert.True(await page.EvaluateAsync<bool>("() => window.canvasTrial.focusKeyword('سلام')"));
            await page.Keyboard.InsertTextAsync("KEYBOARD_EDIT آزمون ");
            await page.WaitForFunctionAsync("() => window.canvasTrial.text().includes('KEYBOARD_EDIT')");
            await page.EvaluateAsync("() => window.canvasTrial.undo()");
            Assert.Equal(rtlData, await Data(page));
            await page.EvaluateAsync("() => window.canvasTrial.redo()");
            Assert.Contains("KEYBOARD_EDIT", await Data(page));
            await page.EvaluateAsync("() => window.canvasTrial.undo()");
            Assert.Equal(rtlData, await Data(page));

            await page.EvaluateAsync("() => window.canvasTrial.load('text')");
            var baseline = await Stable(page); var baselineData = await Data(page);
            Assert.True(baseline.GetProperty("sourceTextPreserved").GetBoolean());
            Assert.True(baseline.GetProperty("pages").GetInt32() > 1);
            var baselineText = await page.EvaluateAsync<string>("() => window.canvasTrial.text()");
            Assert.Contains("جابه‌جایی", baselineText);
            await page.GetByRole(AriaRole.Button, new() { Name = "Sayfa 2", Exact = true }).ClickAsync();
            Assert.Equal(baselineData, await Data(page)); await Shot(page, output, "text-page2");
            await page.EvaluateAsync("() => window.canvasTrial.roundTrip()");
            await Stable(page); Assert.Equal(baselineData, await Data(page));
            await page.EvaluateAsync("() => window.canvasTrial.prepend(Array.from({length:40},(_,i)=>({value:'ADDED_'+i+' Synthetic paragraph for flow testing. '.repeat(4)+'\\n'})))");
            var grown = await Stable(page);
            Assert.True(grown.GetProperty("pages").GetInt32() > baseline.GetProperty("pages").GetInt32());
            await page.EvaluateAsync("() => window.canvasTrial.undo()");
            var shrunk = await Stable(page); Assert.Equal(baselineData, await Data(page));
            var zoom = new Dictionary<string, JsonElement>();
            foreach (var scale in new[] { "0.75", "1.25", "1" })
            {
                await page.Locator("#zoom").SelectOptionAsync(scale); zoom[scale] = await Stable(page);
                Assert.Equal(baselineData, await Data(page));
            }
            await page.SetViewportSizeAsync(390, 844);
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth<=innerWidth+1"));
            await Shot(page, output, "mobile"); await page.SetViewportSizeAsync(1440, 1100);

            var markerPages = new Dictionary<string, int>();
            for (var i = 0; i < baseline.GetProperty("pages").GetInt32(); i++)
            {
                var pageText = await page.EvaluateAsync<string>("i => JSON.stringify(window.canvasTrial.pageData(i))", i);
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(pageText, @"MARK_\d{3}"))
                    Assert.True(markerPages.TryAdd(match.Value, i + 1), "Duplicate marker in per-page native data");
            }
            Assert.Equal(48, markerPages.Count);
            var html = await page.EvaluateAsync<JsonElement>("() => window.canvasTrial.html()");
            var images = await page.EvaluateAsync<string[]>("() => window.canvasTrial.images()");
            Assert.Equal(baseline.GetProperty("pages").GetInt32(), images.Length);
            using (var rasterPdf = new PdfDocument())
            {
                for (var i = 0; i < images.Length; i++)
                {
                    Assert.StartsWith("data:image/png;base64,", images[i]);
                    var bytes = Convert.FromBase64String(images[i].Split(',')[1]);
                    await File.WriteAllBytesAsync(Path.Combine(output, $"canvas-export-{i + 1}.png"), bytes);
                    using var stream = new MemoryStream(bytes); using var image = XImage.FromStream(stream);
                    var sheet = rasterPdf.AddPage(); sheet.Width = XUnit.FromMillimeter(210); sheet.Height = XUnit.FromMillimeter(297);
                    using var graphics = XGraphics.FromPdfPage(sheet); graphics.DrawImage(image, 0, 0, sheet.Width.Point, sheet.Height.Point);
                }
                rasterPdf.Save(Path.Combine(output, "image-path.pdf"));
            }
            var exportPage = await context.NewPageAsync(); await exportPage.GotoAsync(origin.ToString());
            await exportPage.SetContentAsync("<!doctype html><html><head><meta charset='utf-8'><style>" +
                "@font-face{font-family:LabNoto;src:url('/generated/fonts/NotoSans-Regular.ttf')}" +
                "@font-face{font-family:LabArabic;src:url('/generated/fonts/NotoSansArabic-Regular.ttf')}" +
                "@page{size:A4;margin:25mm 20mm 28mm}body{margin:0;font:16px LabNoto,LabArabic,sans-serif}" +
                "</style></head><body>" + html.GetProperty("main").GetString() + "</body></html>");
            await exportPage.EvaluateAsync("() => document.fonts.ready");
            await exportPage.PdfAsync(new() { Path = Path.Combine(output, "html-path.pdf"), PreferCSSPageSize = true, PrintBackground = true });
            await exportPage.CloseAsync();

            var samples = new Dictionary<string, JsonElement>();
            var nativeSamples = new Dictionary<string, JsonElement>();
            var paintedEnds = new Dictionary<string, bool>();
            foreach (var id in new[] { "long-table", "merged", "tall-cell", "long-paragraph", "html" })
            {
                await page.EvaluateAsync("id => window.canvasTrial.load(id)", id); samples[id] = await Stable(page);
                nativeSamples[id] = await page.EvaluateAsync<JsonElement>("() => window.canvasTrial.data()");
                Assert.True(samples[id].GetProperty("sourceTextPreserved").GetBoolean());
                await Shot(page, output, id);
                if (id is "long-table" or "tall-cell" or "long-paragraph" or "merged")
                {
                    var beforeExport = await Data(page);
                    var rendered = await page.EvaluateAsync<string[]>("() => window.canvasTrial.images()");
                    var painted = await page.EvaluateAsync<string>("() => window.canvasTrial.paintedText()");
                    var end = id switch { "long-table" => "TABLE_END_MARKER", "tall-cell" => "AFTER_TALL", "long-paragraph" => "LONG_END", _ => "MERGED_END_MARKER" };
                    paintedEnds[id] = painted.Contains(end, StringComparison.Ordinal);
                    Assert.True(paintedEnds[id], "End marker exists in data but was not painted: " + id);
                    if (id == "long-table")
                        Assert.Equal(46, System.Text.RegularExpressions.Regex.Matches(painted, @"ROW_\d{3}").Select(m => m.Value).Distinct().Count());
                    for (var i = 0; i < rendered.Length; i++)
                        await File.WriteAllBytesAsync(Path.Combine(output, $"{id}-export-{i + 1}.png"), Convert.FromBase64String(rendered[i].Split(',')[1]));
                    Assert.Equal(beforeExport, await Data(page));
                }
            }
            Assert.Empty(errors); Assert.Empty(external);
            var report = new { RunUtc = DateTimeOffset.UtcNow, LocalOnly = true, ProductionIntegrationApproved = false,
                RealWordClipboardTested = false, RealDocxTested = false, PdfBranchInstalled = false,
                Rtl = rtl, RtlCursorPositions = rtlCursors, Text = baseline, NativeMarkerPages = markerPages,
                Grown = grown, AfterUndo = shrunk, Zoom = zoom, Samples = samples, NativeSamples = nativeSamples, PaintedEndMarkers = paintedEnds,
                KeyboardEditUndoRedoPassed = true, InMemoryRoundTripPassed = true, ExternalRequests = external.Count,
                ExportScope = "Raster PDF assembled from published getImage(print) pages; separate HTML export rendered by Chromium. Neither is an integration with production rendering." };
            await File.WriteAllTextAsync(Path.Combine(output, "browser-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); } }
    }
    private static async Task<string> Data(IPage page) => await page.EvaluateAsync<string>("() => JSON.stringify(window.canvasTrial.data())");
    private static async Task Shot(IPage page, string folder, string name) => await page.ScreenshotAsync(new() { Path = Path.Combine(folder, name + ".png"), FullPage = true });
    private static async Task<JsonElement> Stable(IPage page)
    {
        await page.WaitForFunctionAsync("() => {const r=window.canvasTrial.report();return r.pages>0&&r.pages<=50&&r.millisecondsSinceChange>300;}", null, new() { Timeout = 10_000 });
        return await page.EvaluateAsync<JsonElement>("() => window.canvasTrial.report()");
    }
}
