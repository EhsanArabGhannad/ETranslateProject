using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    [LocalEditorAdapterFact(Timeout=90000)]
    public async Task Typography_preserves_selection_mixed_styles_history_saved_content_and_locks()
    {
        await using var fixture=await BrowserFixture.CreateAsync(Initial);var page=fixture.Page;
        fixture.Format=DocumentContentDescriptor.RichFormat;fixture.SchemaVersion="1";
        await page.SetViewportSizeAsync(1600,1200);await fixture.OpenAsync();
        var textbox=page.Locator("#rich-text [role=textbox]");
        await textbox.Locator("p").Nth(1).ClickAsync();await page.Keyboard.PressAsync("Home");await page.Keyboard.PressAsync("Shift+End");
        await Command(page,"bold").ClickAsync();
        await page.Locator("#editor-font-family").SelectOptionAsync("serif");
        await Assertions.Expect(page.Locator("#editor-font-family")).ToHaveValueAsync("serif");
        await page.Locator("#editor-font-size").SelectOptionAsync("18");
        await page.Locator("#editor-text-color").SelectOptionAsync("#b91c1c");
        await page.Locator("#editor-highlight").SelectOptionAsync("#fef08a");
        var latinStyle=await page.Locator("#document-json").InputValueAsync();
        Assert.True(DocumentContentDescriptor.RichV6.TryRead(latinStyle,out _));Assert.False(DocumentContentDescriptor.RichV1.TryRead(latinStyle,out _));
        using(var doc=JsonDocument.Parse(latinStyle)){
            var text=doc.RootElement.GetProperty("content")[1].GetProperty("content")[0];
            var attrs=text.GetProperty("marks").EnumerateArray().Single(mark=>mark.GetProperty("type").GetString()=="typography").GetProperty("attrs");
            Assert.Equal("serif",attrs.GetProperty("fontFamily").GetString());Assert.Equal(18,attrs.GetProperty("fontSize").GetInt32());
            Assert.Equal("#b91c1c",attrs.GetProperty("color").GetString());Assert.Equal("#fef08a",attrs.GetProperty("highlight").GetString());
        }
        await Command(page,"undo").ClickAsync();await Assertions.Expect(page.Locator("#editor-highlight")).ToHaveValueAsync("");
        await Command(page,"redo").ClickAsync();await Assertions.Expect(page.Locator("#editor-highlight")).ToHaveValueAsync("#fef08a");
        Assert.Equal(latinStyle,await page.Locator("#document-json").InputValueAsync());
        await textbox.Locator("p").First.EvaluateAsync("e=>{e.closest('[role=textbox]').focus();const range=document.createRange();range.selectNodeContents(e);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}");
        await Assertions.Expect(page.Locator("#editor-font-family")).ToHaveValueAsync("");
        await page.Locator("#editor-font-family").SelectOptionAsync("arabic");await page.Locator("#editor-font-size").SelectOptionAsync("12");
        await page.Locator("#editor-text-color").SelectOptionAsync("#1d4ed8");
        var styled=await page.Locator("#document-json").InputValueAsync();
        var failed=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await failed.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await page.Locator("#editor-typography :is(select,button)").EvaluateAllAsync<bool>("nodes=>nodes.every(node=>node.disabled)"));
        Assert.Equal(styled,SubmittedJson(failed.Body));failed.Release.SetResult(503);
        await Assertions.Expect(page.Locator("#editor-font-size")).ToBeEnabledAsync();
        var save=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source=SubmittedJson(save.Body);fixture.SchemaVersion="6";save.Release.SetResult(303);await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        await page.EvaluateAsync("()=>document.fonts.ready");
        var computed=await textbox.Locator("p").Nth(1).Locator("span[data-et-typography]").First.EvaluateAsync<string[]>("e=>{const style=getComputedStyle(e);return [style.fontFamily,style.fontSize,style.color,style.backgroundColor];}");
        Assert.Contains("Noto Serif",computed[0]);Assert.Equal("24px",computed[1]);Assert.Equal("rgb(185, 28, 28)",computed[2]);Assert.Equal("rgb(254, 240, 138)",computed[3]);
        await fixture.ScreenshotAsync("typography.png");
        var output=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!,"../../../tmp/pdfs/typography"));Directory.CreateDirectory(output);
        using(var renderer=new ChromiumDraftPdfRenderer()){
            var bytes=await renderer.RenderAsync(new(Guid.NewGuid(),1,null,styled,null,null,null,null,new Dictionary<Guid,PdfImage>())
                {ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=6},CancellationToken.None);
            await File.WriteAllBytesAsync(Path.Combine(output,"selected-text.pdf"),bytes);
        }
        await page.EvaluateAsync("()=>{const root=document.querySelector('#rich-text [role=textbox]');root.focus();const range=document.createRange();range.selectNodeContents(root);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);}");
        await Assertions.Expect(page.Locator("#editor-font-family")).ToHaveValueAsync("__mixed__");
        await page.Locator("#editor-font-size").SelectOptionAsync("24");
        var mixed=await page.Locator("#document-json").InputValueAsync();Assert.Contains("serif",mixed);Assert.Contains("b91c1c",mixed);
        await page.Locator("#editor-typography button[data-command=clearTextStyle]").ClickAsync();
        var cleared=await page.Locator("#document-json").InputValueAsync();Assert.DoesNotContain("typography",cleared);Assert.Contains("bold",cleared);Assert.Contains("rtl",cleared);
        await Command(page,"undo").ClickAsync();Assert.Equal(mixed,await page.Locator("#document-json").InputValueAsync());
        await Command(page,"redo").ClickAsync();Assert.Equal(cleared,await page.Locator("#document-json").InputValueAsync());
        // The fixture retains the saved revision, independently of unsaved local history.
        fixture.Editable=false;await fixture.OpenAsync();
        Assert.True(await page.Locator("#editor-typography :is(select,button)").EvaluateAllAsync<bool>("nodes=>nodes.every(node=>node.disabled)"));
        await page.Locator("#editor-font-size").EvaluateAsync("e=>{e.value='24';e.dispatchEvent(new Event('change',{bubbles:true}));}");
        Assert.Equal(styled,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        await AssertFrozenAsync(page);Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
}
