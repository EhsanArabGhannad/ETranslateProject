using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string SpacingDocument()
    {
        object P(string text,string dir="ltr",string? color=null)=>new{type="paragraph",attrs=new{dir,lineHeight=1.5,textAlign=dir=="rtl"?"right":"left",indentLevel=0},content=new[]{new{type="text",text,marks=color is null?Array.Empty<object>():[new{type="typography",attrs=new{color}}]}}};
        return JsonSerializer.Serialize(new{type="doc",content=new object[]{
            P("START_MARK","ltr","#15803d"),P("LTR_FIRST "+string.Concat(Enumerable.Repeat("Synthetic translation line with several words for measurement. ",5)),"ltr","#b91c1c"),
            P("AFTER_MARK","ltr","#0f766e"),P("RTL_FIRST "+string.Concat(Enumerable.Repeat("ترجمهٔ آزمایشی برای بررسی تورفتگی خط اول و فاصلهٔ پاراگراف. ",5)),"rtl","#1d4ed8"),
            new{type="table",content=new[]{new{type="tableRow",content=new[]{new{type="tableCell",content=new[]{P("TABLE_SPACING "+string.Concat(Enumerable.Repeat("Cell paragraph with separate spacing and indentation. ",4)),"ltr","#7e22ce")}}}}}},
            new{type="blockquote",content=Enumerable.Range(1,40).Select(index=>P($"FLOW_{index:000} "+string.Concat(Enumerable.Repeat("İstanbul çeviri metni ve paragraf aralıkları. ",4)))).ToArray()}
        }});
    }
    [LocalEditorAdapterFact(Timeout=120000)]
    public async Task Paragraph_spacing_and_first_line_indent_survive_history_search_pagination_pdf_and_locks()
    {
        var source=SpacingDocument();await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;
        fixture.Format=DocumentContentDescriptor.RichFormat;fixture.SchemaVersion="4";await page.SetViewportSizeAsync(1600,1250);await fixture.OpenAsync();
        var textbox=page.Locator("#rich-text [role=textbox]");var blocks=textbox.Locator(":scope>p");var json=page.Locator("#document-json");
        Assert.Equal(source,await json.InputValueAsync());
        await page.Locator("#editor-paragraph-advanced").EvaluateAsync("e=>e.open=true");
        Assert.True(await page.Locator(".editor-paragraph-spacing label").EvaluateAllAsync<bool>("nodes=>Math.max(...nodes.map(node=>node.getBoundingClientRect().top))-Math.min(...nodes.map(node=>node.getBoundingClientRect().top))<1"));
        await textbox.EvaluateAsync("e=>{e.focus();const range=document.createRange();range.selectNodeContents(e);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}");
        foreach(var id in new[]{"#editor-space-before","#editor-space-after","#editor-first-line-indent"})await page.Locator(id).SelectOptionAsync("0");
        await SelectScriptText(page,blocks.Nth(1),"LTR_FIRST");await page.Locator("#editor-space-before").SelectOptionAsync("12");await page.Locator("#editor-space-after").SelectOptionAsync("18");await page.Locator("#editor-first-line-indent").SelectOptionAsync("12.5");await page.Locator("#editor-indent-level").SelectOptionAsync("2");
        await Command(page,"heading").ClickAsync();await Assertions.Expect(page.Locator("#editor-first-line-indent")).ToHaveValueAsync("12.5");await Command(page,"paragraph").ClickAsync();
        var metrics=await blocks.Nth(1).EvaluateAsync<double[]>("e=>{const style=getComputedStyle(e);return [parseFloat(style.marginTop),parseFloat(style.marginBottom),parseFloat(style.textIndent)];}");
        Assert.InRange(metrics[0],15.99,16.01);Assert.InRange(metrics[1],23.99,24.01);Assert.InRange(metrics[2],47.24,47.25);
        await SelectScriptText(page,blocks.Nth(3),"RTL_FIRST");await page.Locator("#editor-first-line-indent").SelectOptionAsync("5");await page.Locator("#editor-indent-level").SelectOptionAsync("2");Assert.Equal("rtl",await blocks.Nth(3).GetAttributeAsync("dir"));
        await SelectScriptText(page,textbox.Locator("td p"),"TABLE_SPACING");await page.Locator("#editor-space-before").SelectOptionAsync("6");await page.Locator("#editor-space-after").SelectOptionAsync("12");await page.Locator("#editor-first-line-indent").SelectOptionAsync("5");await page.Locator("#editor-indent-level").SelectOptionAsync("1");
        await textbox.Locator("blockquote").EvaluateAsync("e=>{e.closest('[role=textbox]').focus();const range=document.createRange();range.selectNodeContents(e);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}");
        await page.Locator("#editor-space-before").SelectOptionAsync("6");await page.Locator("#editor-space-after").SelectOptionAsync("12");await page.Locator("#editor-first-line-indent").SelectOptionAsync("10");
        var styled=await json.InputValueAsync();Assert.True(DocumentContentDescriptor.RichV10.TryRead(styled,out _));Assert.False(DocumentContentDescriptor.RichV4.TryRead(styled,out _));
        await textbox.EvaluateAsync("e=>{e.focus();const range=document.createRange();range.selectNodeContents(e);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}");
        foreach(var id in new[]{"#editor-space-before","#editor-space-after","#editor-first-line-indent"})await Assertions.Expect(page.Locator(id)).ToHaveValueAsync("__mixed__");
        await page.Locator("#editor-space-after").SelectOptionAsync("0");await Command(page,"undo").ClickAsync();Assert.Equal(styled,await json.InputValueAsync());
        await Command(page,"clearParagraphStyle").ClickAsync();
        using(var cleared=JsonDocument.Parse(await json.InputValueAsync())){var attrs=cleared.RootElement.GetProperty("content")[1].GetProperty("attrs");foreach(var field in DocumentParagraph.Fields)Assert.Equal(JsonValueKind.Null,attrs.GetProperty(field).ValueKind);}
        await Command(page,"undo").ClickAsync();Assert.Equal(styled,await json.InputValueAsync());
        await Search(page,"START_MARK","/ 1 eşleşme");await page.Locator("#editor-search-replacement").FillAsync("START_EDIT");await page.Locator("#editor-search-replace").ClickAsync();await Assertions.Expect(textbox).ToContainTextAsync("START_EDIT");await Command(page,"undo").ClickAsync();Assert.Equal(styled,await json.InputValueAsync());await page.Locator("#editor-search-clear").ClickAsync();
        var failed=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await failed.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await page.Locator("#editor-paragraph select").EvaluateAllAsync<bool>("nodes=>nodes.every(node=>node.disabled)"));failed.Release.SetResult(503);await Assertions.Expect(page.Locator("#editor-space-before")).ToBeEnabledAsync();
        var save=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));fixture.Source=SubmittedJson(save.Body);fixture.SchemaVersion="10";save.Release.SetResult(303);await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(styled,await json.InputValueAsync());await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        await page.Locator("#editor-paragraph-advanced").EvaluateAsync("e=>e.open=true");await SelectScriptText(page,blocks.Nth(1),"LTR_FIRST");
        await Assertions.Expect(page.Locator("#editor-space-before")).ToHaveValueAsync("12");await Assertions.Expect(page.Locator("#editor-first-line-indent")).ToHaveValueAsync("12.5");
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        var directory=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!,"../../../tmp/pdfs/paragraph-spacing"));Directory.CreateDirectory(directory);
        var markers=await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/START_MARK|AFTER_MARK|LTR_FIRST|RTL_FIRST|TABLE_SPACING|FLOW_\d{3}/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory,"spacing-markers.json"),markers);
        using(var renderer=new ChromiumDraftPdfRenderer()){
            var bytes=await renderer.RenderAsync(new(Guid.NewGuid(),1,null,styled,null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=10},CancellationToken.None);
            using var stream=new MemoryStream(bytes);using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!),pdf.PageCount);Assert.True(pdf.PageCount>1);
            await File.WriteAllBytesAsync(Path.Combine(directory,"paragraph-spacing.pdf"),bytes);
        }
        await page.Locator("#editor-focus-toggle").ClickAsync();await fixture.ScreenshotAsync("paragraph-spacing.png");await page.Keyboard.PressAsync("Escape");
        fixture.Editable=false;await fixture.OpenAsync();await page.Locator("#editor-paragraph-advanced").EvaluateAsync("e=>e.open=true");
        foreach(var id in new[]{"#editor-space-before","#editor-space-after","#editor-first-line-indent"}){await Assertions.Expect(page.Locator(id)).ToBeDisabledAsync();await page.Locator(id).EvaluateAsync("e=>{e.value='0';e.dispatchEvent(new Event('change',{bubbles:true}));}");}
        Assert.Equal(styled,await json.InputValueAsync());await AssertFrozenAsync(page);Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
}
