using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static async Task PageBreakCaret(IPage page, int index, int offset)
    {
        await page.Locator("#rich-text [role=textbox]").FocusAsync();
        await page.Locator("#rich-text [role=textbox] > p,#rich-text [role=textbox] > h2").Nth(index).EvaluateAsync("""
            (p,offset)=>{const walker=document.createTreeWalker(p,NodeFilter.SHOW_TEXT);let node=walker.nextNode();while(node&&offset>node.data.length){offset-=node.data.length;node=walker.nextNode();}
                const range=document.createRange();range.setStart(node??p,node?offset:0);range.collapse(true);const s=getSelection();s.removeAllRanges();s.addRange(range);}
            """, offset);
    }
    private static int Starts(string json) { using var doc = JsonDocument.Parse(json); return DocumentPageBreak.Count(doc.RootElement); }

    [LocalEditorAdapterFact]
    public async Task Manual_page_break_commands_keyboard_history_save_reload_and_locks_preserve_content()
    {
        await using var fixture = await BrowserFixture.CreateAsync(MergeDocument()); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "9";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
        var json = page.Locator("#document-json"); var textbox = page.Locator("#rich-text [role=textbox]");
        await page.Locator("#editor-page-breaks").EvaluateAsync("e=>e.open=true");
        await PageBreakCaret(page, 0, 0); await Assertions.Expect(Command(page, "insertPageBreak")).ToBeDisabledAsync(); await page.Keyboard.PressAsync("Control+Enter"); Assert.Equal(fixture.Source, await json.InputValueAsync());
        await PageBreakCaret(page, 0, 7); await Assertions.Expect(Command(page, "insertPageBreak")).ToBeEnabledAsync(); await page.Keyboard.PressAsync("Control+Enter");
        var split = await json.InputValueAsync(); Assert.Equal(1, Starts(split)); Assert.True(DocumentContentDescriptor.RichV10.TryRead(split, out var plain)); Assert.Contains("MERGE", plain);
        using (var body = JsonDocument.Parse(split)) {
            var next = body.RootElement.GetProperty("content")[1]; Assert.True(next.GetProperty("attrs").GetProperty("pageBreakBefore").GetBoolean());
            Assert.Equal("serif", next.GetProperty("content")[0].GetProperty("marks").EnumerateArray().Single(mark => mark.GetProperty("type").GetString() == "typography").GetProperty("attrs").GetProperty("fontFamily").GetString());
        }
        await Assertions.Expect(textbox.Locator("[data-et-page-start]")).ToHaveCountAsync(1);
        await Assertions.Expect(Command(page, "bulletList")).ToBeDisabledAsync();
        await Command(page, "undo").ClickAsync(); Assert.Equal(0, Starts(await json.InputValueAsync())); await Command(page, "redo").ClickAsync(); Assert.Equal(split, await json.InputValueAsync());
        // Ordinary Enter must not copy the forced page start to the next paragraph.
        await PageBreakCaret(page, 1, 2); await page.Keyboard.PressAsync("Enter"); Assert.Equal(1, Starts(await json.InputValueAsync())); await Command(page, "undo").ClickAsync(); Assert.Equal(split, await json.InputValueAsync());
        await PageBreakCaret(page, 1, 0); await Assertions.Expect(Command(page, "removePageBreak")).ToBeEnabledAsync(); await page.Keyboard.PressAsync("Backspace"); Assert.Equal(0, Starts(await json.InputValueAsync()));
        await Command(page, "undo").ClickAsync(); Assert.Equal(split, await json.InputValueAsync()); await Command(page, "removePageBreak").ClickAsync(); Assert.Equal(0, Starts(await json.InputValueAsync())); await Command(page, "undo").ClickAsync(); Assert.Equal(split, await json.InputValueAsync());
        await PageBreakCaret(page, 1, 0); await page.Locator("#editor-images").EvaluateAsync("e=>e.open=true");
        await page.Locator("#editor-image-file").SetInputFilesAsync(new FilePayload { Name = "page-start.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
        var imageUpload = fixture.Enqueue(); await page.Locator("#editor-image-upload").ClickAsync(); await imageUpload.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); imageUpload.Release.SetResult(200); await Assertions.Expect(textbox.Locator("img")).ToHaveCountAsync(1);
        Assert.Equal(1, Starts(await json.InputValueAsync())); await Command(page, "undo").ClickAsync(); Assert.Equal(split, await json.InputValueAsync());
        await textbox.Locator("td p").First.ClickAsync(); await Assertions.Expect(Command(page, "insertPageBreak")).ToBeDisabledAsync(); await page.Keyboard.PressAsync("Control+Enter"); Assert.Equal(split, await json.InputValueAsync());
        var retry = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); await AssertFrozenAsync(page);
        await Command(page, "removePageBreak").DispatchEventAsync("click"); Assert.Equal(split, await json.InputValueAsync()); retry.Release.SetResult(503); await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true");
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "10"; save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(split, await json.InputValueAsync());
        await page.Locator("#editor-page-breaks").EvaluateAsync("e=>e.open=true"); await PageBreakCaret(page, 1, 2); await Assertions.Expect(Command(page, "removePageBreak")).ToBeEnabledAsync();
        await fixture.ScreenshotAsync("manual-page-breaks.png");
        fixture.Editable = false; await fixture.OpenAsync(); await page.Locator("#editor-page-breaks").EvaluateAsync("e=>e.open=true"); await PageBreakCaret(page, 1, 0);
        await Assertions.Expect(Command(page, "removePageBreak")).ToBeDisabledAsync(); await Command(page, "removePageBreak").EvaluateAsync("e=>{e.disabled=false;e.click()}"); await page.Keyboard.PressAsync("Control+Enter"); Assert.Equal(split, await json.InputValueAsync());
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    private static object BreakP(string text, bool start = false) => new { type = "paragraph", attrs = new { dir = text.StartsWith("PB_007") ? "rtl" : "ltr", pageBreakBefore = start }, content = text.Length == 0 ? Array.Empty<object>() : new object[] { new { type = "text", text } } };
    private static string BreakPrintDocument(string name)
    {
        if (name == "trailing-empty") return JsonSerializer.Serialize(new { type = "doc", content = new[] { BreakP("PB_TAIL_000"), BreakP("", true) } });
        if (name == "first-and-empty") return JsonSerializer.Serialize(new { type = "doc", content = new[] { BreakP("PB_EDGE_000", true), BreakP("", true), BreakP("PB_EDGE_001", true) } });
        if (name == "limit") return JsonSerializer.Serialize(new { type = "doc", content = Enumerable.Range(0, 50).Select(i => BreakP($"PB_LIMIT_{i:D3}", i > 0)).ToArray() });
        var blocks = new List<object>();
        for (var i = 0; i < 12; i++) blocks.Add(BreakP($"PB_{i:D3} İstanbul ترجمه. " + string.Concat(Enumerable.Repeat("Sentetik metin ve çeviri. ", 6)), i is 2 or 7));
        if (name == "table-image") {
            blocks.Add(new { type = "image", attrs = new { assetId = BrowserFixture.ImageId, assetScope = "document", widthPercent = 25, align = "right" } });
            blocks.Add(BreakP("PB_TABLE_BEFORE", true));
            blocks.Add(new { type = "table", content = Enumerable.Range(0, 20).Select(i => new { type = "tableRow", content = new[] { new { type = i == 0 ? "tableHeader" : "tableCell", content = new[] { BreakP($"PB_ROW_{i:D3} " + new string('x', 140)) } }, new { type = i == 0 ? "tableHeader" : "tableCell", content = new[] { BreakP("جدول ساختگی") } } } }).ToArray() });
            blocks.Add(BreakP("PB_AFTER_TABLE", true));
        }
        return JsonSerializer.Serialize(new { type = "doc", content = blocks });
    }

    [LocalEditorAdapterFact(Timeout = 180000)]
    public async Task Manual_starts_match_native_pages_pdf_empty_last_page_footer_and_fifty_page_limit()
    {
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/page-breaks")); Directory.CreateDirectory(directory);
        foreach (var name in new[] { "plain", "table-image", "trailing-empty", "first-and-empty", "limit" })
        {
            var body = BreakPrintDocument(name); await using var fixture = await BrowserFixture.CreateAsync(body); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            var layout = DocumentPageLayout.Default with { FooterPlacement = "LastPage" }; fixture.Layout = layout.ToJson();
            fixture.Header = JsonSerializer.Serialize(new { type = "header", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "PB_HEADER" } } } } });
            fixture.Footer = JsonSerializer.Serialize(new { type = "footer", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "PB_LAST_FOOTER" } } } } });
            var page = fixture.Page; await fixture.OpenAsync(); await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
            var map = await page.EvaluateAsync<string>("""
                ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,markers={};
                    const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/PB_(?:\d{3}|ROW_\d{3}|TABLE_BEFORE|AFTER_TABLE|TAIL_\d{3}|EDGE_\d{3}|LIMIT_\d{3})/g)){
                        const r=document.createRange();r.setStart(node,match.index);r.setEnd(node,match.index+match[0].length);const rect=[...r.getClientRects()].find(r=>r.width>0);markers[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                    const starts=[...root.querySelectorAll('[data-et-page-start]')].map(p=>{const r=p.getClientRects()[0];return Math.floor(((r.left-origin)/scale+1)/pitch)+1;});
                    return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers,starts});}
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), map);
            using var renderer = new ChromiumDraftPdfRenderer(); var images = new Dictionary<Guid, PdfImage>(); if (name == "table-image") images.Add(BrowserFixture.ImageId, new("image/png", BodyImageFixture.Png));
            var input = new DraftPdfInput(Guid.NewGuid(), 1, null, body, fixture.Header, fixture.Footer, fixture.Layout, null, images) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 };
            var bytes = await renderer.RenderAsync(input, CancellationToken.None); await File.WriteAllBytesAsync(Path.Combine(directory, name + ".pdf"), bytes);
            using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import); using var measured = JsonDocument.Parse(map); Assert.Equal(measured.RootElement.GetProperty("pageCount").GetInt32(), pdf.PageCount);
            if (name == "trailing-empty") Assert.Equal(2, pdf.PageCount); if (name == "first-and-empty") Assert.Equal(3, pdf.PageCount); if (name == "limit") { Assert.Equal(50, pdf.PageCount); await PageBreakCaret(page, 1, 2); await Assertions.Expect(Command(page, "insertPageBreak")).ToBeDisabledAsync(); }
            Assert.Equal(body, await page.Locator("#document-json").InputValueAsync()); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }
}
