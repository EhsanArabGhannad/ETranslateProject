using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static async Task SelectImageCaret(IPage page, int paragraph, int offset = 0)
    {
        await page.Locator("#rich-text [role=textbox] > p").Nth(paragraph).EvaluateAsync("(p,offset)=>{p.closest('[contenteditable]').focus();const r=document.createRange();r.setStart(p.firstChild,offset);r.collapse(true);const s=getSelection();s.removeAllRanges();s.addRange(r);document.dispatchEvent(new Event('selectionchange'));}", offset);
    }

    [LocalEditorAdapterFact]
    public async Task Private_image_upload_preserves_anchor_text_history_layout_retry_save_and_readonly_locks()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "8";
        var page = fixture.Page; await fixture.OpenAsync();
        var json = page.Locator("#document-json"); var image = page.Locator("#rich-text img");
        await page.Locator("#editor-images").EvaluateAsync("e=>e.open=true"); await SelectImageCaret(page, 1, 8);
        await page.Locator("#editor-image-file").SetInputFilesAsync(new FilePayload { Name = "blocked.svg", MimeType = "image/svg+xml", Buffer = "<svg/>"u8.ToArray() });
        await page.Locator("#editor-image-upload").ClickAsync(); await Assertions.Expect(page.Locator("#editor-image-status")).ToContainTextAsync("PNG"); Assert.Equal(Initial, await json.InputValueAsync());
        await page.Locator("#editor-image-file").SetInputFilesAsync(new FilePayload { Name = "synthetic.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
        var retry = fixture.Enqueue(); await page.Locator("#editor-image-upload").ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertFrozenAsync(page); await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
        Assert.Contains("synthetic-token", retry.Body); Assert.Contains("expectedRevision", retry.Body);
        retry.Release.SetResult(503); await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true"); Assert.Equal(Initial, await json.InputValueAsync());
        var upload = fixture.Enqueue(); await page.Locator("#editor-image-upload").ClickAsync(); await upload.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Readonly navigation while waiting must not move the captured insertion point.
        await SelectImageCaret(page, 0, 3); upload.Release.SetResult(200);
        await Assertions.Expect(image).ToHaveCountAsync(1); await Assertions.Expect(page.Locator("#editor-image-status")).ToContainTextAsync("eklendi");
        var inserted = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV10.TryRead(inserted, out var plain)); Assert.Contains("İstanbul", plain); Assert.Contains("şĞı", plain);
        using (var body = JsonDocument.Parse(inserted)) { Assert.Equal("image", body.RootElement.GetProperty("content")[2].GetProperty("type").GetString()); Assert.Equal("document", body.RootElement.GetProperty("content")[2].GetProperty("attrs").GetProperty("assetScope").GetString()); }
        await Assertions.Expect(image).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("/Workspace/BodyImage\\?"));
        await page.Locator("#editor-image-width").FillAsync("75"); await page.Locator("#editor-image-align").SelectOptionAsync("right"); await page.Locator("#editor-image-apply").ClickAsync();
        Assert.Equal("75%", await image.EvaluateAsync<string>("e=>e.style.width")); Assert.Equal("0px", await image.EvaluateAsync<string>("e=>e.style.marginRight"));
        Assert.True(await image.EvaluateAsync<bool>("e=>{const p=e.closest('[role=textbox]'),r=p.getBoundingClientRect(),s=getComputedStyle(p);return getComputedStyle(e).display==='block' && Math.abs(e.getBoundingClientRect().right-(r.right-parseFloat(s.paddingRight)-parseFloat(s.borderRightWidth)))<2;}"));
        var resized = await json.InputValueAsync(); await Command(page, "undo").ClickAsync(); Assert.Equal(inserted, await json.InputValueAsync()); await Command(page, "redo").ClickAsync(); Assert.Equal(resized, await json.InputValueAsync());
        await page.Locator("#editor-image-width").FillAsync("101"); await Assertions.Expect(page.Locator("#editor-image-apply")).ToBeDisabledAsync(); Assert.Equal(resized, await json.InputValueAsync());
        await Command(page, "deleteImage").ClickAsync(); await Assertions.Expect(image).ToHaveCountAsync(0); await Command(page, "undo").ClickAsync(); Assert.Equal(resized, await json.InputValueAsync());
        await page.Locator("#preview-body img").EvaluateAsync("async e=>{await e.decode();window.qaPreviewImage=e;}"); await image.EvaluateAsync("e=>e.decode()");
        var requests = fixture.Responses.Count(response => response.Contains("/Workspace/BodyImage?"));
        await SelectImageCaret(page, 0, 3); await page.Keyboard.InsertTextAsync("CACHE_EDIT");
        Assert.True(await page.Locator("#preview-body img").EvaluateAsync<bool>("e=>e===window.qaPreviewImage"));
        await Command(page, "undo").ClickAsync(); Assert.Equal(resized, await json.InputValueAsync()); Assert.Equal(requests, fixture.Responses.Count(response => response.Contains("/Workspace/BodyImage?")));
        await image.ClickAsync();
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); await AssertFrozenAsync(page);
        fixture.Source = SubmittedJson(save.Body); fixture.SchemaVersion = "10"; save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(resized, await json.InputValueAsync()); await image.ClickAsync();
        await Assertions.Expect(page.Locator("#editor-image-width")).ToHaveValueAsync("75"); await Assertions.Expect(page.Locator("#editor-image-align")).ToHaveValueAsync("right");
        await page.SetViewportSizeAsync(1440, 1200); await page.Locator("#editor-images").EvaluateAsync("e=>e.open=true"); await page.Locator("#editor-images").ScrollIntoViewIfNeededAsync();
        await fixture.ScreenshotAsync("body-images.png");
        fixture.Editable = false; await fixture.OpenAsync(); await page.Locator("#editor-images").EvaluateAsync("e=>e.open=true"); await image.ClickAsync();
        await Assertions.Expect(page.Locator("#editor-image-file")).ToBeDisabledAsync(); await Assertions.Expect(page.Locator("#editor-image-apply")).ToBeDisabledAsync(); await Assertions.Expect(Command(page, "deleteImage")).ToBeDisabledAsync();
        await page.Locator("#editor-image-apply").EvaluateAsync("e=>{e.disabled=false;e.click()}"); Assert.Equal(resized, await json.InputValueAsync());
        fixture.Editable = true; await fixture.OpenAsync(); await page.Locator("#editor-images").EvaluateAsync("e=>e.open=true"); await SelectImageCaret(page, 0, 3);
        await page.Locator("#editor-image-file").SetInputFilesAsync(new FilePayload { Name = "conflict.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
        var conflict = fixture.Enqueue(); await page.Locator("#editor-image-upload").ClickAsync(); await conflict.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); conflict.Release.SetResult(409);
        await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false"); await AssertFrozenAsync(page); Assert.Equal(resized, await json.InputValueAsync());
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    private static string BodyImagePrintDocument(bool table)
    {
        object P(int index) => new { type = "paragraph", content = new[] { new { type = "text", text = $"BI_{index:D3} İstanbul ترجمه آزمایشی. " + string.Concat(Enumerable.Repeat("Sentetik metin ve çeviri. ", 10)) } } };
        object Image(int width, string align) => new { type = "image", attrs = new { assetId = BrowserFixture.ImageId, assetScope = "document", widthPercent = width, align } };
        var blocks = new List<object> { Image(25, "left") }; blocks.AddRange(Enumerable.Range(0, 10).Select(P)); blocks.Add(Image(50, "center"));
        blocks.AddRange(Enumerable.Range(10, 10).Select(P)); blocks.Add(Image(75, "right"));
        if (table) blocks.Add(new { type = "table", content = Enumerable.Range(20, 10).Select(i => new { type = "tableRow", content = new object[] { new { type = "tableCell", content = new[] { P(i) } }, new { type = "tableCell", content = new[] { new { type = "paragraph" } } } } }).ToArray() });
        else blocks.AddRange(Enumerable.Range(20, 10).Select(P));
        return JsonSerializer.Serialize(new { type = "doc", content = blocks });
    }

    [LocalEditorAdapterFact]
    public async Task Private_body_image_taller_than_page_or_missing_falls_back_without_rewriting_content()
    {
        var source = JsonSerializer.Serialize(new { type = "doc", content = new object[] { new { type = "image", attrs = new { assetId = BrowserFixture.ImageId, assetScope = "document", widthPercent = 100, align = "center" } }, new { type = "paragraph" } } });
        var layout = (DocumentPageLayout.Default with { Orientation = "Landscape" }).ToJson();
        foreach (var available in new[] { true, false })
        {
            await using var fixture = await BrowserFixture.CreateAsync(source); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10"; fixture.Layout = layout; fixture.BodyImageBytes = BrowserFixture.ImageBytes; fixture.ServeImage = available;
            await fixture.OpenAsync(); await fixture.Page.Locator("#editor-pages-toggle").ClickAsync();
            await Assertions.Expect(fixture.Page.Locator("#editor-pagination-status")).ToContainTextAsync("sürekli");
            Assert.Equal(source, await fixture.Page.Locator("#document-json").InputValueAsync());
            await Assertions.Expect(fixture.Page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
        }
        using var renderer = new ChromiumDraftPdfRenderer();
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, source, null, null, layout, null, new Dictionary<Guid, PdfImage> { [BrowserFixture.ImageId] = new("image/png", BrowserFixture.ImageBytes) }) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 };
        var error = await Assert.ThrowsAsync<PdfRenderValidationException>(() => renderer.RenderAsync(input, CancellationToken.None)); Assert.Contains("taller", error.Message);
    }

    [LocalEditorAdapterFact]
    public async Task Private_body_image_pages_dimensions_alignment_and_aspect_match_pdf_with_and_without_tables()
    {
        foreach (var table in new[] { false, true })
        {
            var source = BodyImagePrintDocument(table); await using var fixture = await BrowserFixture.CreateAsync(source); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10"; fixture.Layout = DocumentPageLayout.Default.ToJson();
            var page = fixture.Page; await fixture.OpenAsync(); await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
            var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/body-images")); Directory.CreateDirectory(directory);
            var map = await page.EvaluateAsync<string>("""
                ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect(),markers={};
                    const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/BI_\d{3}/g)){
                        const r=document.createRange();r.setStart(node,match.index);r.setEnd(node,match.index+match[0].length);const rect=[...r.getClientRects()].find(r=>r.width>0);markers[match[0]]=Math.floor(((rect.left-origin.left)/scale+1)/pitch)+1;}
                    const images=[...root.querySelectorAll('img')].map(image=>{const r=image.getBoundingClientRect(),x=(r.left-origin.left)/scale,page=Math.floor((x+1)/pitch)+1;return {page,x:x-(page-1)*pitch,y:(r.top-origin.top)/scale,width:r.width/scale,height:r.height/scale};});
                    return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),contentWidth:parseFloat(root.style.width),markers,images});}
                """);
            var name = table ? "with-table" : "text-only"; await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), map);
            using var renderer = new ChromiumDraftPdfRenderer();
            var input = new DraftPdfInput(Guid.NewGuid(), 1, null, source, null, null, fixture.Layout, null, new Dictionary<Guid, PdfImage> { [BrowserFixture.ImageId] = new("image/png", BodyImageFixture.Png) }) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 };
            var bytes = await renderer.RenderAsync(input, CancellationToken.None); await File.WriteAllBytesAsync(Path.Combine(directory, name + ".pdf"), bytes);
            using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import); using var view = JsonDocument.Parse(map); Assert.Equal(view.RootElement.GetProperty("pageCount").GetInt32(), pdf.PageCount);
            Assert.Equal(source, await page.Locator("#document-json").InputValueAsync()); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }
}
