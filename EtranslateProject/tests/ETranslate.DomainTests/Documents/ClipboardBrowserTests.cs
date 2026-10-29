using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private const string ClipboardSource = """
        {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"CLIP_BEFORE"}]},{"type":"paragraph"},{"type":"paragraph","content":[{"type":"text","text":"CLIP_AFTER"}]}]}
        """;
    // Synthetic Office clipboard markup, never a customer's document or an asserted Word corpus.
    private const string WordClipboard = """
        <html xmlns:o="urn:schemas-microsoft-com:office:office"><head><style>
        p.MsoNormal {font-family:"Times New Roman";font-size:12pt;margin:0pt;}
        </style></head><body><!--StartFragment-->
        <p class="MsoNormal" dir="rtl" style="text-align:right;line-height:150%;margin-top:6pt;margin-bottom:12pt;text-indent:5mm">
        <b><span style="font-size:18pt;color:#B91C1C;background-color:#FEF08A">WP_STYLE İstanbul ترجمه</span></b><i> WP_ITALIC</i><u> WP_UNDERLINE</u><sup>WP_SUP</sup></p>
        <h2 style="page-break-before:always;text-align:center">WP_HEADING</h2>
        <p class="MsoNormal">WP_LITERAL $5 &lt;not-a-tag&gt; نیم‌فاصله</p>
        <ul><li>WP_BULLET</li></ul>
        <p class="MsoListParagraph" style="mso-list:l0 level1 lfo1"><span style="mso-list:Ignore">3.<span>&nbsp; </span></span>WP_LIST_A</p>
        <p class="MsoListParagraph" style="mso-list:l0 level1 lfo1"><span style="mso-list:Ignore">4.<span>&nbsp; </span></span>WP_LIST_B</p>
        <table style="width:100%"><thead><tr><th style="width:60%;background-color:#dbeafe;border:1pt solid #1d4ed8"><p>WP_CELL_A</p></th><th style="width:40%;background-color:#dbeafe;border:1pt solid #1d4ed8"><p>WP_CELL_B</p></th></tr></thead>
        <tbody><tr><td colspan="2"><p>WP_SPAN</p></td></tr></tbody></table>
        <!--EndFragment--></body></html>
        """;
    private static Task ClipboardPaste(IPage page, string html, string text = "") => page.Locator("#rich-text [role=textbox]").EvaluateAsync("""
        (el,value)=>{const data=new DataTransfer();if(value.html)data.setData('text/html',value.html);if(value.text)data.setData('text/plain',value.text);el.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));}
        """, new { html, text });
    private static IEnumerable<JsonElement> ClipboardNodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("content", out var children)) foreach (var child in children.EnumerateArray()) foreach (var item in ClipboardNodes(child)) yield return item;
    }

    [LocalEditorAdapterFact]
    public async Task Word_clipboard_preserves_supported_styles_lists_table_history_save_and_pdf()
    {
        await using var fixture = await BrowserFixture.CreateAsync(ClipboardSource); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync(); await PageBreakCaret(page, 1, 0);
        var json = page.Locator("#document-json"); await ClipboardPaste(page, WordClipboard);
        await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("Noto");
        var pasted = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV10.TryRead(pasted, out var plain));
        foreach (var marker in new[] { "CLIP_BEFORE", "CLIP_AFTER", "WP_STYLE", "WP_LIST_A", "WP_LIST_B", "WP_SPAN", "$5 <not-a-tag>", "نیم‌فاصله" }) Assert.Contains(marker, plain);
        using (var body = JsonDocument.Parse(pasted)) {
            var all = ClipboardNodes(body.RootElement).ToArray();
            var styled = all.Single(n => n.GetProperty("type").GetString() == "text" && n.GetProperty("text").GetString()!.Contains("WP_STYLE"));
            var marks = styled.GetProperty("marks").EnumerateArray().ToArray(); Assert.Contains(marks, m => m.GetProperty("type").GetString() == "bold");
            var paint = marks.Single(m => m.GetProperty("type").GetString() == "typography").GetProperty("attrs");
            Assert.Equal("serif", paint.GetProperty("fontFamily").GetString()); Assert.Equal(18, paint.GetProperty("fontSize").GetInt32()); Assert.Equal("#b91c1c", paint.GetProperty("color").GetString()); Assert.Equal("#fef08a", paint.GetProperty("highlight").GetString());
            var paragraph = all.First(n => n.GetProperty("type").GetString() == "paragraph" && n.TryGetProperty("attrs", out var a) && a.TryGetProperty("spaceBeforePt", out var v) && v.ValueKind == JsonValueKind.Number);
            Assert.Equal("rtl", paragraph.GetProperty("attrs").GetProperty("dir").GetString()); Assert.Equal(6, paragraph.GetProperty("attrs").GetProperty("spaceBeforePt").GetInt32()); Assert.Equal(1.5m, paragraph.GetProperty("attrs").GetProperty("lineHeight").GetDecimal());
            Assert.Equal(1, DocumentPageBreak.Count(body.RootElement));
            var list = all.Single(n => n.GetProperty("type").GetString() == "orderedList"); Assert.Equal(3, list.GetProperty("attrs").GetProperty("start").GetInt32()); Assert.Equal(2, list.GetProperty("content").GetArrayLength());
            var table = all.Single(n => n.GetProperty("type").GetString() == "table"); Assert.Equal(new[] { 60, 40 }, table.GetProperty("attrs").GetProperty("columnWidths").EnumerateArray().Select(v => v.GetInt32()).ToArray());
            Assert.Equal(2, table.GetProperty("content")[1].GetProperty("content")[0].GetProperty("attrs").GetProperty("colspan").GetInt32());
            Assert.Equal("#dbeafe", table.GetProperty("content")[0].GetProperty("content")[0].GetProperty("attrs").GetProperty("backgroundColor").GetString());
        }
        await Command(page, "undo").ClickAsync();
        using (var undone = JsonDocument.Parse(await json.InputValueAsync())) { Assert.Equal(3, undone.RootElement.GetProperty("content").GetArrayLength()); Assert.DoesNotContain("WP_", undone.RootElement.GetRawText()); Assert.Equal("CLIP_BEFORE", undone.RootElement.GetProperty("content")[0].GetProperty("content")[0].GetProperty("text").GetString()); }
        await Command(page, "redo").ClickAsync(); Assert.Equal(pasted, await json.InputValueAsync());
        await page.Locator("#editor-clipboard").EvaluateAsync("el=>el.open=true"); await page.Locator("#editor-clipboard").ScrollIntoViewIfNeededAsync(); await fixture.ScreenshotAsync("word-paste-controls.png");
        var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await ClipboardPaste(page, "<p>BLOCKED_PENDING</p>"); Assert.Equal(pasted, await json.InputValueAsync());
        fixture.Source = SubmittedJson(save.Body); save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(pasted, await json.InputValueAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        var map = await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,markers={};
            const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let n=walker.nextNode();n;n=walker.nextNode())for(const m of n.data.matchAll(/WP_[A-Z_]+|CLIP_BEFORE|CLIP_AFTER/g)){const r=document.createRange();r.setStart(n,m.index);r.setEnd(n,m.index+m[0].length);const rect=[...r.getClientRects()].find(r=>r.width>0);markers[m[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
            return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers});}
            """);
        var directory = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/word-paste")); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "word.json"), map); await File.WriteAllTextAsync(Path.Combine(directory, "saved.json"), pasted);
        using var renderer = new ChromiumDraftPdfRenderer(); var bytes = await renderer.RenderAsync(new DraftPdfInput(Guid.NewGuid(), 1, null, pasted, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 }, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(directory, "word.pdf"), bytes); await fixture.ScreenshotAsync("word-paste.png");
        Assert.True(fixture.Errors.Count == 0, string.Join("\n", fixture.Errors)); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Unsupported_clipboard_is_inert_reports_losses_plain_mode_and_limits_preserve_original()
    {
        await using var fixture = await BrowserFixture.CreateAsync(ClipboardSource); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        var page = fixture.Page; await fixture.OpenAsync(); var json = page.Locator("#document-json"); await PageBreakCaret(page, 1, 0);
        const string html = """
            <link rel="stylesheet" href="https://clipboard.invalid/a.css"><style>p{background-image:url(https://clipboard.invalid/css.png)}</style>
            <script>window.pasteExecuted=true</script><iframe src="https://clipboard.invalid/frame"></iframe>
            <p onclick="window.pasteExecuted=true">VISIBLE <img data-asset-id="11111111-1111-1111-1111-111111111111" src="https://clipboard.invalid/image.png" onerror="window.pasteExecuted=true" alt="ALT_IMAGE"><a href="javascript:bad()">LINK_TEXT</a></p>
            <table><tr><td rowspan="2"><p>CELL_ONE</p></td><td>CELL_TWO</td></tr><tr><td>CELL_THREE</td></tr></table>
            """;
        await ClipboardPaste(page, html); var result = await json.InputValueAsync();
        Assert.True(DocumentContentDescriptor.RichV10.TryRead(result, out var plain)); foreach (var text in new[] { "VISIBLE", "ALT_IMAGE", "LINK_TEXT", "CELL_ONE", "CELL_TWO", "CELL_THREE" }) Assert.Contains(text, plain);
        Assert.DoesNotContain("clipboard.invalid", result); Assert.DoesNotContain("assetId", result); Assert.DoesNotContain("tableRow", result); Assert.False(await page.EvaluateAsync<bool>("()=>window.pasteExecuted===true"));
        await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("hücre metinlerine"); await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("görselleri");
        foreach (var payload in new[] { "<p>" + new string('x', 2_000_001) + "</p>", string.Concat(Enumerable.Repeat("<div>", 34)) + "DEEP" + string.Concat(Enumerable.Repeat("</div>", 34)) }) {
            await ClipboardPaste(page, payload); Assert.Equal(result, await json.InputValueAsync()); await Assertions.Expect(page.Locator("#editor-paste-status")).ToHaveAttributeAsync("data-outcome", "rejected");
        }
        await page.Locator("#editor-clipboard").EvaluateAsync("e=>e.open=true"); await page.Locator("#editor-paste-mode").SelectOptionAsync("plain"); await PageBreakCaret(page, 0, 0);
        await ClipboardPaste(page, "<p><b>FOREIGN_FORMAT</b></p>", "PLAIN <b>literal</b>\n\nترجمه\tİstanbul");
        var plainResult = await json.InputValueAsync(); Assert.Contains("PLAIN <b>literal</b>", plainResult); Assert.DoesNotContain("FOREIGN_FORMAT", plainResult);
        await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("Yalnızca metin");
        await page.Locator("#editor-paste-mode").SelectOptionAsync("formatted"); await PageBreakCaret(page, 0, 0);
        await page.Locator("#rich-text [role=textbox]").DispatchEventAsync("keydown", new { key = "v", ctrlKey = true, shiftKey = true, bubbles = true });
        await ClipboardPaste(page, "<p><b>WRONG_FORMATTED</b></p>", "SHORTCUT_PLAIN"); await page.Locator("#rich-text [role=textbox]").DispatchEventAsync("keyup", new { key = "v", bubbles = true });
        Assert.Contains("SHORTCUT_PLAIN", await json.InputValueAsync()); Assert.DoesNotContain("WRONG_FORMATTED", await json.InputValueAsync());
        fixture.Source = await json.InputValueAsync(); fixture.Editable = false; await fixture.OpenAsync(); await PageBreakCaret(page, 0, 0); await ClipboardPaste(page, "<p>BLOCKED_READONLY</p>"); Assert.Equal(fixture.Source, await json.InputValueAsync());
        await Assertions.Expect(page.Locator("#editor-paste-mode")).ToBeDisabledAsync(); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Local_copy_cut_paste_preserves_private_images_and_readonly_cut_cannot_delete()
    {
        var source = JsonSerializer.Serialize(new { type = "doc", content = new object[] { BreakP("LOCAL_FIRST"), new { type = "image", attrs = new { assetId = BrowserFixture.ImageId, assetScope = "document", widthPercent = 40, align = "right" } }, BreakP("LOCAL_LAST", true) } });
        await using var fixture = await BrowserFixture.CreateAsync(source); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        var page = fixture.Page; await fixture.OpenAsync(); var json = page.Locator("#document-json"); var textbox = page.Locator("#rich-text [role=textbox]");
        await textbox.FocusAsync(); await page.Keyboard.PressAsync("Control+a");
        await textbox.EvaluateAsync("el=>{window.qaClipboard=new DataTransfer();el.dispatchEvent(new ClipboardEvent('copy',{clipboardData:window.qaClipboard,bubbles:true,cancelable:true}));}");
        await textbox.EvaluateAsync("el=>el.dispatchEvent(new ClipboardEvent('paste',{clipboardData:window.qaClipboard,bubbles:true,cancelable:true}))");
        await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("Bu editörde"); await Assertions.Expect(textbox.Locator("img")).ToHaveCountAsync(1); Assert.True(DocumentContentDescriptor.RichV10.TryRead(await json.InputValueAsync(), out _));
        var beforeCut = await json.InputValueAsync(); await textbox.FocusAsync(); await page.Keyboard.PressAsync("Control+a");
        await textbox.EvaluateAsync("el=>{window.qaClipboard=new DataTransfer();el.dispatchEvent(new ClipboardEvent('cut',{clipboardData:window.qaClipboard,bubbles:true,cancelable:true}));}");
        await Assertions.Expect(textbox.Locator("img")).ToHaveCountAsync(0); await Command(page, "undo").ClickAsync(); Assert.Equal(beforeCut, await json.InputValueAsync());
        fixture.Source = beforeCut; fixture.Editable = false; await fixture.OpenAsync(); await textbox.FocusAsync(); await page.Keyboard.PressAsync("Control+a");
        await textbox.EvaluateAsync("el=>el.dispatchEvent(new ClipboardEvent('cut',{clipboardData:new DataTransfer(),bubbles:true,cancelable:true}))"); Assert.Equal(beforeCut, await json.InputValueAsync());
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Clipboard_inheritance_hidden_text_table_context_and_forged_copy_tokens_are_bounded()
    {
        await using var fixture = await BrowserFixture.CreateAsync(ClipboardSource); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        var page = fixture.Page; await fixture.OpenAsync(); await PageBreakCaret(page, 1, 0); var json = page.Locator("#document-json");
        await ClipboardPaste(page, """
            <style>p.specific{color:#15803d} p{color:#b91c1c}</style><div style="text-align:center;line-height:2">
            <p class="specific">INHERITED <span style="display:none">HIDDEN_INLINE</span></p><p style="display:none">HIDDEN_BLOCK</p></div>
            <pre>PRE_ONE
            PRE_TWO</pre><p><b style="mso-bidi-font-weight:normal">BIDI_BOLD</b></p><table><tr><td style="border-color:#15803d"><p>EXISTING_CELL</p></td></tr></table>
            """);
        var pasted = await json.InputValueAsync(); Assert.DoesNotContain("HIDDEN_", pasted); Assert.Contains("hardBreak", pasted); Assert.True(DocumentContentDescriptor.RichV10.TryRead(pasted, out _));
        using (var body = JsonDocument.Parse(pasted)) {
            var inherited = body.RootElement.GetProperty("content").EnumerateArray().Single(n => n.GetProperty("type").GetString() == "paragraph" && n.TryGetProperty("content", out var children) && children.EnumerateArray().Any(c => c.TryGetProperty("text", out var t) && t.GetString()!.Contains("INHERITED")));
            Assert.Equal("center", inherited.GetProperty("attrs").GetProperty("textAlign").GetString()); Assert.Equal(2, inherited.GetProperty("attrs").GetProperty("lineHeight").GetInt32());
            Assert.Contains("#15803d", inherited.GetRawText()); Assert.DoesNotContain("#b91c1c", inherited.GetRawText());
            var bold = ClipboardNodes(body.RootElement).Single(n => n.GetProperty("type").GetString() == "text" && n.GetProperty("text").GetString() == "BIDI_BOLD"); Assert.Contains("bold", bold.GetRawText());
            var cell = ClipboardNodes(body.RootElement).Single(n => n.GetProperty("type").GetString() == "tableCell"); Assert.Equal(JsonValueKind.Null, cell.GetProperty("attrs").GetProperty("borderWidthPt").ValueKind);
        }
        await page.Locator("#rich-text td p").ClickAsync(); await ClipboardPaste(page, "<table><tr><td>NESTED_ONE</td><td>NESTED_TWO</td></tr></table>");
        await Assertions.Expect(page.Locator("#rich-text table")).ToHaveCountAsync(1); var nested = await json.InputValueAsync(); Assert.Contains("NESTED_ONE", nested); Assert.Contains("NESTED_TWO", nested); Assert.True(DocumentContentDescriptor.RichV10.TryRead(nested, out _));
        await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("hücre metinlerine");
        await PageBreakCaret(page, 0, 0); await page.Locator("#rich-text [role=textbox]").EvaluateAsync("""
            el=>{const data=new DataTransfer();data.setData('application/x-etranslate-local-slice','forged-token');data.setData('text/html','<p>FORGED_SAFE<img data-asset-id="11111111-1111-1111-1111-111111111111" src="https://clipboard.invalid/x"></p>');el.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));}
            """);
        var forged = await json.InputValueAsync(); Assert.Contains("FORGED_SAFE", forged); Assert.DoesNotContain("assetId", forged); await Assertions.Expect(page.Locator("#rich-text img")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("görselleri");
        await page.Locator("#rich-text [role=textbox]").FocusAsync(); await page.Keyboard.PressAsync("Control+a");
        await ClipboardPaste(page, "<table><tr><td>REPLACE_ALL_CELL</td></tr></table><p style='page-break-before:always'>REPLACE_ALL_NEXT</p>");
        var replaced = await json.InputValueAsync(); Assert.DoesNotContain("CLIP_BEFORE", replaced); Assert.Contains("REPLACE_ALL_CELL", replaced); Assert.True(DocumentContentDescriptor.RichV10.TryRead(replaced, out _));
        await Assertions.Expect(page.Locator("#rich-text table")).ToHaveCountAsync(1); Assert.Equal(1, Starts(replaced));
        await page.Locator("#rich-text [role=textbox]").FocusAsync(); await page.Keyboard.PressAsync("Control+a");
        await ClipboardPaste(page, "<style>p.fragment{color:#15803d}</style><p class='fragment' style='text-align:right'>OUTSIDE_BEFORE<!--StartFragment --><b>SELECTED_FRAGMENT</b><!--EndFragment -->OUTSIDE_AFTER</p>");
        var fragment = await json.InputValueAsync(); Assert.Contains("SELECTED_FRAGMENT", fragment); Assert.DoesNotContain("OUTSIDE_", fragment); Assert.Contains("#15803d", fragment); Assert.Contains("right", fragment);
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }
}
