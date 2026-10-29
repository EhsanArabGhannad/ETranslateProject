using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static async Task CellField(IPage page, string id, string value, string scope = "selection")
    {
        await page.Locator("#editor-cell-style").EvaluateAsync("e=>e.open=true");
        await page.Locator("#editor-cell-scope").SelectOptionAsync(scope); await page.Locator(id).SelectOptionAsync(value);
    }
    [LocalEditorAdapterFact(Timeout = 120000)]
    public async Task Cell_styles_preserve_text_widths_mixed_state_history_save_and_write_locks()
    {
        var doc = JsonNode.Parse(HeaderRowDocument())!; doc["content"]![1]!["attrs"] = new JsonObject { ["columnWidths"] = new JsonArray(25, 50, 25) };
        var source = doc.ToJsonString(); await using var fixture = await BrowserFixture.CreateAsync(source);
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "6";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var json = page.Locator("#document-json"); var table = page.Locator("#rich-text table").First;
        Assert.Equal(source, await json.InputValueAsync()); await SelectHeaderMarker(page, "HRT_003");
        var text = await table.InnerTextAsync(); var widths = await table.Locator("col").EvaluateAllAsync<string[]>("cols=>cols.map(col=>col.style.width)");
        await CellField(page, "#editor-cell-background", "#bfdbfe");
        var single = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV10.TryRead(single, out _)); Assert.False(DocumentContentDescriptor.RichV6.TryRead(single, out _));
        Assert.Equal("HRT_003", await page.EvaluateAsync<string>("()=>getSelection().toString()"));
        Assert.Equal(1, await table.Locator("td").EvaluateAllAsync<int>("cells=>cells.filter(cell=>cell.style.backgroundColor==='rgb(191, 219, 254)').length"));
        await page.Locator("#editor-cell-scope").SelectOptionAsync("table"); await Assertions.Expect(page.Locator("#editor-cell-background")).ToHaveValueAsync("__mixed__");
        await CellField(page, "#editor-cell-border-color", "#b91c1c", "table");
        await CellField(page, "#editor-cell-border-width", "1.5", "table"); await CellField(page, "#editor-cell-border-style", "dashed", "table");
        var styled = await json.InputValueAsync(); Assert.Equal(text, await table.InnerTextAsync()); Assert.Equal(widths, await table.Locator("col").EvaluateAllAsync<string[]>("cols=>cols.map(col=>col.style.width)"));
        Assert.True(await table.Locator("th,td").EvaluateAllAsync<bool>("cells=>cells.every(cell=>cell.style.borderColor==='rgb(185, 28, 28)'&&cell.style.borderWidth==='1.5pt'&&cell.style.borderStyle==='dashed')"));
        await page.Locator("#editor-cell-reset").ClickAsync(); await Command(page, "undo").ClickAsync(); Assert.Equal(styled, await json.InputValueAsync());
        await Command(page, "redo").ClickAsync(); await Command(page, "undo").ClickAsync(); Assert.Equal(styled, await json.InputValueAsync());
        await CellField(page, "#editor-cell-border-width", "0");
        Assert.Equal("hidden", await table.Locator("td").Filter(new() { HasText = "HRT_003" }).EvaluateAsync<string>("e=>e.style.borderStyle"));
        await Command(page, "undo").ClickAsync(); Assert.Equal(styled, await json.InputValueAsync());
        // Arbitrary custom colors use the native color control, with one committed change.
        await page.Locator("#editor-cell-background-custom").EvaluateAsync("e=>{e.value='#fed7aa';e.dispatchEvent(new Event('change',{bubbles:true}));}");
        await Assertions.Expect(page.Locator("#editor-cell-background")).ToHaveValueAsync("#fed7aa"); await Command(page, "undo").ClickAsync(); Assert.Equal(styled, await json.InputValueAsync());
        var retry = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(page.Locator("#editor-cell-reset")).ToBeDisabledAsync(); await page.Locator("#editor-cell-reset").DispatchEventAsync("click"); Assert.Equal(styled, await json.InputValueAsync());
        retry.Release.SetResult(503); await Assertions.Expect(page.Locator("#editor-cell-reset")).ToBeEnabledAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "10";
        save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(styled, await json.InputValueAsync());
        await SelectHeaderMarker(page, "HRT_003"); await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        await page.Locator("#editor-cell-style").EvaluateAsync("e=>e.open=true"); await Assertions.Expect(page.Locator("#editor-cell-background")).ToHaveValueAsync("#bfdbfe");
        await page.Locator("#editor-search").EvaluateAsync("e=>e.open=false"); await page.Locator("#editor-focus-toggle").ClickAsync(); await fixture.ScreenshotAsync("table-cell-style.png"); await page.Keyboard.PressAsync("Escape");
        fixture.Editable = false; await fixture.OpenAsync(); await SelectHeaderMarker(page, "HRT_003");
        Assert.True(await page.Locator("#editor-cell-style :is(select[data-cell-field],input)").EvaluateAllAsync<bool>("controls=>controls.every(control=>control.disabled)"));
        await page.Locator("#editor-cell-reset").DispatchEventAsync("click"); Assert.Equal(styled, await json.InputValueAsync()); await AssertFrozenAsync(page);
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
    [LocalEditorAdapterFact(Timeout = 180000)]
    public async Task Cell_fills_borders_repeated_headers_and_hidden_edges_match_saved_pdf()
    {
        var doc = JsonNode.Parse(ColumnPrintDocument())!;
        doc["content"]!.AsArray().Add(JsonNode.Parse("""{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"CS_HIDE"}]}]}]}]}"""));
        await using var fixture = await BrowserFixture.CreateAsync(doc.ToJsonString()); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "6";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        foreach (var spec in new[] { (Marker: "CW_A_030", Fill: "#bfdbfe", Border: "#b91c1c", Width: "1.5", Style: "dashed", Percent: new[] { 50, 30, 20 }), (Marker: "CW_B_030", Fill: "#bbf7d0", Border: "#1d4ed8", Width: "2", Style: "dotted", Percent: new[] { 65, 35 }) })
        {
            await SelectHeaderMarker(page, spec.Marker); await EnterColumnWidths(page, spec.Percent); await page.Locator("#editor-column-apply").ClickAsync();
            await CellField(page, "#editor-cell-background", spec.Fill, "table"); await CellField(page, "#editor-cell-border-color", spec.Border, "table");
            await CellField(page, "#editor-cell-border-width", spec.Width, "table"); await CellField(page, "#editor-cell-border-style", spec.Style, "table");
        }
        await SelectHeaderMarker(page, "CW_A_000"); await CellField(page, "#editor-cell-background", "#fef08a");
        await SelectHeaderMarker(page, "CS_HIDE"); await CellField(page, "#editor-cell-border-width", "0", "table");
        var source = await page.Locator("#document-json").InputValueAsync(); var save = fixture.Enqueue();
        await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "10";
        save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(source, await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/cell-style")); Directory.CreateDirectory(directory);
        var markers = await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/CW_[AB]_\d{3}|CS_HIDE/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result,
                    borders:[...root.querySelectorAll('table')].slice(0,2).map(table=>{const cell=table.rows[0].cells[0],style=getComputedStyle(cell);return {nominalPt:parseFloat(cell.style.borderWidth),computedPt:parseFloat(style.borderTopWidth)*72/96,style:style.borderTopStyle};})});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "cell-markers.json"), markers);
        using (var renderer = new ChromiumDraftPdfRenderer())
        {
            var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, source, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 }, CancellationToken.None);
            using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!), pdf.PageCount); Assert.True(pdf.PageCount > 2);
            await File.WriteAllBytesAsync(Path.Combine(directory, "cell-style.pdf"), bytes);
        }
        Assert.Equal("rgb(191, 219, 254)", await page.Locator("#preview-body td").First.EvaluateAsync<string>("e=>e.style.backgroundColor"));
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
}
