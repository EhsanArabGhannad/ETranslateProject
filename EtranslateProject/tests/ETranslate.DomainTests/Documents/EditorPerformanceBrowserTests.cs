using System.Diagnostics;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorPerformanceFactAttribute : FactAttribute
{
    public LocalEditorPerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_PERFORMANCE_TESTS") != "1") Skip = "Run Verify-EditorPerformance.ps1; synthetic loopback documents and independent browser only.";
        Timeout = 300000;
    }
}

public sealed partial class EditorAdapterBrowserTests
{
    internal static Task PerformanceFrames(IPage page, int frames = 3) => page.EvaluateAsync("n => new Promise(resolve => {const frame=()=>--n>0?requestAnimationFrame(frame):resolve();requestAnimationFrame(frame);})", frames);

    [LocalEditorPerformanceFact]
    public async Task Pagination_cache_invalidates_for_page_overflow_and_undo_can_restore_the_supported_view()
    {
        await using var fixture = await BrowserFixture.CreateAsync(EditorPerformanceSamples.Body("fifty-pages", BrowserFixture.ImageId));
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10"; await fixture.OpenAsync(); var page = fixture.Page;
        var viewport = page.Locator("#editor-viewport"); var json = page.Locator("#document-json");
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-page-count", "50");
        await page.Locator("#editor-page-index").SelectOptionAsync("50"); await page.Locator("#rich-text [role=textbox] > p").Last.ClickAsync();
        await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync("x"); await Command(page, "undo").ClickAsync(); await PerformanceFrames(page);
        var before = await json.InputValueAsync();
        await page.Keyboard.InsertTextAsync("PERFORMANCE_OVERFLOW " + string.Concat(Enumerable.Repeat("İstanbul ترجمه with long text. ", 400)));
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "continuous"); Assert.Contains("PERFORMANCE_OVERFLOW", await json.InputValueAsync());
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
        await Command(page, "undo").ClickAsync(); await PerformanceFrames(page); Assert.Equal(before, await json.InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-page-count", "50"); Assert.Equal(before, await json.InputValueAsync()); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorPerformanceFact]
    public async Task Large_documents_measure_input_view_work_and_pdf_parity_without_rewriting_source()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_PERFORMANCE_OUTPUT")!); Directory.CreateDirectory(output);
        var baseline = Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_PERFORMANCE_BASELINE") == "1";
        foreach (var name in EditorPerformanceSamples.Names) {
            var source = EditorPerformanceSamples.Body(name, BrowserFixture.ImageId); Assert.True(DocumentContentDescriptor.RichV10.TryRead(source, out var plain));
            await using var fixture = await BrowserFixture.CreateAsync(source); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            fixture.Header = EditorPerformanceSamples.Header; fixture.Footer = EditorPerformanceSamples.Footer;
            fixture.Layout = (DocumentPageLayout.Default with { FooterPlacement = "LastPage" }).ToJson();
            var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1100);
            var watch = Stopwatch.StartNew(); await fixture.OpenAsync(); var readyMs = watch.Elapsed.TotalMilliseconds;
            var textbox = page.Locator("#rich-text [role=textbox]"); var json = page.Locator("#document-json"); var viewport = page.Locator("#editor-viewport");
            watch.Restart(); await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged", new() { Timeout = 20000 }); await PerformanceFrames(page);
            var enableMs = watch.Elapsed.TotalMilliseconds; Assert.Equal(source, await json.InputValueAsync());
            await page.EvaluateAsync("""
                () => {window.perfQa={measurements:0,input:[],longTasks:[]};
                    const box=document.querySelector('#editor-flow-measure');
                    new MutationObserver(records=>{for(const record of records)for(const node of record.addedNodes)if(node.nodeType===1&&node.classList.contains('document-flow'))perfQa.measurements++;}).observe(box,{childList:true});
                    document.querySelector('#rich-text [role=textbox]').addEventListener('input',()=>{const start=performance.now();requestAnimationFrame(()=>requestAnimationFrame(()=>perfQa.input.push(performance.now()-start)));},true);
                    if(PerformanceObserver.supportedEntryTypes.includes('longtask'))new PerformanceObserver(list=>{for(const task of list.getEntries())perfQa.longTasks.push(task.duration);}).observe({type:'longtask'});
                }
                """);
            await textbox.Locator(":scope > p").First.EvaluateAsync("p=>{p.closest('[role=textbox]').focus();const range=document.createRange();range.selectNodeContents(p);range.collapse(false);const s=getSelection();s.removeAllRanges();s.addRange(range);}"); await PerformanceFrames(page);
            var before = await page.EvaluateAsync<int>("perfQa.measurements"); watch.Restart();
            for (var i = 0; i < 12; i++) { await page.Keyboard.PressAsync(i % 2 == 0 ? "ArrowLeft" : "ArrowRight"); await PerformanceFrames(page, 2); }
            var caretMs = watch.Elapsed.TotalMilliseconds; var caretMeasures = await page.EvaluateAsync<int>("perfQa.measurements") - before;
            before = await page.EvaluateAsync<int>("perfQa.measurements"); watch.Restart();
            foreach (var zoom in new[] { "75", "125", "100", "fit" }) { await page.Locator("#editor-zoom").SelectOptionAsync(zoom); await PerformanceFrames(page); }
            var zoomMs = watch.Elapsed.TotalMilliseconds; var zoomMeasures = await page.EvaluateAsync<int>("perfQa.measurements") - before;
            Assert.Equal(source, await json.InputValueAsync());
            if (!baseline) { Assert.Equal(0, caretMeasures); Assert.Equal(0, zoomMeasures); }
            before = await page.EvaluateAsync<int>("perfQa.measurements");
            foreach (var character in "PERF_EDIT_123456789") {
                var sampleCount = await page.EvaluateAsync<int>("perfQa.input.length");
                await page.Keyboard.InsertTextAsync(character.ToString()); await page.WaitForFunctionAsync("n=>perfQa.input.length>n", sampleCount);
            }
            await PerformanceFrames(page); var editMeasures = await page.EvaluateAsync<int>("perfQa.measurements") - before; Assert.True(editMeasures > 0);
            await textbox.Locator(":scope > p").First.EvaluateAsync("p=>{const range=document.createRange();range.selectNodeContents(p);const s=getSelection();s.removeAllRanges();s.addRange(range);}");
            before = await page.EvaluateAsync<int>("perfQa.measurements"); await page.Locator("#editor-font-size").SelectOptionAsync("18"); await PerformanceFrames(page); Assert.True(await page.EvaluateAsync<int>("perfQa.measurements") > before);
            var edited = await json.InputValueAsync(); await Command(page, "undo").ClickAsync(); await PerformanceFrames(page); Assert.NotEqual(edited, await json.InputValueAsync());
            await Command(page, "redo").ClickAsync(); await PerformanceFrames(page); Assert.Equal(edited, await json.InputValueAsync()); Assert.True(DocumentContentDescriptor.RichV10.TryRead(edited, out _));
            var count = int.Parse((await viewport.GetAttributeAsync("data-page-count"))!); if (name == "fifty-pages") Assert.Equal(50, count);
            await page.Locator("#editor-page-index").SelectOptionAsync(count.ToString()); await PerformanceFrames(page); await viewport.ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, name + ".png"), Scale = ScreenshotScale.Css });
            await File.WriteAllTextAsync(Path.Combine(output, name + "-body.json"), edited);
            var map = await PerformanceMarkerPages(page); await File.WriteAllTextAsync(Path.Combine(output, name + "-markers.json"), JsonSerializer.Serialize(new { pageCount = count, markers = map }));
            double? pdfMs = null; int? pdfBytes = null;
            if (!baseline) {
                using var renderer = new ChromiumDraftPdfRenderer(); var images = new Dictionary<Guid, PdfImage>(); if (name == "mixed-images") images.Add(BrowserFixture.ImageId, new("image/png", BodyImageFixture.Png));
                watch.Restart(); var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, edited, fixture.Header, fixture.Footer, fixture.Layout, null, images) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 }, CancellationToken.None);
                pdfMs = watch.Elapsed.TotalMilliseconds; pdfBytes = bytes.Length; await File.WriteAllBytesAsync(Path.Combine(output, name + ".pdf"), bytes);
                using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import); Assert.Equal(count, pdf.PageCount);
            }
            var input = await page.EvaluateAsync<double[]>("perfQa.input"); Array.Sort(input); var p95 = input[(int)Math.Ceiling(input.Length * .95) - 1];
            Assert.True(readyMs < 15000 && enableMs < 15000, $"{name}: editor initialization exceeded the local acceptance budget.");
            Assert.True(p95 < 750, $"{name}: input-to-two-frame p95 {p95:F1}ms exceeded the local 750ms budget.");
            Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
            await File.WriteAllTextAsync(Path.Combine(output, name + "-report.json"), JsonSerializer.Serialize(new { name, baseline, browserVersion = fixture.BrowserVersion, characters = plain!.Length,
                jsonBytes = System.Text.Encoding.UTF8.GetByteCount(edited), readyMs, enableMs, caretMs, caretMeasures, zoomMs, zoomMeasures, editMeasures, inputSamples = input.Length, inputP95Ms = p95,
                pageCount = count, pdfMs, pdfBytes, longTasks = await page.EvaluateAsync<double[]>("perfQa.longTasks"), errors = fixture.Errors, unexpected = fixture.Unexpected, passed = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    internal static async Task<Dictionary<string, int>> PerformanceMarkerPages(IPage page) => JsonSerializer.Deserialize<Dictionary<string, int>>(await page.EvaluateAsync<string>("""
        () => {const root=document.querySelector('#rich-text [role=textbox]'),paper=document.querySelector('#editor-paper'),scale=new DOMMatrixReadOnly(getComputedStyle(paper).transform).a,
            pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={},walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
            for(let node=walker.nextNode();node;node=walker.nextNode())for(const m of node.data.matchAll(/PERF_(?:START|TAIL|TEXT_\d{3}|CELL_\d{2}_\d{2}_\d{2}|MIX_\d{2}_\d{2}|PAGE_\d{3})/g)) {
                const range=document.createRange();range.setStart(node,m.index);range.setEnd(node,m.index+m[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);
                result[m[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;
            }return JSON.stringify(result);}
        """))!;
}
