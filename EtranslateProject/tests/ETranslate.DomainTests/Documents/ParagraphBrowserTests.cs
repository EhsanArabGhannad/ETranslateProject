using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string ParagraphDocument()
    {
        object Block(string text,string dir,string color,bool heading=false)=>new{
            type=heading?"heading":"paragraph",attrs=heading?(object)new{dir,level=2}:new{dir},
            content=new[]{new{type="text",text,marks=new[]{new{type="typography",attrs=new{color}}}}}};
        return JsonSerializer.Serialize(new{type="doc",content=new[]{
            Block("RTL_MARK "+string.Concat(Enumerable.Repeat("ترجمهٔ آزمایشی برای بررسی فاصلهٔ خطوط و تورفتگی. ",8)),"rtl","#1d4ed8"),
            Block("LTR_MARK "+string.Concat(Enumerable.Repeat("Sentetik paragraf satır aralığı ve girinti testi. ",8)),"ltr","#15803d"),
            Block("CENTER_MARK","ltr","#7e22ce",true),
            Block("JUSTIFY_MARK "+string.Concat(Enumerable.Repeat("Justified translation paragraph with multiple words for measurement. ",8)),"ltr","#b91c1c")
        }});
    }
    [LocalEditorAdapterFact(Timeout=90000)]
    public async Task Paragraph_layout_preserves_direction_mixed_selection_history_pagination_and_save_locks()
    {
        var source=ParagraphDocument();await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;
        fixture.Format=DocumentContentDescriptor.RichFormat;fixture.SchemaVersion="2";
        await page.SetViewportSizeAsync(1600,1250);await fixture.OpenAsync();
        var textbox=page.Locator("#rich-text [role=textbox]");var blocks=textbox.Locator(":scope>p,:scope>h2");
        await blocks.Nth(0).Locator("span").First.ClickAsync(new(){Position=new(){X=5,Y=5}});await Assertions.Expect(Command(page,"rtl")).ToHaveAttributeAsync("aria-pressed","true");
        await page.Locator("#editor-text-align").SelectOptionAsync("left");
        Assert.Equal("rtl",await blocks.Nth(0).GetAttributeAsync("dir"));
        await page.Locator("#editor-text-align").SelectOptionAsync("right");
        await page.Locator("#editor-line-height").SelectOptionAsync("2");
        await page.Locator("#editor-indent-level").SelectOptionAsync("2");
        var metrics=await blocks.Nth(0).EvaluateAsync<double[]>("e=>{const s=getComputedStyle(e);return [parseFloat(s.marginRight),parseFloat(s.marginLeft),parseFloat(s.lineHeight)/parseFloat(s.fontSize)];}");
        var diagnostic=await page.EvaluateAsync<string>("()=>JSON.stringify({nodes:[...document.querySelectorAll('#rich-text p,#rich-text h2')].map(e=>({text:e.textContent.slice(0,18),data:e.dataset.etParagraph,style:e.getAttribute('style'),dir:e.dir})),json:JSON.parse(document.querySelector('#document-json').value).content.map(n=>n.attrs),notice:document.querySelector('#editor-format-status').textContent})");
        Assert.True(metrics[0]>37.7&&metrics[0]<37.9,diagnostic);Assert.Equal(0,metrics[1]);Assert.InRange(metrics[2],1.999,2.001);
        await blocks.Nth(1).Locator("span").First.ClickAsync(new(){Position=new(){X=5,Y=5}});await Assertions.Expect(Command(page,"ltr")).ToHaveAttributeAsync("aria-pressed","true");
        await page.Locator("#editor-text-align").SelectOptionAsync("left");await page.Locator("#editor-line-height").SelectOptionAsync("1.5");
        await page.Locator("#editor-indent-level").SelectOptionAsync("8");await Assertions.Expect(Command(page,"increaseIndent")).ToBeDisabledAsync();
        await Command(page,"decreaseIndent").ClickAsync();await Assertions.Expect(page.Locator("#editor-indent-level")).ToHaveValueAsync("7");
        await page.Locator("#editor-indent-level").SelectOptionAsync("0");await Assertions.Expect(Command(page,"decreaseIndent")).ToBeDisabledAsync();
        await Command(page,"increaseIndent").ClickAsync();await Command(page,"increaseIndent").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-indent-level")).ToHaveValueAsync("2");
        metrics=await blocks.Nth(1).EvaluateAsync<double[]>("e=>{const s=getComputedStyle(e);return [parseFloat(s.marginLeft),parseFloat(s.marginRight)];}");
        diagnostic=await blocks.Nth(1).EvaluateAsync<string>("e=>JSON.stringify({data:e.dataset.etParagraph,style:e.getAttribute('style'),dir:e.dir,direction:getComputedStyle(e).direction})");
        Assert.True(metrics[0]>37.7&&metrics[0]<37.9,diagnostic);Assert.Equal(0,metrics[1]);
        await blocks.Nth(2).Locator("span").First.ClickAsync(new(){Position=new(){X=5,Y=5}});await Assertions.Expect(Command(page,"heading")).ToHaveAttributeAsync("aria-pressed","true");await page.Locator("#editor-text-align").SelectOptionAsync("center");await page.Locator("#editor-line-height").SelectOptionAsync("1.15");
        await Command(page,"paragraph").ClickAsync();await Assertions.Expect(page.Locator("#editor-text-align")).ToHaveValueAsync("center");await Assertions.Expect(page.Locator("#editor-line-height")).ToHaveValueAsync("1.15");
        await Command(page,"heading").ClickAsync();await Assertions.Expect(page.Locator("#editor-text-align")).ToHaveValueAsync("center");
        await blocks.Nth(3).Locator("span").First.ClickAsync(new(){Position=new(){X=5,Y=5}});await Assertions.Expect(page.Locator("#editor-text-align")).ToHaveValueAsync("");await page.Locator("#editor-text-align").SelectOptionAsync("justify");await page.Locator("#editor-line-height").SelectOptionAsync("2.5");await page.Locator("#editor-indent-level").SelectOptionAsync("1");
        var styled=await page.Locator("#document-json").InputValueAsync();Assert.True(DocumentContentDescriptor.RichV6.TryRead(styled,out _));Assert.False(DocumentContentDescriptor.RichV2.TryRead(styled,out _));
        var fail=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await fail.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await page.Locator("#editor-paragraph :is(select,button)").EvaluateAllAsync<bool>("nodes=>nodes.every(node=>node.disabled)"));
        fail.Release.SetResult(503);await Assertions.Expect(page.Locator("#editor-text-align")).ToBeEnabledAsync();
        var save=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source=SubmittedJson(save.Body);fixture.SchemaVersion="6";save.Release.SetResult(303);await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        var pageCount=int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!);
        var directory=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!,"../../../tmp/pdfs/paragraph"));Directory.CreateDirectory(directory);
        var markers=await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/RTL_MARK|LTR_MARK|CENTER_MARK|JUSTIFY_MARK/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);
                    result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;
                }return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory,"paragraph-markers.json"),markers);
        using(var renderer=new ChromiumDraftPdfRenderer()){
            var bytes=await renderer.RenderAsync(new(Guid.NewGuid(),1,null,styled,null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=6},CancellationToken.None);
            using var stream=new MemoryStream(bytes);using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);Assert.Equal(pageCount,pdf.PageCount);
            await File.WriteAllBytesAsync(Path.Combine(directory,"paragraph-layout.pdf"),bytes);
        }
        await page.Locator("#editor-focus-toggle").ClickAsync();await fixture.ScreenshotAsync("paragraph-layout.png");await page.Keyboard.PressAsync("Escape");
        await page.Locator("#editor-pages-toggle").ClickAsync();
        await page.EvaluateAsync("()=>{const root=document.querySelector('#rich-text [role=textbox]');root.focus();const range=document.createRange();range.selectNodeContents(root);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}");
        await Assertions.Expect(page.Locator("#editor-text-align")).ToHaveValueAsync("__mixed__");
        await page.Locator("#editor-line-height").SelectOptionAsync("2");var mixed=await page.Locator("#document-json").InputValueAsync();
        using(var json=JsonDocument.Parse(mixed)){var items=json.RootElement.GetProperty("content");Assert.Equal("right",items[0].GetProperty("attrs").GetProperty("textAlign").GetString());Assert.Equal("justify",items[3].GetProperty("attrs").GetProperty("textAlign").GetString());}
        await Command(page,"clearParagraphStyle").ClickAsync();var cleared=await page.Locator("#document-json").InputValueAsync();
        using(var json=JsonDocument.Parse(cleared))foreach(var block in json.RootElement.GetProperty("content").EnumerateArray()){
            var attrs=block.GetProperty("attrs");Assert.Equal(JsonValueKind.Null,attrs.GetProperty("textAlign").ValueKind);Assert.Equal(JsonValueKind.Null,attrs.GetProperty("lineHeight").ValueKind);Assert.Equal(JsonValueKind.Null,attrs.GetProperty("indentLevel").ValueKind);
        }
        Assert.Contains("typography",cleared);Assert.Contains("rtl",cleared);
        await Command(page,"undo").ClickAsync();Assert.Equal(mixed,await page.Locator("#document-json").InputValueAsync());await Command(page,"redo").ClickAsync();Assert.Equal(cleared,await page.Locator("#document-json").InputValueAsync());
        fixture.Editable=false;await fixture.OpenAsync();
        Assert.True(await page.Locator("#editor-paragraph :is(select,button)").EvaluateAllAsync<bool>("nodes=>nodes.every(node=>node.disabled)"));
        await page.Locator("#editor-line-height").EvaluateAsync("e=>{e.value='3';e.dispatchEvent(new Event('change',{bubbles:true}));}");Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");await AssertFrozenAsync(page);
        Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
}
