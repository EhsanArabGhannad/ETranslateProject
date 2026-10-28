using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string TableDocument(bool tall = false) {
        object P(string text) => new { type="paragraph",attrs=new{dir="ltr"},content=new[]{new{type="text",text}}};
        object Cell(string text) => new{type="tableCell",content=new[]{P(text)}};
        var rows=Enumerable.Range(0,tall?1:47).Select(i=>new{type="tableRow",content=new[]{Cell(i==0?"Sıra":$"MARK_{i:000}"),
            Cell(i==0&&!tall?"Sentetik açıklama":string.Concat(Enumerable.Repeat("İstanbul - جدول ساختگی / Türkçe. ",tall?400:3)))}}).ToArray();
        return JsonSerializer.Serialize(new{type="doc",content=new object[]{new{type="table",content=rows},P("END_MARKER")}});
    }

    [LocalEditorAdapterFact(Timeout=90000)]
    public async Task Production_pagination_matches_the_new_pdf_and_preserves_save_reload_and_locks()
    {
        var source=TableDocument();await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;
        await page.SetViewportSizeAsync(1440,1100);await fixture.OpenAsync();
        var textbox=page.Locator("#rich-text [role=textbox]");var viewport=page.Locator("#editor-viewport");
        await textbox.EvaluateAsync("e=>window.originalPagedEditor=e");
        await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination","paged");
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        Assert.Equal("CLEAN",await page.Locator("#save-state").InnerTextAsync());
        var count=int.Parse((await viewport.GetAttributeAsync("data-page-count"))!);Assert.True(count>1);
        using(var renderer=new ChromiumDraftPdfRenderer()) {
            var bytes=await renderer.RenderAsync(new(Guid.NewGuid(),1,null,source,null,null,null,null,new Dictionary<Guid,PdfImage>()),CancellationToken.None);
            using var stream=new MemoryStream(bytes);using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);Assert.Equal(count,pdf.PageCount);
            var directory=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!,"../../../tmp/editor-adapter"));Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory,"product-pagination-v11.pdf"),bytes);
            var markerPages=await page.EvaluateAsync<string>("""
                ()=>{const root=document.querySelector('#rich-text [role=textbox]'),world=document.querySelector('#editor-paper'),scale=parseFloat(world.style.transform.slice(6)),
                    pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                    const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
                    for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/MARK_\d{3}|END_MARKER/g)){
                        const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);
                        const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;
                    }return JSON.stringify(result);}
                """);
            await File.WriteAllTextAsync(Path.Combine(directory,"product-pagination-markers.json"),
                JsonSerializer.Serialize(new { pageCount=count, markers=JsonSerializer.Deserialize<Dictionary<string,int>>(markerPages) }));
        }
        await page.Locator("#editor-page-index").SelectOptionAsync("2");
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-page-index","2");
        var marker=await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6));
                const pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left;
                return [...root.querySelectorAll('td p')].find(p=>p.textContent.startsWith('MARK_')&&Math.floor(((p.getBoundingClientRect().left-origin)/scale+1)/pitch)+1===2).textContent;}
            """);
        await textbox.Locator("td p").Filter(new(){HasText=marker}).First.ClickAsync();await page.Keyboard.PressAsync("End");await page.Keyboard.InsertTextAsync(" PAGE_EDIT");
        await Assertions.Expect(page.Locator("#document-json")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("PAGE_EDIT"));
        var edited=await page.Locator("#document-json").InputValueAsync();Assert.True(DocumentContentDescriptor.RichV5.TryRead(edited,out _));
        foreach(var zoom in new[]{"75","125","fit"}){
            await page.Locator("#editor-zoom").SelectOptionAsync(zoom);
            await Assertions.Expect(viewport).ToHaveAttributeAsync("data-page-index","2");
            await page.WaitForFunctionAsync("() => {const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),viewport=document.querySelector('#editor-viewport');return Math.abs(viewport.scrollLeft-(parseFloat(root.style.width)+parseFloat(root.style.columnGap))*scale)<2;}");
            Assert.Equal(edited,await page.Locator("#document-json").InputValueAsync());
        }
        await page.Locator("#editor-focus-toggle").ClickAsync();await page.Keyboard.PressAsync("Escape");
        await page.Locator("#editor-pages-toggle").ClickAsync();await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination","paged");
        Assert.True(await textbox.EvaluateAsync<bool>("e=>e===window.originalPagedEditor"));
        await Command(page,"undo").ClickAsync();await Assertions.Expect(textbox).Not.ToContainTextAsync("PAGE_EDIT");
        await Command(page,"redo").ClickAsync();await Assertions.Expect(textbox).ToContainTextAsync("PAGE_EDIT");
        var retry=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertFrozenAsync(page);await page.Locator("#editor-page-index").SelectOptionAsync("2");await AssertFrozenAsync(page);
        Assert.Equal(edited,SubmittedJson(retry.Body));retry.Release.SetResult(503);await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable","true");
        var save=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source=SubmittedJson(save.Body);fixture.Format=DocumentContentDescriptor.RichFormat;fixture.SchemaVersion="5";save.Release.SetResult(303);await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(edited,await page.Locator("#document-json").InputValueAsync());await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination","paged");
        await fixture.ScreenshotAsync("product-pagination.png");
        fixture.Editable=false;await fixture.OpenAsync();await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination","paged");await page.Locator("#editor-page-index").SelectOptionAsync("2");await AssertFrozenAsync(page);
        Assert.Equal(edited,await page.Locator("#document-json").InputValueAsync());Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Unsupported_pagination_falls_back_without_deleting_or_unlocking_content()
    {
        var source=TableDocument(true);await using var fixture=await BrowserFixture.CreateAsync(source,editable:false);var page=fixture.Page;
        await fixture.OpenAsync();await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-pagination-status")).ToHaveTextAsync(ETranslate.Web.Localization.UiText.Get("EditorPagesUnavailable"));
        await AssertFrozenAsync(page);Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        fixture.Source=Initial;fixture.Header="""{"type":"header","content":[],"unsupported":"keep"}""";await fixture.OpenAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-pages-toggle")).ToHaveAttributeAsync("aria-pressed","false");
        Assert.Equal(Initial,await page.Locator("#document-json").InputValueAsync());await AssertFrozenAsync(page);Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Paginated_template_regions_follow_last_page_footer_policy_without_touching_body()
    {
        var source=TableDocument();await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;
        fixture.Header="""{"type":"header","content":[{"type":"paragraph","attrs":{"textAlign":"right"},"content":[{"type":"text","text":"SENTETİK ÜSTBİLGİ"}]}]}""";
        fixture.Footer="""{"type":"footer","content":[{"type":"paragraph","content":[{"type":"text","text":"SENTETİK SON ALTBİLGİ"}]}]}""";
        fixture.Layout=(DocumentPageLayout.Default with {FooterPlacement="LastPage"}).ToJson();
        await fixture.OpenAsync();await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        var count=int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!);
        Assert.Equal(count,await page.Locator("#editor-page-sheets .pagination-header").CountAsync());
        Assert.Equal(1,await page.Locator("#editor-page-sheets .pagination-footer").CountAsync());
        Assert.Equal(1,await page.Locator(".editor-page-sheet").Last.Locator(".pagination-footer").CountAsync());
        Assert.Equal("right",await page.Locator("#editor-page-sheets .pagination-header p").First.EvaluateAsync<string>("e=>e.style.textAlign"));
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-page-index").SelectOptionAsync(count.ToString());
        await page.Locator("#editor-viewport").ScrollIntoViewIfNeededAsync();await fixture.ScreenshotAsync("product-pagination-template.png");
        await page.Locator("#editor-viewport").EvaluateAsync("e=>e.scrollTop=e.scrollHeight");
        await fixture.ScreenshotAsync("product-pagination-template-footer.png");
        await page.SetViewportSizeAsync(390,844);await Assertions.Expect(page.Locator("#editor-zoom-status")).Not.ToBeEmptyAsync();await AssertNoOuterOverflowAsync(page);
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Pagination_measures_private_images_and_falls_back_when_a_template_image_is_missing()
    {
        var body=System.Text.Json.Nodes.JsonNode.Parse(TableDocument())!;
        body["content"]!.AsArray().Insert(0,System.Text.Json.Nodes.JsonNode.Parse(
            """{"type":"image","attrs":{"assetId":"$IMAGE$"}}""".Replace("$IMAGE$",BrowserFixture.ImageId.ToString())));
        var source=body.ToJsonString();await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;
        fixture.Header="""{"type":"header","content":[{"type":"image","attrs":{"assetId":"$IMAGE$","layout":"banner","align":"right"}}]}""".Replace("$IMAGE$",BrowserFixture.ImageId.ToString());
        await fixture.OpenAsync();await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        var count=int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!);
        Assert.Equal(count,await page.Locator("#editor-page-sheets img.template-banner.image-right").CountAsync());
        foreach(var zoom in new[]{"75","125","fit"}) {
            await page.Locator("#editor-zoom").SelectOptionAsync(zoom);
            await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-page-count",count.ToString());
        }
        using(var renderer=new ChromiumDraftPdfRenderer()) {
            var bytes=await renderer.RenderAsync(new(Guid.NewGuid(),1,null,source,fixture.Header,null,null,null,
                new Dictionary<Guid,PdfImage>{{BrowserFixture.ImageId,new("image/png",BrowserFixture.ImageBytes)}}),CancellationToken.None);
            using var stream=new MemoryStream(bytes);using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);Assert.Equal(count,pdf.PageCount);
        }
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        fixture.Source=Initial;fixture.Editable=false;fixture.ServeImage=false;await fixture.OpenAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-pagination-status")).ToHaveTextAsync(ETranslate.Web.Localization.UiText.Get("EditorPagesUnavailable"));
        Assert.Equal(Initial,await page.Locator("#document-json").InputValueAsync());await AssertFrozenAsync(page);
        Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
}
