using System.Diagnostics;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class NativePaginationFlowFactAttribute : FactAttribute
{
    public NativePaginationFlowFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_TESTS") != "1")
            Skip = "Run Verify-NativePaginationFlow.ps1; isolated synthetic flow/PDF comparison only.";
    }
}

public sealed partial class NativePaginationFlowTests
{
    [NativePaginationFlowFact(Timeout = 180_000)]
    public async Task Native_fragmentation_is_measured_against_the_existing_renderer_without_product_or_database_writes()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_ROOT")!);
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_OUTPUT")!);
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_NODE")!)
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("serve.mjs"); start.ArgumentList.Add("--port"); start.ArgumentList.Add("0");
        using var server = Process.Start(start)!;
        var stderr = server.StandardError.ReadToEndAsync();
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
            await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block,
                ViewportSize = new() { Width = 1440, Height = 1100 } });
            var external = new List<string>(); var errors = new List<string>();
            await context.RouteAsync("**/*", async route => {
                if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
                { external.Add(route.Request.Url); await route.AbortAsync(); }
                else await route.ContinueAsync();
            });
            var page = await context.NewPageAsync();
            page.PageError += (_, error) => errors.Add(error);
            page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
            page.SetDefaultTimeout(10000);
            await page.GotoAsync(origin.ToString());
            await page.WaitForFunctionAsync("() => !!window.nativeFlowLab && window.nativeFlowLab.ready()");
            var baseline = await Stable(page); var original = await Json(page);
            await File.WriteAllTextAsync(Path.Combine(output, "baseline-debug.json"), baseline.GetRawText());
            Assert.True(baseline.GetProperty("sourceTextPreserved").GetBoolean());
            Assert.True(baseline.GetProperty("jsonUnchanged").GetBoolean());
            Assert.True(baseline.GetProperty("pages").GetInt32() > 1);
            Assert.Equal(48, baseline.GetProperty("markers").EnumerateObject().Count());
            Assert.True(DocumentContentJson.TryGetPlainText(original, out var plain));
            Assert.Contains("فارسی", plain); Assert.Contains("İstanbul", plain);
            await Shot(page, output, "text-page1");
            await page.Locator("#page").SelectOptionAsync("2");
            Assert.Equal(original, await Json(page));
            await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("Sayfa 2 /");
            await Shot(page, output, "text-page2");

            // Locate a paragraph whose own marker is in page two, not an assumed paragraph index.
            var pageTwo = baseline.GetProperty("markers").EnumerateObject().First(p => p.Value.GetInt32() == 2).Name;
            var paragraph = page.Locator("#editor p").Filter(new() { HasText = pageTwo }).First;
            await paragraph.ClickAsync(); await page.Keyboard.PressAsync("End");
            await page.Keyboard.InsertTextAsync(" PAGE_TWO_EDIT آزمون");
            await Stable(page); Assert.Contains("PAGE_TWO_EDIT", await Json(page));
            await page.Keyboard.PressAsync("Control+z"); await Stable(page);
            Assert.Equal(original, await Json(page));
            await page.Keyboard.PressAsync("Control+Shift+z"); await Stable(page);
            Assert.Contains("PAGE_TWO_EDIT", await Json(page));
            await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); await Stable(page);
            Assert.Equal(original, await Json(page));

            var rtlMarker = baseline.GetProperty("markers").EnumerateObject().First(p => p.Value.GetInt32() == 2 && int.Parse(p.Name[5..]) % 2 == 0).Name;
            await page.Locator("#editor p").Filter(new() { HasText = rtlMarker }).First.ClickAsync();
            await page.Keyboard.PressAsync("Home"); await page.Keyboard.InsertTextAsync("آزمون‌مکان‌نما ");
            await Stable(page);
            using (var changed = JsonDocument.Parse(await Json(page))) {
                var rtlParagraph = changed.RootElement.GetProperty("content").EnumerateArray().Single(p => p.TryGetProperty("content", out var children) &&
                    children.EnumerateArray().Any(c => c.TryGetProperty("text", out var text) && text.GetString()!.Contains(rtlMarker)));
                Assert.Contains("آزمون‌مکان‌نما", string.Concat(rtlParagraph.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString())));
                Assert.Equal("rtl", rtlParagraph.GetProperty("attrs").GetProperty("dir").GetString());
            }
            await page.Keyboard.PressAsync("Control+z"); await Stable(page); Assert.Equal(original, await Json(page));

            // Observe native vertical-arrow traversal at a real page boundary, not a page-button jump.
            var lastOnFirst = baseline.GetProperty("markers").EnumerateObject().Last(p => p.Value.GetInt32() == 1).Name;
            var firstOnSecond = baseline.GetProperty("markers").EnumerateObject().First(p => p.Value.GetInt32() == 2).Name;
            var arrowDown = new List<JsonElement>(); var arrowUp = new List<JsonElement>();
            for (var repeat = 0; repeat < 3; repeat++) {
                await PlaceCaret(page, lastOnFirst, start: false); await Stable(page); await page.Keyboard.PressAsync("ArrowDown");
                arrowDown.Add(await Stable(page));
                await PlaceCaret(page, firstOnSecond, start: true); await Stable(page); await page.Keyboard.PressAsync("ArrowUp");
                arrowUp.Add(await Stable(page));
            }
            var arrowForward = arrowDown.All(r => r.GetProperty("selection").GetProperty("page").ValueKind == JsonValueKind.Number && r.GetProperty("selection").GetProperty("page").GetInt32() == 2);
            var arrowBackward = arrowUp.All(r => r.GetProperty("selection").GetProperty("page").ValueKind == JsonValueKind.Number && r.GetProperty("selection").GetProperty("page").GetInt32() == 1);
            var arrowUiSynchronized = arrowDown.All(r => r.GetProperty("activePage").GetInt32() == 2) && arrowUp.All(r => r.GetProperty("activePage").GetInt32() == 1);
            Assert.Equal(original, await Json(page));

            // One real keyboard insertion flows forward; undo must pull it back without rewriting JSON.
            await page.EvaluateAsync("() => window.nativeFlowLab.jump(1)");
            await page.Locator("#editor p").First.ClickAsync(); await page.Keyboard.PressAsync("Control+Home");
            await page.Keyboard.InsertTextAsync(string.Concat(Enumerable.Repeat("SYNTHETIC_GROWTH İstanbul آزمایش جریان متن. ", 160)));
            var grown = await Stable(page);
            Assert.True(grown.GetProperty("pages").GetInt32() > baseline.GetProperty("pages").GetInt32());
            await page.Keyboard.PressAsync("Control+z"); var shrunk = await Stable(page);
            Assert.Equal(original, await Json(page)); Assert.Equal(baseline.GetProperty("pages").GetInt32(), shrunk.GetProperty("pages").GetInt32());
            var selection = await page.EvaluateAsync<string>("() => JSON.stringify(window.nativeFlowLab.selection())");
            foreach (var zoom in new[] { "75", "100", "125", "fit" })
            {
                await page.Locator("#zoom").SelectOptionAsync(zoom); var zoomed = await Stable(page);
                Assert.Equal(original, await Json(page));
                Assert.Equal(selection, await page.EvaluateAsync<string>("() => JSON.stringify(window.nativeFlowLab.selection())"));
                Assert.Equal(baseline.GetProperty("pages").GetInt32(), zoomed.GetProperty("pages").GetInt32());
            }
            await page.SetViewportSizeAsync(390, 844); await Stable(page);
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
            await Shot(page, output, "mobile");
            await page.SetViewportSizeAsync(1440, 1100); await Stable(page);
            await page.Locator("#readonly").ClickAsync(); await Stable(page);
            await Assertions.Expect(page.Locator("#editor [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false");
            await page.Locator("#page").SelectOptionAsync("2"); Assert.Equal(original, await Json(page));
            await page.Locator("#readonly").ClickAsync(); await Stable(page);

            var tableEditing = await VerifyTableEditing(page, output);

            // PDF files below are new synthetic QA artifacts from the unchanged production renderer.
            var samples = new Dictionary<string, object>();
            using var renderer = new ChromiumDraftPdfRenderer();
            foreach (var id in new[] { "text", "letterhead-last", "landscape", "simple-table", "long-table", "long-table-multiline", "table-no-header", "table-mixed-header", "long-paragraph", "tall-cell" })
            {
                await page.EvaluateAsync("id => window.nativeFlowLab.load(id)", id);
                var snapshot = await Stable(page); var json = await Json(page);
                Assert.True(DocumentContentJson.TryGetPlainText(json, out _));
                Assert.True(snapshot.GetProperty("sourceTextPreserved").GetBoolean());
                Assert.True(snapshot.GetProperty("jsonUnchanged").GetBoolean());
                Assert.True(snapshot.GetProperty("bodyDomIsSingle").GetBoolean());
                if (id != "tall-cell") foreach (var table in snapshot.GetProperty("tables").EnumerateArray()) {
                    Assert.True(table.GetProperty("repetitionMatches").GetBoolean(), $"Header fragment mismatch: {id}: {table}");
                    Assert.Equal(1, table.GetProperty("bodyGroups").GetInt32());
                }
                using var fixtureJson = JsonDocument.Parse(await page.EvaluateAsync<string>("() => JSON.stringify(window.nativeFlowLab.fixture())"));
                var value = fixtureJson.RootElement;
                var input = new DraftPdfInput(Guid.NewGuid(), 1, null, json,
                    Part(value.GetProperty("header"), "header"), Part(value.GetProperty("footer"), "footer"),
                    value.GetProperty("layout").GetRawText(), null, new Dictionary<Guid, PdfImage>())
                    { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 1 };
                int? pdfPages = null; string? rejection = null;
                try
                {
                    var bytes = await renderer.RenderAsync(input, CancellationToken.None);
                    using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import); pdfPages = pdf.PageCount;
                    await File.WriteAllBytesAsync(Path.Combine(output, id + ".pdf"), bytes);
                }
                catch (PdfRenderValidationException error) { rejection = error.Message; }
                if (id == "tall-cell")
                {
                    Assert.Equal("tall-row-exceeds-printable-page", snapshot.GetProperty("layoutIssue").GetString());
                    Assert.Null(pdfPages); Assert.Contains("table row", rejection);
                    // No retrying paginator or proliferating page nodes on an unsupported row.
                    for (var n = 0; n < 10; n++) {
                        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                        Assert.Equal(1, await page.Locator(".sheet").CountAsync());
                    }
                }
                else { Assert.Null(rejection); Assert.NotNull(pdfPages); }
                await Shot(page, output, id);
                if (id == "letterhead-last")
                {
                    Assert.Equal(snapshot.GetProperty("pages").GetInt32(), await page.Locator(".sheet .letterhead").CountAsync());
                    Assert.Equal(1, await page.Locator(".sheet .letterfoot").CountAsync());
                    await page.EvaluateAsync("() => window.nativeFlowLab.jump(window.nativeFlowLab.report().pages)");
                    await Shot(page, output, id + "-last-page");
                }
                if (id is "long-table" or "long-table-multiline" or "table-no-header" or "table-mixed-header" or "long-paragraph") {
                    await page.EvaluateAsync("() => window.nativeFlowLab.jump(2)"); await Shot(page, output, id + "-page2");
                    await page.EvaluateAsync("() => window.nativeFlowLab.jump(window.nativeFlowLab.report().pages)"); await Shot(page, output, id + "-last-page");
                }
                samples[id] = new { Editor = snapshot, ProductionPdfPages = pdfPages, RendererRejection = rejection };
            }
            var report = new { RunUtc = DateTimeOffset.UtcNow, LocalOnly = true, ProductionApproved = false,
                KeyboardPageTwoEditUndoRedoPassed = true, ForwardBackwardFlowPassed = true, ZoomPreservedJsonAndSelection = true,
                RtlPageTwoCaretEditUndoPassed = true, NativeArrowForwardPassed = arrowForward, NativeArrowBackwardPassed = arrowBackward,
                NativeArrowUiSynchronized = arrowUiSynchronized,
                TableEditing = tableEditing,
                NativeArrowDown = arrowDown.Select(r => new { UiPage = r.GetProperty("activePage"), Selection = r.GetProperty("selection") }).ToArray(),
                NativeArrowUp = arrowUp.Select(r => new { UiPage = r.GetProperty("activePage"), Selection = r.GetProperty("selection") }).ToArray(),
                ReadonlyNavigationPassed = true, MobileOuterOverflow = false, Baseline = baseline, Grown = grown, AfterUndo = shrunk,
                Samples = samples, ExternalRequests = external, BrowserErrors = errors,
                BoundaryParity = "Run compare_pdf.py; the harness completing is not engine approval." };
            await File.WriteAllTextAsync(Path.Combine(output, "browser-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Assert.Empty(external); Assert.Empty(errors);
        }
        finally
        {
            if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
            var error = await stderr; if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine(error);
        }
    }

    private static string? Part(JsonElement lines, string type)
    {
        if (lines.GetArrayLength() == 0) return null;
        return JsonSerializer.Serialize(new { type, content = lines.EnumerateArray().Select(line => new {
            type = "paragraph", attrs = new { dir = "auto" }, content = new[] { new { type = "text", text = line.GetString() } }
        }).ToArray() });
    }
    private static Task PlaceCaret(IPage page, string marker, bool start) => page.EvaluateAsync("""
        value => {
            const editor = document.querySelector('#editor [role=textbox]');
            const p = [...editor.querySelectorAll('p')].find(p => p.textContent.includes(value.marker));
            editor.focus(); const range = document.createRange(); range.selectNodeContents(p); range.collapse(value.start);
            const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range);
        }
        """, new { marker, start });
    private static async Task<string> Json(IPage page) => await page.EvaluateAsync<string>("() => window.nativeFlowLab.json()");
    private static async Task Shot(IPage page, string output, string name) => await page.ScreenshotAsync(new() { Path = Path.Combine(output, name + ".png") });
    private static async Task<JsonElement> Stable(IPage page)
    {
        await page.WaitForFunctionAsync("() => window.nativeFlowLab.ready() && window.nativeFlowLab.report().millisecondsSinceLayout > 100");
        using var report = JsonDocument.Parse(await page.EvaluateAsync<string>("() => JSON.stringify(window.nativeFlowLab.report())"));
        return report.RootElement.Clone();
    }
}
