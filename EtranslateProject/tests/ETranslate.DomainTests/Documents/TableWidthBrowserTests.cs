using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static async Task EnterColumnWidths(IPage page, params int[] widths)
    {
        await page.Locator("#editor-table-widths").EvaluateAsync("e=>e.open=true");
        var inputs = page.Locator("#editor-column-inputs input"); Assert.Equal(widths.Length, await inputs.CountAsync());
        for (var i = 0; i < widths.Length; i++) await inputs.Nth(i).FillAsync(widths[i].ToString());
    }
    private static async Task AssertWidthValues(IPage page, params int[] widths)
    {
        var inputs = page.Locator("#editor-column-inputs input"); Assert.Equal(widths.Length, await inputs.CountAsync());
        for (var i = 0; i < widths.Length; i++) await Assertions.Expect(inputs.Nth(i)).ToHaveValueAsync(widths[i].ToString());
    }
    [LocalEditorAdapterFact(Timeout = 120000)]
    public async Task Column_percentages_preserve_content_selection_history_and_saved_v6_with_locks()
    {
        var doc = JsonNode.Parse(HeaderRowDocument())!;
        doc["content"]![3] = JsonNode.Parse(doc["content"]![1]!.ToJsonString().Replace("HRT_", "OTHER_"));
        var source = doc.ToJsonString(); await using var fixture = await BrowserFixture.CreateAsync(source);
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "5";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var json = page.Locator("#document-json"); var table = page.Locator("#rich-text table").First;
        await SelectHeaderMarker(page, "HRT_003"); await EnterColumnWidths(page, 25, 50, 24);
        await Assertions.Expect(page.Locator("#editor-column-apply")).ToBeDisabledAsync();
        Assert.True(await page.Locator("#draft-form").EvaluateAsync<bool>("e=>e.checkValidity()"));
        await page.Locator("#editor-column-apply").DispatchEventAsync("click"); Assert.Equal(source, await json.InputValueAsync());
        await EnterColumnWidths(page, 25, 50, 25); await page.Locator("#editor-column-inputs input").Last.PressAsync("Enter");
        Assert.Equal(source, await json.InputValueAsync()); Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());
        var content = await table.Locator("tr").EvaluateAllAsync<string[]>("nodes=>nodes.map(node=>node.innerHTML)");
        await page.Locator("#editor-column-apply").ClickAsync();
        var unequal = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV7.TryRead(unequal, out _)); Assert.False(DocumentContentDescriptor.RichV5.TryRead(unequal, out _));
        Assert.Equal("HRT_003", await page.EvaluateAsync<string>("()=>getSelection().toString()"));
        Assert.Equal(content, await table.Locator("tr").EvaluateAllAsync<string[]>("nodes=>nodes.map(node=>node.innerHTML)"));
        var ratios = await table.EvaluateAsync<double[]>("e=>[...e.rows[0].cells].map(cell=>100*cell.getBoundingClientRect().width/e.getBoundingClientRect().width)");
        for (var i = 0; i < ratios.Length; i++) Assert.InRange(ratios[i], new[] { 25d, 50, 25 }[i] - .3, new[] { 25d, 50, 25 }[i] + .3);
        await page.Locator("#editor-column-reset").ClickAsync(); await AssertWidthValues(page, 33, 33, 34);
        await Command(page, "undo").ClickAsync(); Assert.Equal(unequal, await json.InputValueAsync());
        await Command(page, "redo").ClickAsync(); await Command(page, "undo").ClickAsync(); Assert.Equal(unequal, await json.InputValueAsync());
        await Command(page, "addColumnAfter").ClickAsync(); Assert.Equal(4, await table.Locator("tr").First.Locator("th,td").CountAsync());
        await Assertions.Expect(page.Locator("#editor-column-reset")).ToBeDisabledAsync();
        await Command(page, "undo").ClickAsync(); Assert.Equal(unequal, await json.InputValueAsync());
        await Command(page, "deleteColumn").ClickAsync(); Assert.Equal(2, await table.Locator("tr").First.Locator("th,td").CountAsync());
        await Command(page, "undo").ClickAsync(); Assert.Equal(unequal, await json.InputValueAsync());
        await EnterColumnWidths(page, 10, 20, 70); await SelectHeaderMarker(page, "OTHER_003"); await AssertWidthValues(page, 33, 33, 34);
        await SelectHeaderMarker(page, "HRT_003"); await AssertWidthValues(page, 25, 50, 25);
        var retry = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(page.Locator("#editor-column-apply")).ToBeDisabledAsync(); await page.Locator("#editor-column-reset").DispatchEventAsync("click"); Assert.Equal(unequal, await json.InputValueAsync());
        retry.Release.SetResult(503); await Assertions.Expect(page.Locator("#editor-column-apply")).ToBeEnabledAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "7"; save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(unequal, await json.InputValueAsync()); await SelectHeaderMarker(page, "HRT_003"); await AssertWidthValues(page, 25, 50, 25);
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        await page.Locator("#editor-table-widths").EvaluateAsync("e=>e.open=true");
        await page.Locator("#editor-focus-toggle").ClickAsync(); await fixture.ScreenshotAsync("table-column-widths.png"); await page.Keyboard.PressAsync("Escape");
        fixture.Editable = false; await fixture.OpenAsync(); await SelectHeaderMarker(page, "HRT_003");
        Assert.True(await page.Locator("#editor-column-inputs input").EvaluateAllAsync<bool>("nodes=>nodes.every(node=>node.disabled)"));
        await page.Locator("#editor-column-reset").DispatchEventAsync("click"); Assert.Equal(unequal, await json.InputValueAsync()); await AssertFrozenAsync(page);
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    private static string ColumnPrintDocument()
    {
        object P(string text, string dir = "ltr") => new { type = "paragraph", attrs = new { dir }, content = new[] { new { type = "text", text } } };
        object T(string prefix, int columns, bool heading, string dir) => new { type = "table", attrs = new { dir }, content = Enumerable.Range(0, 35).Select(r => new { type = "tableRow", content = Enumerable.Range(0, columns).Select(c => new { type = heading && r == 0 ? "tableHeader" : "tableCell", content = new[] { P(c == 0 ? $"CW_{prefix}_{r:000} İstanbul" : string.Concat(Enumerable.Repeat(c == 1 ? "Sentetik çeviri metni burada. " : "ترجمهٔ آزمایشی برای جدول. ", 2)), c == 2 ? "rtl" : "ltr") } }).ToArray() }).ToArray() };
        return JsonSerializer.Serialize(new { type = "doc", content = new[] { T("A", 3, true, "ltr"), T("B", 2, false, "rtl") } });
    }
    [LocalEditorAdapterFact(Timeout = 180000)]
    public async Task Column_percentages_match_editor_preview_and_pdf_geometry_for_ltr_and_rtl_tables()
    {
        await using var fixture = await BrowserFixture.CreateAsync(ColumnPrintDocument());
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "5";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        foreach (var spec in new[] { (Marker: "CW_A_030", Widths: new[] { 50, 30, 20 }), (Marker: "CW_B_030", Widths: new[] { 65, 35 }) })
        {
            await SelectHeaderMarker(page, spec.Marker); await EnterColumnWidths(page, spec.Widths); await page.Locator("#editor-column-apply").ClickAsync();
        }
        var source = await page.Locator("#document-json").InputValueAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "7"; save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(source, await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/table-widths")); Directory.CreateDirectory(directory);
        var markers = await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/CW_[AB]_\d{3}/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "width-markers.json"), markers);
        using (var renderer = new ChromiumDraftPdfRenderer())
        {
            var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, source, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 7 }, CancellationToken.None);
            using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!), pdf.PageCount); Assert.True(pdf.PageCount > 2);
            await File.WriteAllBytesAsync(Path.Combine(directory, "column-widths.pdf"), bytes);
        }
        var viewRatios = await page.Locator("#rich-text table").EvaluateAllAsync<double[][]>("tables=>tables.map(e=>[...e.rows[0].cells].map(cell=>100*[...cell.getClientRects()].find(r=>r.width>0).width/[...e.getClientRects()].find(r=>r.width>0).width))");
        for (var t = 0; t < 2; t++) for (var c = 0; c < viewRatios[t].Length; c++) Assert.InRange(viewRatios[t][c], new[] { new[] { 50d, 30, 20 }, new[] { 65d, 35 } }[t][c] - .3, new[] { new[] { 50d, 30, 20 }, new[] { 65d, 35 } }[t][c] + .3);
        // Fixture preview is hidden; verify its semantic width declaration without hidden-box geometry.
        Assert.Equal(new[] { "50%", "30%", "20%", "65%", "35%" }, await page.Locator("#preview-body col").EvaluateAllAsync<string[]>("cols=>cols.map(col=>col.style.width)"));
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
}
