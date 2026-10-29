using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string HeaderRowDocument(int rows = 4)
    {
        object Paragraph(string text, string dir = "ltr", bool styled = false) => new
        {
            type = "paragraph",
            attrs = new { dir, textAlign = dir == "rtl" ? "right" : "left", lineHeight = 1.5, spaceBeforePt = 0, spaceAfterPt = 3, firstLineIndentMm = styled ? 5 : 0 },
            content = new[] { new { type = "text", text, marks = styled ? new object[] { new { type = "bold" }, new { type = "typography", attrs = new { fontFamily = dir == "rtl" ? "arabic" : "serif", fontSize = 12, color = "#1d4ed8", highlight = "#fef08a" } } } : [] } }
        };
        object Cell(int r, int c) => new
        {
            type = r == 0 && c != 1 || r == 2 && c == 1 ? "tableHeader" : "tableCell",
            attrs = new { colspan = 1, rowspan = 1, colwidth = (object?)null, align = (object?)null },
            content = r == 0 ? new[] { Paragraph(c == 0 ? "HRT_000 İstanbul" : c == 1 ? "Başlık metni" : "عنوان ترجمه", c == 2 ? "rtl" : "ltr", true), Paragraph(c == 0 ? "İkinci paragraf" : "123", c == 2 ? "rtl" : "ltr") }
                : new[] { Paragraph(c == 0 ? $"HRT_{r:000} İstanbul" : c == 1 ? "Çeviri hücresi" : "ترجمهٔ آزمایشی", c == 2 ? "rtl" : "ltr") }
        };
        return JsonSerializer.Serialize(new
        {
            type = "doc", content = new object[] {
                Paragraph("BEFORE_TABLE"),
                new { type = "table", content = Enumerable.Range(0, rows).Select(r => new { type = "tableRow", content = Enumerable.Range(0, 3).Select(c => Cell(r, c)).ToArray() }).ToArray() },
                Paragraph("BETWEEN_TABLES"),
                new { type = "table", content = new[] { new { type = "tableRow", content = new[] { new { type = "tableHeader", content = new[] { Paragraph("OTHER_TABLE_HEADER") } } } },
                    new { type = "tableRow", content = new[] { new { type = "tableHeader", content = new[] { Paragraph("OTHER_TABLE_BODY") } } } } } },
                Paragraph("AFTER_TABLE")
            }
        });
    }

    private static async Task SelectHeaderMarker(IPage page, string marker)
    {
        await Search(page, marker, "/ 1 eşleşme"); await page.Locator("#editor-search-next").ClickAsync();
        await page.WaitForFunctionAsync("text=>getSelection().toString()===text", marker);
    }

    [LocalEditorAdapterFact(Timeout = 120000)]
    public async Task Existing_first_row_toggle_preserves_formatted_cells_history_save_retry_and_readonly_locks()
    {
        var source = HeaderRowDocument(); await using var fixture = await BrowserFixture.CreateAsync(source);
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var textbox = page.Locator("#rich-text [role=textbox]"); var json = page.Locator("#document-json");
        var command = Command(page, "toggleTableHeaderRow"); var table = textbox.Locator("table").First;
        Assert.Equal(source, await json.InputValueAsync()); await Assertions.Expect(command).ToBeDisabledAsync();
        await SelectHeaderMarker(page, "HRT_003"); await Assertions.Expect(command).ToBeEnabledAsync();
        await Assertions.Expect(command).ToHaveAttributeAsync("aria-pressed", "false");
        var cellContent = await table.Locator("tr").First.Locator("th,td").EvaluateAllAsync<string[]>("nodes=>nodes.map(node=>node.innerHTML)");
        var bodyRows = await table.Locator("tr").EvaluateAllAsync<string[]>("rows=>rows.slice(1).map(row=>row.innerHTML)");
        var otherTable = await textbox.Locator("table").Last.InnerHTMLAsync();
        await command.ClickAsync();
        Assert.Equal(3, await table.Locator("tr").First.Locator("th").CountAsync());
        await Assertions.Expect(command).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal("HRT_003", await page.EvaluateAsync<string>("()=>getSelection().toString()"));
        Assert.Equal(cellContent, await table.Locator("tr").First.Locator("th,td").EvaluateAllAsync<string[]>("nodes=>nodes.map(node=>node.innerHTML)"));
        Assert.Equal(bodyRows, await table.Locator("tr").EvaluateAllAsync<string[]>("rows=>rows.slice(1).map(row=>row.innerHTML)"));
        Assert.Equal(otherTable, await textbox.Locator("table").Last.InnerHTMLAsync());
        var headed = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV10.TryRead(headed, out _));
        await command.ClickAsync(); Assert.Equal(0, await table.Locator("tr").First.Locator("th").CountAsync());
        var ordinary = await json.InputValueAsync();
        await Command(page, "undo").ClickAsync(); Assert.Equal(headed, await json.InputValueAsync());
        await Command(page, "undo").ClickAsync(); Assert.Equal(2, await table.Locator("tr").First.Locator("th").CountAsync());
        await Command(page, "redo").ClickAsync(); Assert.Equal(headed, await json.InputValueAsync());
        await Command(page, "redo").ClickAsync(); Assert.Equal(ordinary, await json.InputValueAsync());
        // Typing after the action has its own history step.
        await page.Keyboard.InsertTextAsync("REPLACED_ROW_MARKER"); await Command(page, "undo").ClickAsync();
        Assert.Equal(ordinary, await json.InputValueAsync());
        await textbox.ClickAsync(); await page.Keyboard.PressAsync("Control+a");
        await Assertions.Expect(command).ToBeDisabledAsync(); await command.DispatchEventAsync("click"); Assert.Equal(ordinary, await json.InputValueAsync());
        await SelectHeaderMarker(page, "HRT_003"); await command.ClickAsync(); Assert.Equal(headed, await json.InputValueAsync());
        var retry = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Assertions.Expect(command).ToBeDisabledAsync();
        await command.DispatchEventAsync("click"); Assert.Equal(headed, await json.InputValueAsync());
        retry.Release.SetResult(503); await Assertions.Expect(command).ToBeEnabledAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(save.Body); save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(headed, await json.InputValueAsync()); await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        await SelectHeaderMarker(page, "HRT_003"); await Assertions.Expect(command).ToHaveAttributeAsync("aria-pressed", "true");
        var review = fixture.Enqueue(); await page.Locator(".review-form button").ClickAsync(); await review.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(command).ToBeDisabledAsync(); review.Release.SetResult(503); await Assertions.Expect(command).ToBeEnabledAsync();
        Assert.Equal(headed, await json.InputValueAsync());
        var conflict = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await conflict.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        conflict.Release.SetResult(409); await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
        await AssertFrozenAsync(page); await command.DispatchEventAsync("click"); Assert.Equal(headed, await json.InputValueAsync());
        fixture.Editable = false; await fixture.OpenAsync(); await SelectHeaderMarker(page, "HRT_003");
        await Assertions.Expect(command).ToBeDisabledAsync(); await Assertions.Expect(command).ToHaveAttributeAsync("aria-pressed", "true");
        await command.DispatchEventAsync("click"); Assert.Equal(headed, await json.InputValueAsync());
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact(Timeout = 180000)]
    public async Task Existing_header_changes_reflow_live_pages_and_repeat_only_enabled_first_rows_in_pdf()
    {
        await using var fixture = await BrowserFixture.CreateAsync(HeaderRowDocument(42));
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var json = page.Locator("#document-json"); var command = Command(page, "toggleTableHeaderRow");
        await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/table-header")); Directory.CreateDirectory(directory);
        foreach (var mode in new[] { "mixed", "heading", "ordinary" })
        {
            if (mode != "mixed") { await SelectHeaderMarker(page, "HRT_030"); await command.ClickAsync(); }
            await Assertions.Expect(command).ToHaveAttributeAsync("aria-pressed", (mode == "heading").ToString().ToLowerInvariant());
            await page.EvaluateAsync("()=>document.fonts.ready.then(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))");
            await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
            var source = await json.InputValueAsync();
            var markers = await page.EvaluateAsync<string>("""
                ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                    const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/HRT_\d{3}/g)){
                        const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                    return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, mode + "-markers.json"), markers);
            using (var renderer = new ChromiumDraftPdfRenderer())
            {
                var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, source, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 }, CancellationToken.None);
                using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!), pdf.PageCount); Assert.True(pdf.PageCount > 1);
                await File.WriteAllBytesAsync(Path.Combine(directory, mode + ".pdf"), bytes);
            }
            if (mode == "heading")
            {
                await page.Locator("#editor-search").EvaluateAsync("e=>e.open=false");
                await page.Locator("#editor-focus-toggle").ClickAsync(); await fixture.ScreenshotAsync("existing-table-header.png"); await page.Keyboard.PressAsync("Escape");
            }
        }
        var ordinary = await json.InputValueAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(save.Body); save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(ordinary, await json.InputValueAsync());
        await SelectHeaderMarker(page, "HRT_030"); await Assertions.Expect(command).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.Equal(0, await page.Locator("#rich-text table").First.Locator("tr").First.Locator("th").CountAsync());
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
}
