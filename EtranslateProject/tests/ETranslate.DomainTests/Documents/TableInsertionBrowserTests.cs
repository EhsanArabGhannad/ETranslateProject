using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private const string TableInsertionSource = """
        {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"INTRO İstanbul çeviri"}]},{"type":"paragraph","content":[{"type":"text","text":"INSERT_HERE"}]},{"type":"paragraph","attrs":{"dir":"rtl"},"content":[{"type":"text","text":"END_ANCHOR ترجمهٔ آزمایشی"}]}]}
        """;

    private static async Task TableDimensions(IPage page, int rows, int columns, bool header)
    {
        await page.Locator("#editor-table-settings").EvaluateAsync("e=>e.open=true");
        await page.Locator("#editor-table-rows").FillAsync(rows.ToString());
        await page.Locator("#editor-table-columns").FillAsync(columns.ToString());
        await page.Locator("#editor-table-header").SetCheckedAsync(header);
    }

    [LocalEditorAdapterFact(Timeout = 120000)]
    public async Task Table_size_picker_validates_bounds_preserves_selection_history_save_and_locks()
    {
        await using var fixture = await BrowserFixture.CreateAsync(TableInsertionSource);
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "4";
        var page = fixture.Page; await page.SetViewportSizeAsync(1500, 1200); await fixture.OpenAsync();
        var textbox = page.Locator("#rich-text [role=textbox]"); var json = page.Locator("#document-json");
        await textbox.Locator(":scope>p").Nth(1).ClickAsync(); await page.Keyboard.PressAsync("End");
        await page.Locator("#editor-table-settings summary").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-table-size")).ToHaveTextAsync("3 satır × 3 sütun · Başlıklı");
        var grid = page.Locator("#editor-table-grid");
        await grid.Locator("button[data-rows='2'][data-columns='4']").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-table-rows")).ToHaveValueAsync("2");
        await Assertions.Expect(page.Locator("#editor-table-columns")).ToHaveValueAsync("4");
        Assert.Equal(TableInsertionSource, await json.InputValueAsync());
        Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        await grid.Locator("button").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft"); await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(grid.Locator("button").First).ToBeFocusedAsync();
        await grid.Locator("button").Last.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight"); await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(grid.Locator("button").Last).ToBeFocusedAsync();
        var gridChoice = grid.Locator("button[data-rows='2'][data-columns='4']");
        await gridChoice.FocusAsync(); await page.Keyboard.PressAsync("ArrowDown"); await page.Keyboard.PressAsync("ArrowRight"); await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#editor-table-size")).ToHaveTextAsync("3 satır × 5 sütun · Başlıklı");
        await grid.Locator("button[data-rows='3'][data-columns='5']").HoverAsync();
        Assert.Equal(15, await grid.Locator("button[data-preview=true]").CountAsync());
        Assert.Equal(5, await grid.Locator("button[data-preview=true][data-header=true]").CountAsync());
        Assert.Equal(1, await grid.Locator("button[tabindex='0']").CountAsync());
        foreach (var bad in new[] { "", "0", "51", "-1", "aa" })
        {
            await page.Locator("#editor-table-rows").FillAsync(bad);
            await Assertions.Expect(Command(page, "insertTable")).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("#editor-table-error")).ToBeVisibleAsync();
            Assert.True(await page.Locator("#draft-form").EvaluateAsync<bool>("e=>e.checkValidity()"));
            await Command(page, "insertTable").DispatchEventAsync("click");
            Assert.Equal(TableInsertionSource, await json.InputValueAsync());
        }
        await TableDimensions(page, 2, 13, false); await Assertions.Expect(Command(page, "insertTable")).ToBeDisabledAsync();
        await TableDimensions(page, 2, 4, false);
        await page.Locator("#editor-table-rows").PressAsync("Enter");
        Assert.Equal(TableInsertionSource, await json.InputValueAsync()); Assert.Empty(fixture.Unexpected);
        await Command(page, "insertTable").ClickAsync();
        var table = textbox.Locator("table"); Assert.Equal(2, await table.Locator("tr").CountAsync());
        Assert.Equal(8, await table.Locator("td").CountAsync()); Assert.Equal(0, await table.Locator("th").CountAsync());
        await Assertions.Expect(page.Locator("#editor-table-rows")).ToBeDisabledAsync();
        await page.Keyboard.InsertTextAsync("CELL_İstanbul");
        await Command(page, "undo").ClickAsync(); Assert.Equal(1, await textbox.Locator("table").CountAsync());
        Assert.DoesNotContain("CELL_İstanbul", await textbox.InnerTextAsync());
        await Command(page, "undo").ClickAsync(); Assert.Equal(0, await textbox.Locator("table").CountAsync());
        await Assertions.Expect(textbox).ToContainTextAsync("INSERT_HERE"); await Assertions.Expect(textbox).ToContainTextAsync("END_ANCHOR");
        await Command(page, "redo").ClickAsync(); await Command(page, "redo").ClickAsync();
        await Assertions.Expect(textbox).ToContainTextAsync("CELL_İstanbul");
        // Retry freezes the picker even when the selection is at an eligible top-level paragraph.
        await textbox.Locator(":scope>p").Last.ClickAsync(); await page.Keyboard.PressAsync("End");
        var source = await json.InputValueAsync(); var retry = fixture.Enqueue();
        await page.Locator("#draft-form button[type=submit]").ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(page.Locator("#editor-table-rows")).ToBeDisabledAsync();
        await Command(page, "insertTable").DispatchEventAsync("click"); Assert.Equal(source, await json.InputValueAsync());
        retry.Release.SetResult(503); await Assertions.Expect(page.Locator("#editor-table-rows")).ToBeEnabledAsync();
        var saved = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await saved.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(saved.Body); fixture.SchemaVersion = "5"; saved.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(source, await json.InputValueAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        Assert.Equal(8, await textbox.Locator("table td").CountAsync()); Assert.Equal(0, await textbox.Locator("table th").CountAsync());
        fixture.Editable = false; await fixture.OpenAsync();
        await page.Locator("#editor-table-settings").EvaluateAsync("e=>e.open=true");
        foreach (var id in new[] { "#editor-table-rows", "#editor-table-columns", "#editor-table-header" }) await Assertions.Expect(page.Locator(id)).ToBeDisabledAsync();
        await grid.Locator("button").Last.DispatchEventAsync("click"); await Command(page, "insertTable").DispatchEventAsync("click");
        Assert.Equal(source, await json.InputValueAsync()); await AssertFrozenAsync(page);
        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await grid.EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth;}"));
        await page.Locator("#editor-table-settings").ScrollIntoViewIfNeededAsync();
        await fixture.ScreenshotAsync("table-insertion-mobile.png");
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact(Timeout = 180000)]
    public async Task Chosen_table_minimum_maximum_and_header_modes_flow_through_pages_and_pdf()
    {
        await using var fixture = await BrowserFixture.CreateAsync(TableInsertionSource); var page = fixture.Page;
        await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var textbox = page.Locator("#rich-text [role=textbox]"); var json = page.Locator("#document-json");
        await textbox.Locator(":scope>p").Nth(1).ClickAsync(); await page.Keyboard.PressAsync("End");
        await TableDimensions(page, 50, 12, true); await Command(page, "insertTable").ClickAsync();
        Assert.Equal(50, await textbox.Locator("table tr").CountAsync()); Assert.Equal(12, await textbox.Locator("table th").CountAsync());
        await Assertions.Expect(Command(page, "addRowAfter")).ToBeDisabledAsync(); await Assertions.Expect(Command(page, "addColumnAfter")).ToBeDisabledAsync();
        var maximum = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV5.TryRead(maximum, out _));
        await textbox.Locator("table tr").Last.Locator("td p").Last.ClickAsync(); await page.Keyboard.PressAsync("Tab");
        Assert.Equal(maximum, await json.InputValueAsync());
        await Command(page, "undo").ClickAsync(); Assert.Equal(0, await textbox.Locator("table").CountAsync());
        await textbox.Locator(":scope>p").First.ClickAsync(); await page.Keyboard.PressAsync("End");
        await TableDimensions(page, 1, 1, false); await Command(page, "insertTable").ClickAsync();
        Assert.Equal(1, await textbox.Locator("table td").CountAsync()); Assert.Equal(0, await textbox.Locator("table th").CountAsync());
        await Command(page, "undo").ClickAsync(); Assert.Equal(0, await textbox.Locator("table").CountAsync());
        // Build real tables using only the public picker and normal cell typing.
        foreach (var spec in new[] { (Rows: 36, Columns: 4, Header: true, Prefix: "CFG_H_"), (Rows: 32, Columns: 3, Header: false, Prefix: "CFG_N_") })
        {
            await textbox.Locator(":scope>p").Last.ClickAsync(); await page.Keyboard.PressAsync("End");
            await TableDimensions(page, spec.Rows, spec.Columns, spec.Header); await Command(page, "insertTable").ClickAsync();
            var table = textbox.Locator("table").Last;
            for (var r = 0; r < spec.Rows; r++)
            {
                await table.Locator("tr").Nth(r).Locator("th,td").First.Locator("p").ClickAsync();
                await page.Keyboard.InsertTextAsync($"{spec.Prefix}{r:000} İstanbul ترجمه");
            }
        }
        var source = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV5.TryRead(source, out _));
        Assert.Equal(4, await textbox.Locator("table").First.Locator("th").CountAsync());
        Assert.Equal(0, await textbox.Locator("table").Last.Locator("th").CountAsync());
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(save.Body); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "5";
        save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(source, await json.InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/table-insertion")); Directory.CreateDirectory(directory);
        var markers = await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/CFG_[HN]_\d{3}/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "table-markers.json"), markers);
        using (var renderer = new ChromiumDraftPdfRenderer())
        {
            var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, source, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 5 }, CancellationToken.None);
            using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!), pdf.PageCount); Assert.True(pdf.PageCount > 2);
            await File.WriteAllBytesAsync(Path.Combine(directory, "chosen-tables.pdf"), bytes);
        }
        await textbox.Locator(":scope>p").First.ClickAsync(); await page.Locator("#editor-table-settings").EvaluateAsync("e=>e.open=true");
        await page.Locator("#editor-focus-toggle").ClickAsync(); await fixture.ScreenshotAsync("table-insertion.png"); await page.Keyboard.PressAsync("Escape");
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
}
