using System.Text.Json;
using ETranslate.Contracts.Documents;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string SearchDocument()
    {
        object Text(string text,object[]? marks=null)=>new{type="text",text,marks=marks??[]};
        object P(object[] content,string dir="ltr")=>new{type="paragraph",attrs=new{dir},content};
        List<object> content=[
            P([Text("SEARCH_MARK MER",[new{type="bold"},new{type="typography",attrs=new{fontFamily="serif",fontSize=18,color="#b91c1c"}}]),Text("HABA",[new{type="italic"}]),Text(" merhaba MERHABA.")]),
            P([Text("ترجمه می‌روم ترجمه")],"rtl"),P([Text("I ı İ i A.*A 😀")]),
            new{type="table",content=new[]{new{type="tableRow",content=new[]{new{type="tableCell",content=new[]{P([Text("merhaba",[new{type="underline"},new{type="superscript"}])])}}}}}}
        ];
        for(var index=0;index<70;index++)content.Add(P([Text($"ROW_{index:000} İstanbul çeviri metni · جدول ساختگی. "+new string('x',70))]));
        content.Add(P([Text("TAIL_TARGET")]));return JsonSerializer.Serialize(new{type="doc",content});
    }
    private static async Task Search(IPage page,string query,string count)
    {
        await page.Locator("#editor-search").EvaluateAsync("e=>e.open=true");
        await page.Locator("#editor-search-query").FillAsync(query);
        await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync(count);
    }
    [LocalEditorAdapterFact(Timeout=120000)]
    public async Task Search_replace_preserves_marks_history_statistics_pagination_and_readonly_navigation()
    {
        var source=SearchDocument();await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;
        fixture.Format=DocumentContentDescriptor.RichFormat;fixture.SchemaVersion="4";
        await page.SetViewportSizeAsync(1600,1250);await fixture.OpenAsync();
        var textbox=page.Locator("#rich-text [role=textbox]");var json=page.Locator("#document-json");
        var counts=await page.Locator("#editor-statistics").InnerTextAsync();Assert.Contains("sözcük",counts);
        await textbox.Locator("p").First.ClickAsync();await page.Keyboard.PressAsync("Control+Home");await page.Keyboard.PressAsync("Control+f");
        await Assertions.Expect(page.Locator("#editor-search-query")).ToBeFocusedAsync();
        await Search(page,"merhaba","/ 4 eşleşme");
        Assert.Equal(source,await json.InputValueAsync());Assert.Equal(counts,await page.Locator("#editor-statistics").InnerTextAsync());
        await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        await page.Locator("#editor-search-next").ClickAsync();Assert.Equal("MERHABA",await page.EvaluateAsync<string>("()=>getSelection().toString()"));
        await page.Locator("#editor-search-next").ClickAsync();await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync("2 / 4");
        await page.Locator("#editor-search-prev").ClickAsync();await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync("1 / 4");
        await page.Locator("#editor-search-replacement").FillAsync("SELAM");await page.Locator("#editor-search-replace").ClickAsync();
        await Assertions.Expect(textbox.Locator("p").First).ToContainTextAsync("SEARCH_MARK SELAM merhaba MERHABA.");
        var first=await json.InputValueAsync();
        using(var body=JsonDocument.Parse(first)){var leaf=body.RootElement.GetProperty("content")[0].GetProperty("content").EnumerateArray().Single(node=>node.GetProperty("text").GetString()!.Contains("SELAM"));
            var marks=leaf.GetProperty("marks").GetRawText();Assert.Contains("bold",marks);Assert.Contains("#b91c1c",marks);Assert.DoesNotContain("italic",marks);}
        await Command(page,"undo").ClickAsync();Assert.Equal(counts,await page.Locator("#editor-statistics").InnerTextAsync());
        await Command(page,"redo").ClickAsync();Assert.Equal(first,await json.InputValueAsync());
        await textbox.Locator("p").First.ClickAsync();await page.Keyboard.PressAsync("End");await page.Keyboard.InsertTextAsync(" EXTRA");
        await Command(page,"undo").ClickAsync();Assert.Equal(first,await json.InputValueAsync());
        await page.Locator("#editor-search-all").ClickAsync();await Assertions.Expect(textbox).Not.ToContainTextAsync("merhaba");
        var all=await json.InputValueAsync();Assert.Contains("superscript",all);Assert.Contains("underline",all);
        await Command(page,"undo").ClickAsync();Assert.Equal(first,await json.InputValueAsync());
        await Command(page,"redo").ClickAsync();Assert.Equal(all,await json.InputValueAsync());
        await Search(page,"SELAM","/ 4 eşleşme");await page.Locator("#editor-search-replace").ClickAsync();Assert.Equal(all,await json.InputValueAsync());
        await page.Locator("#editor-search-replacement").FillAsync("");await page.Locator("#editor-search-replace").ClickAsync();
        Assert.NotEqual(all,await json.InputValueAsync());await Command(page,"undo").ClickAsync();Assert.Equal(all,await json.InputValueAsync());
        await Search(page,"A.*A","/ 1 eşleşme");await page.Locator("#editor-search-replacement").FillAsync("<b>$&</b>");await page.Locator("#editor-search-replace").ClickAsync();
        await Assertions.Expect(textbox).ToContainTextAsync("<b>$&</b>");Assert.Equal(0,await textbox.Locator("b,script").CountAsync());
        await page.Locator("#editor-search-word").CheckAsync();await Search(page,"i","/ 2 eşleşme");
        await page.Locator("#editor-search-case").CheckAsync();await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync("/ 1 eşleşme");
        await page.Locator("#editor-search-case").UncheckAsync();await page.Locator("#editor-search-word").UncheckAsync();
        await Search(page,"ترجمه","/ 2 eşleşme");await page.Locator("#editor-search-replacement").FillAsync("ترجمهٔ رسمی");await page.Locator("#editor-search-all").ClickAsync();
        Assert.Equal("rtl",await textbox.Locator(":scope>p").Nth(1).GetAttributeAsync("dir"));await Assertions.Expect(textbox.Locator(":scope>p").Nth(1)).ToHaveTextAsync("ترجمهٔ رسمی می‌روم ترجمهٔ رسمی");
        var edited=await json.InputValueAsync();Assert.True(DocumentContentDescriptor.RichV7.TryRead(edited,out _));Assert.DoesNotContain("editor-search",edited);
        await page.Locator("#editor-pages-toggle").ClickAsync();await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination","paged");
        await Search(page,"TAIL_TARGET","/ 1 eşleşme");await page.Locator("#editor-search-next").ClickAsync();
        await page.WaitForFunctionAsync("()=>Number(document.querySelector('#editor-viewport').dataset.pageIndex)>1");
        await Search(page,"SEARCH_MARK","/ 1 eşleşme");await page.Locator("#editor-search-next").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-page-index","1");
        Assert.Equal(edited,await json.InputValueAsync());
        var fail=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await fail.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(page.Locator("#editor-search-replace")).ToBeDisabledAsync();await Assertions.Expect(page.Locator("#editor-search-replacement")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#editor-search-query")).ToBeEnabledAsync();
        await page.Locator("#editor-search-all").EvaluateAsync("e=>e.dispatchEvent(new MouseEvent('click',{bubbles:true}))");Assert.Equal(edited,await json.InputValueAsync());
        fail.Release.SetResult(503);await Assertions.Expect(page.Locator("#editor-search-replace")).ToBeEnabledAsync();
        var save=fixture.Enqueue();await page.Locator("#draft-form button[type=submit]").ClickAsync();await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source=SubmittedJson(save.Body);fixture.SchemaVersion="7";save.Release.SetResult(303);await page.WaitForURLAsync("**after-save=1");
        Assert.Equal(edited,await json.InputValueAsync());await Assertions.Expect(Command(page,"undo")).ToBeDisabledAsync();
        await Search(page,"SELAM","/ 4 eşleşme");await page.Locator("#editor-focus-toggle").ClickAsync();await fixture.ScreenshotAsync("search-replace.png");await page.Keyboard.PressAsync("Escape");
        fixture.Editable=false;await fixture.OpenAsync();await Search(page,"SELAM","/ 4 eşleşme");
        await Assertions.Expect(page.Locator("#editor-search-all")).ToBeDisabledAsync();await page.Locator("#editor-search-next").ClickAsync();Assert.Equal("SELAM",await page.EvaluateAsync<string>("()=>getSelection().toString()"));
        await page.Locator("#editor-search-replace").EvaluateAsync("e=>e.dispatchEvent(new MouseEvent('click',{bubbles:true}))");
        Assert.Equal(edited,await json.InputValueAsync());await AssertFrozenAsync(page);Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
    [LocalEditorAdapterFact]
    public async Task Search_limit_is_explicit_and_replace_all_cannot_silently_change_only_a_subset()
    {
        var source=JsonSerializer.Serialize(new{type="doc",content=new[]{new{type="paragraph",content=new[]{new{type="text",text=string.Concat(Enumerable.Repeat("x ",2001))}}}}});
        await using var fixture=await BrowserFixture.CreateAsync(source);var page=fixture.Page;await page.SetViewportSizeAsync(390,844);await fixture.OpenAsync();
        await Search(page,"x","2.000+");await Assertions.Expect(page.Locator("#editor-search-all")).ToBeDisabledAsync();
        await AssertNoOuterOverflowAsync(page);await fixture.ScreenshotAsync("search-mobile.png");
        await page.Locator("#editor-search-replacement").FillAsync("y");await page.Locator("#editor-search-all").EvaluateAsync("e=>e.dispatchEvent(new MouseEvent('click',{bubbles:true}))");
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-search-replacement").EvaluateAsync("e=>e.value='y'.repeat(2001)");await page.Locator("#editor-search-replace").ClickAsync();
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-search-query").EvaluateAsync("e=>{e.value='q'.repeat(201);e.dispatchEvent(new Event('input',{bubbles:true}));}");
        await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync("0 / 0");await Assertions.Expect(page.Locator("#editor-search-replace")).ToBeDisabledAsync();
        Assert.Equal(source,await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-search-clear").ClickAsync();Assert.Equal(0,await page.Locator("#rich-text .editor-search-match").CountAsync());
        Assert.Empty(fixture.Errors);Assert.Empty(fixture.Unexpected);
    }
}
