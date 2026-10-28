using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string MergeDocument()
    {
        object P(string text, string dir = "ltr") => new { type = "paragraph", attrs = new { dir, textAlign = dir == "rtl" ? "right" : "left", lineHeight = 1.5 }, content = new[] { new { type = "text", text, marks = new object[] { new { type = "bold" }, new { type = "typography", attrs = new { fontFamily = "serif", fontSize = 12, color = "#1d4ed8" } } } } } };
        object T(string prefix) => new { type = "table", attrs = new { columnWidths = new[] { 25, 50, 25 } }, content = Enumerable.Range(0, 4).Select(r => new { type = "tableRow", content = Enumerable.Range(0, 3).Select(c => new { type = r == 0 ? "tableHeader" : "tableCell", attrs = new { backgroundColor = r == 1 && c == 2 ? "#fef08a" : "#bfdbfe", borderColor = "#b91c1c", borderWidthPt = 1.5, borderStyle = "solid" }, content = new[] { P($"{prefix}_R{r}C{c} İstanbul"), P("ترجمهٔ آزمایشی", "rtl") } }).ToArray() }).ToArray() };
        return JsonSerializer.Serialize(new { type = "doc", content = new object[] { P("BEFORE_MERGE"), T("MG"), P("BETWEEN_MERGE"), T("OT"), P("AFTER_MERGE") } });
    }
    private static async Task SelectMergeRange(IPage page, ILocator table, int firstRow, int firstCell, int lastRow, int lastCell)
    {
        await table.Locator("tr").Nth(firstRow).ScrollIntoViewIfNeededAsync();
        await page.Locator("#rich-text [role=textbox]").FocusAsync();
        await table.EvaluateAsync("""
            (table,arg)=>{const from=table.rows[arg.firstRow].cells[arg.firstCell],to=table.rows[arg.lastRow].cells[arg.lastCell];
                const range=document.createRange();range.setStart(from,0);range.setEnd(to,to.childNodes.length);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}
            """, new { firstRow, firstCell, lastRow, lastCell });
    }
    [LocalEditorAdapterFact(Timeout = 120000)]
    public async Task Horizontal_merge_and_split_preserve_text_style_widths_history_save_and_locks()
    {
        var source = MergeDocument(); await using var fixture = await BrowserFixture.CreateAsync(source); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "7";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var json = page.Locator("#document-json"); var table = page.Locator("#rich-text table").First; var row = table.Locator("tr").First;
        await page.Locator("#editor-horizontal-merge").EvaluateAsync("e=>e.open=true"); Assert.Equal(source, await json.InputValueAsync());
        await Assertions.Expect(Command(page, "mergeNextCell")).ToBeDisabledAsync();
        var other = await page.Locator("#rich-text table").Last.InnerHTMLAsync();
        await SelectHeaderMarker(page, "MG_R0C0");
        var original = await row.Locator("th,td").EvaluateAllAsync<string[]>("cells=>cells.map(cell=>cell.innerHTML)");
        await Command(page, "mergeNextCell").ClickAsync();
        Assert.Equal(2, await row.Locator("th").CountAsync()); await Assertions.Expect(row.Locator("th").First).ToHaveAttributeAsync("colspan", "2");
        await Assertions.Expect(row.Locator("th").First).ToContainTextAsync("MG_R0C1"); Assert.Equal(4, await row.Locator("th").First.Locator("p").CountAsync());
        var merged = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV9.TryRead(merged, out _)); Assert.False(DocumentContentDescriptor.RichV7.TryRead(merged, out _));
        await Command(page, "undo").ClickAsync(); Assert.Equal(original, await row.Locator("th,td").EvaluateAllAsync<string[]>("cells=>cells.map(cell=>cell.innerHTML)"));
        await Command(page, "redo").ClickAsync(); Assert.Equal(merged, await json.InputValueAsync());
        await Command(page, "splitCellInRow").ClickAsync(); Assert.Equal(3, await row.Locator("th").CountAsync());
        await Assertions.Expect(row.Locator("th").First).ToContainTextAsync("MG_R0C1"); Assert.Equal("", await row.Locator("th").Nth(1).TextContentAsync());
        await Command(page, "undo").ClickAsync(); Assert.Equal(merged, await json.InputValueAsync());
        await Command(page, "mergeNextCell").ClickAsync(); await Assertions.Expect(row.Locator("th")).ToHaveAttributeAsync("colspan", "3");
        await AssertWidthValues(page, 25, 50, 25); var full = await json.InputValueAsync();
        await Assertions.Expect(Command(page, "deleteColumn")).ToBeDisabledAsync(); await Command(page, "deleteColumn").DispatchEventAsync("click"); Assert.Equal(full, await json.InputValueAsync());
        await Command(page, "toggleTableHeaderRow").ClickAsync(); await Assertions.Expect(row.Locator("td")).ToHaveAttributeAsync("colspan", "3");
        await Command(page, "toggleTableHeaderRow").ClickAsync(); Assert.Equal(full, await json.InputValueAsync());
        await SelectHeaderMarker(page, "MG_R1C1"); await Assertions.Expect(Command(page, "mergeNextCell")).ToBeDisabledAsync();
        await Command(page, "mergeNextCell").DispatchEventAsync("click"); Assert.Equal(full, await json.InputValueAsync());
        await SelectMergeRange(page, table, 2, 0, 2, 1); await Assertions.Expect(Command(page, "mergeCellsInRow")).ToBeEnabledAsync(); await Command(page, "mergeCellsInRow").ClickAsync();
        Assert.Equal(2, await table.Locator("tr").Nth(2).Locator("td").CountAsync());
        var edited = await json.InputValueAsync(); Assert.Equal(other, await page.Locator("#rich-text table").Last.InnerHTMLAsync());
        await SelectMergeRange(page, table, 1, 0, 2, 1); await Assertions.Expect(Command(page, "mergeCellsInRow")).ToBeDisabledAsync(); Assert.Equal(edited, await json.InputValueAsync());
        await SelectHeaderMarker(page, "MG_R2C0"); var retry = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(Command(page, "splitCellInRow")).ToBeDisabledAsync(); await Command(page, "splitCellInRow").DispatchEventAsync("click"); Assert.Equal(edited, await json.InputValueAsync());
        retry.Release.SetResult(503); await Assertions.Expect(Command(page, "splitCellInRow")).ToBeEnabledAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "9";
        save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(edited, await json.InputValueAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync(); await SelectHeaderMarker(page, "MG_R2C0"); await Assertions.Expect(Command(page, "splitCellInRow")).ToBeEnabledAsync();
        await page.Locator("#editor-horizontal-merge").EvaluateAsync("e=>e.open=true"); await page.Locator("#editor-search").EvaluateAsync("e=>e.open=false");
        await page.Locator("#editor-focus-toggle").ClickAsync(); await fixture.ScreenshotAsync("horizontal-merge.png"); await page.Keyboard.PressAsync("Escape");
        fixture.Editable = false; await fixture.OpenAsync(); await SelectHeaderMarker(page, "MG_R2C0");
        foreach (var command in new[] { "mergeNextCell", "mergeCellsInRow", "splitCellInRow" }) { await Assertions.Expect(Command(page, command)).ToBeDisabledAsync(); await Command(page, command).DispatchEventAsync("click"); }
        Assert.Equal(edited, await json.InputValueAsync()); await AssertFrozenAsync(page);
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
    [LocalEditorAdapterFact(Timeout = 180000)]
    public async Task Horizontal_spans_match_live_pagination_pdf_geometry_and_ltr_rtl_header_repetition()
    {
        var printDoc = JsonNode.Parse(ColumnPrintDocument())!;
        var equalRows = new JsonArray();
        for (var r = 0; r < 3; r++) {
            var cells = new JsonArray();
            for (var c = 0; c < 3; c++) cells.Add(JsonSerializer.SerializeToNode(new { type = r == 0 ? "tableHeader" : "tableCell", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = c == 0 ? $"EQ_{r:000}" : $"Equal cell {c}" } } } } }));
            equalRows.Add(new JsonObject { ["type"] = "tableRow", ["content"] = cells });
        }
        printDoc["content"]!.AsArray().Add(new JsonObject { ["type"] = "table", ["attrs"] = new JsonObject { ["dir"] = "rtl" }, ["content"] = equalRows });
        var source = printDoc.ToJsonString(); await using var fixture = await BrowserFixture.CreateAsync(source); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "7";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        foreach (var spec in new[] { (Marker: "CW_A_030", Widths: new[] { 50, 30, 20 }, Fill: "#bfdbfe"), (Marker: "CW_B_030", Widths: new[] { 65, 35 }, Fill: "#bbf7d0") }) {
            await SelectHeaderMarker(page, spec.Marker); await EnterColumnWidths(page, spec.Widths); await page.Locator("#editor-column-apply").ClickAsync(); await CellField(page, "#editor-cell-background", spec.Fill, "table");
        }
        await page.Locator("#editor-horizontal-merge").EvaluateAsync("e=>e.open=true");
        foreach (var spec in new[] { (Marker: "CW_A_000", Times: 2), (Marker: "CW_A_010", Times: 1), (Marker: "CW_A_020", Times: 2), (Marker: "CW_B_000", Times: 1), (Marker: "CW_B_017", Times: 1), (Marker: "EQ_000", Times: 2), (Marker: "EQ_001", Times: 1) }) {
            await SelectHeaderMarker(page, spec.Marker); for (var i = 0; i < spec.Times; i++) await Command(page, "mergeNextCell").ClickAsync();
        }
        var edited = await page.Locator("#document-json").InputValueAsync();
        Assert.True(DocumentContentDescriptor.RichV7.TryRead(source, out var before)); Assert.True(DocumentContentDescriptor.RichV9.TryRead(edited, out var after)); Assert.Equal(before, after);
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "9";
        save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(edited, await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/horizontal-merge")); Directory.CreateDirectory(directory);
        var markers = await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/CW_[AB]_\d{3}|EQ_\d{3}/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "merge-markers.json"), markers);
        using (var renderer = new ChromiumDraftPdfRenderer()) {
            var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, edited, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 9 }, CancellationToken.None);
            using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!), pdf.PageCount); Assert.True(pdf.PageCount > 2); await File.WriteAllBytesAsync(Path.Combine(directory, "horizontal-merge.pdf"), bytes);
        }
        Assert.Equal("3", await page.Locator("#preview-body table").First.Locator("tr").First.Locator("th").GetAttributeAsync("colspan"));
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
}
