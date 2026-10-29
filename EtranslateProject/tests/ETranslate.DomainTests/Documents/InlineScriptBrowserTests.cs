using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string ScriptDocument()
    {
        object P(string text,string dir,string family,string color)=>new{type="paragraph",attrs=new{dir,lineHeight=2,textAlign=dir=="rtl"?"right":"left",indentLevel=1},content=new[]{new{type="text",text,marks=new[]{new{type="typography",attrs=new{fontFamily=family,fontSize=18,color}}}}}};
        return JsonSerializer.Serialize(new{type="doc",content=new object[]{
            P("SCRIPT_MARK H2O x2 OLD","ltr","serif","#b91c1c"),
            P("RTL_SCRIPT توان ۲ و شاخص ۳","rtl","arabic","#1d4ed8"),
            P("CARET_MARK ","ltr","serif","#20282b"),
            new{type="table",content=new[]{new{type="tableRow",content=new[]{new{type="tableCell",content=new[]{P("TABLE_SCRIPT T2","ltr","serif","#7e22ce")}}}}}}
        }});
    }
    private static async Task SelectScriptText(IPage page,ILocator block,string text,bool last=false)
    {
        await block.EvaluateAsync("""
            (element,arg)=>{element.closest('[role=textbox]').focus();const index=arg.last?element.textContent.lastIndexOf(arg.text):element.textContent.indexOf(arg.text);
                if(index<0)throw new Error('Selection text missing');const walker=document.createTreeWalker(element,NodeFilter.SHOW_TEXT);let offset=0,start,end;
                for(let node=walker.nextNode();node;node=walker.nextNode()){const next=offset+node.length;
                    if(!start&&index<next)start=[node,index-offset];if(index+arg.text.length<=next){end=[node,index+arg.text.length-offset];break;}offset=next;}
                const range=document.createRange();range.setStart(...start);range.setEnd(...end);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}
            """,new{text,last});
        await page.WaitForFunctionAsync("text=>getSelection().toString()===text",text);
    }
    [LocalEditorAdapterFact(Timeout=90000)]
    public async Task Inline_scripts_preserve_typography_direction_exclusion_history_saved_content_and_locks()
    {
        await using var fixture=await BrowserFixture.CreateAsync(ScriptDocument());var page=fixture.Page;
        fixture.Format=DocumentContentDescriptor.RichFormat;fixture.SchemaVersion="3";
        await page.SetViewportSizeAsync(1600,1250);await fixture.OpenAsync();
        var textbox=page.Locator("#rich-text [role=textbox]");var blocks=textbox.Locator(":scope>p");
        await SelectScriptText(page,blocks.Nth(0),"2");await Command(page,"subscript").ClickAsync();
        await Assertions.Expect(blocks.Nth(0).Locator("sub")).ToHaveTextAsync("2");
        await Command(page,"superscript").ClickAsync();Assert.Equal(0,await blocks.Nth(0).Locator("sub").CountAsync());
        await Command(page,"undo").ClickAsync();await Assertions.Expect(blocks.Nth(0).Locator("sub")).ToHaveTextAsync("2");
        await Command(page,"redo").ClickAsync();await Assertions.Expect(blocks.Nth(0).Locator("sup")).ToHaveTextAsync("2");
        await Command(page,"subscript").ClickAsync();
        await SelectScriptText(page,blocks.Nth(0),"2",last:true);await Command(page,"superscript").ClickAsync();
        await SelectScriptText(page,blocks.Nth(0),"OLD");await Command(page,"strike").ClickAsync();await Command(page,"bold").ClickAsync();
        await page.Locator("#editor-text-color").SelectOptionAsync("#15803d");
        await SelectScriptText(page,blocks.Nth(1),"۲");await Command(page,"superscript").ClickAsync();
        await SelectScriptText(page,blocks.Nth(1),"۳");await Command(page,"subscript").ClickAsync();
        Assert.Equal("rtl",await blocks.Nth(1).GetAttributeAsync("dir"));
        await blocks.Nth(2).Locator("span").First.ClickAsync(new(){Position=new(){X=5,Y=5}});await page.Keyboard.PressAsync("End");
        await Command(page,"superscript").ClickAsync();await page.Keyboard.InsertTextAsync("2");
        await Command(page,"subscript").ClickAsync();await page.Keyboard.InsertTextAsync("3");
        await Command(page,"subscript").ClickAsync();await page.Keyboard.InsertTextAsync("END");
        await Assertions.Expect(blocks.Nth(2).Locator("sup")).ToHaveTextAsync("2");await Assertions.Expect(blocks.Nth(2).Locator("sub")).ToHaveTextAsync("3");
        await SelectScriptText(page,textbox.Locator("td p"),"2");await Command(page,"superscript").ClickAsync();await Command(page,"strike").ClickAsync();
        var styled=await page.Locator("#document-json").InputValueAsync();
        Assert.True(DocumentContentDescriptor.RichV10.TryRead(styled,out _));Assert.False(DocumentContentDescriptor.RichV3.TryRead(styled,out _));
        await Assertions.Expect(page.Locator("#preview-body sup")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("#preview-body sub")).ToHaveCountAsync(3);
        var fail=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await fail.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach(var command in new[]{"superscript","subscript","strike"})await Assertions.Expect(Command(page,command)).ToBeDisabledAsync();
        fail.Release.SetResult(503);await Assertions.Expect(Command(page,"superscript")).ToBeEnabledAsync();
        var save=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source=SubmittedJson(save.Body);fixture.SchemaVersion="10";save.Release.SetResult(303);await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        var script=blocks.Nth(0).Locator("sub");
        Assert.Equal("18px",await script.EvaluateAsync<string>("e=>getComputedStyle(e).fontSize"));
        Assert.Equal("rgb(21, 128, 61)",await blocks.Nth(0).Locator("s").EvaluateAsync<string>("e=>getComputedStyle(e).color"));
        Assert.Equal("18px",await textbox.Locator("td sup s").EvaluateAsync<string>("e=>getComputedStyle(e).fontSize"));
        await SelectScriptText(page,blocks.Nth(0),"2");await Command(page,"clearTextStyle").ClickAsync();
        await Assertions.Expect(script).ToHaveTextAsync("2");await Command(page,"undo").ClickAsync();Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());
        await SelectScriptText(page,blocks.Nth(0),"H2O x2");await Command(page,"subscript").ClickAsync();
        Assert.Equal(0,await blocks.Nth(0).Locator("sup").CountAsync());await Command(page,"undo").ClickAsync();Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        var directory=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!,"../../../tmp/pdfs/inline-scripts"));Directory.CreateDirectory(directory);
        var markers=await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,result={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/SCRIPT_MARK|RTL_SCRIPT|CARET_MARK|TABLE_SCRIPT/g)){
                    const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);result[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers:result});}
            """);
        await File.WriteAllTextAsync(Path.Combine(directory,"script-markers.json"),markers);
        using(var renderer=new ChromiumDraftPdfRenderer()){
            var bytes=await renderer.RenderAsync(new(Guid.NewGuid(),1,null,styled,null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=10},CancellationToken.None);
            using var stream=new MemoryStream(bytes);using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);
            Assert.Equal(int.Parse((await page.Locator("#editor-viewport").GetAttributeAsync("data-page-count"))!),pdf.PageCount);
            await File.WriteAllBytesAsync(Path.Combine(directory,"script-styles.pdf"),bytes);
        }
        await page.Locator("#editor-focus-toggle").ClickAsync();await fixture.ScreenshotAsync("inline-scripts.png");await page.Keyboard.PressAsync("Escape");
        fixture.Editable=false;await fixture.OpenAsync();
        foreach(var command in new[]{"superscript","subscript","strike"}){await Assertions.Expect(Command(page,command)).ToBeDisabledAsync();await Command(page,command).EvaluateAsync("e=>e.dispatchEvent(new MouseEvent('click',{bubbles:true}))");}
        Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());await AssertFrozenAsync(page);
        Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
}
