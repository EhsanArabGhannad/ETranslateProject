using System.Text.Json;
using System.Text.RegularExpressions;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    [LocalEditorAdapterFact]
    public async Task Captured_Word15_clipboard_preserves_unicode_direction_header_cells_history_save_and_pdf()
    {
        var output = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!, "../../../tmp/pdfs/native-word")); Directory.CreateDirectory(output);
        foreach (var name in new[] { "typography", "tables", "unsupported" }) {
            using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/WordClipboard", "word15-" + name + ".json")));
            var value = sample.RootElement; Assert.True(value.GetProperty("provenance").GetProperty("captureEventTrusted").GetBoolean());
            const string empty = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}";
            await using var fixture = await BrowserFixture.CreateAsync(empty); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            var page = fixture.Page; await page.SetViewportSizeAsync(1600, 1250); await fixture.OpenAsync();
            await PageBreakCaret(page, 0, 0); await ClipboardPaste(page, value.GetProperty("html").GetString()!, value.GetProperty("text").GetString()!);
            var json = page.Locator("#document-json"); var source = await json.InputValueAsync();
            Assert.True(DocumentContentDescriptor.RichV10.TryRead(source, out var plain));
            foreach (var marker in value.GetProperty("markers").EnumerateArray()) Assert.Contains(marker.GetString()!, plain);
            using (var document = JsonDocument.Parse(source)) {
                var nodes = ClipboardNodes(document.RootElement).ToArray();
                if (name == "typography") {
                    var rtl = document.RootElement.GetProperty("content").EnumerateArray().Single(p => p.TryGetProperty("content", out var content) && content.EnumerateArray().Any(t => t.TryGetProperty("text", out var text) && text.GetString()!.Contains("RW_STYLE")));
                    Assert.Equal("rtl", rtl.GetProperty("attrs").GetProperty("dir").GetString());
                    Assert.Contains("نیم‌فاصله", plain); Assert.Equal(1, DocumentPageBreak.Count(document.RootElement));
                    Assert.Contains(nodes, n => n.GetProperty("type").GetString() == "paragraph" && (!n.TryGetProperty("content", out var c) || c.GetArrayLength() == 0));
                    Assert.DoesNotContain(nodes, n => n.GetProperty("type").GetString() == "text" && n.GetProperty("text").GetString() == "\u00a0");
                }
                if (name == "tables") {
                    var table = nodes.Single(n => n.GetProperty("type").GetString() == "table"); Assert.Equal(27, table.GetProperty("content").GetArrayLength());
                    Assert.All(table.GetProperty("content")[0].GetProperty("content").EnumerateArray(), c => Assert.Equal("tableHeader", c.GetProperty("type").GetString()));
                    Assert.Contains(nodes, n => n.GetProperty("type").GetString() == "tableCell" && n.GetProperty("attrs").GetProperty("colspan").GetInt32() == 2);
                    Assert.Equal(2, nodes.Count(n => n.GetProperty("type").GetString() is "bulletList" or "orderedList"));
                }
                if (name == "unsupported") {
                    Assert.DoesNotContain(nodes, n => n.GetProperty("type").GetString() is "table" or "image");
                    Assert.DoesNotContain("file:///", source); Assert.DoesNotContain("data:image", source);
                    await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("hücre metinlerine");
                    await Assertions.Expect(page.Locator("#editor-paste-status")).ToContainTextAsync("görselleri");
                }
            }
            await Command(page, "undo").ClickAsync(); Assert.True(DocumentContentDescriptor.RichV10.TryRead(await json.InputValueAsync(), out var undone)); Assert.Equal("", undone);
            await Command(page, "redo").ClickAsync(); Assert.Equal(source, await json.InputValueAsync());
            var save = fixture.Enqueue(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await save.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Source = SubmittedJson(save.Body); save.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1"); Assert.Equal(source, await json.InputValueAsync());
            await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync(); await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
            var map = await page.EvaluateAsync<string>("""
                ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,markers={};
                const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);for(let n=walker.nextNode();n;n=walker.nextNode())for(const m of n.data.matchAll(/RW_[A-Z]+(?:_[A-Z0-9]+)*/g)){const r=document.createRange();r.setStart(n,m.index);r.setEnd(n,m.index+m[0].length);const rect=[...r.getClientRects()].find(r=>r.width>0);markers[m[0]]??=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;}
                return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers});}
                """);
            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), map); await File.WriteAllTextAsync(Path.Combine(output, name + ".saved.json"), source);
            using var renderer = new ChromiumDraftPdfRenderer(); var bytes = await renderer.RenderAsync(new DraftPdfInput(Guid.NewGuid(), 1, null, source, null, null, null, null, new Dictionary<Guid, PdfImage>()) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 10 }, CancellationToken.None);
            await File.WriteAllBytesAsync(Path.Combine(output, name + ".pdf"), bytes);
            if (name == "typography") { await page.Locator("#editor-focus-toggle").ClickAsync(); await fixture.ScreenshotAsync("native-word-editor.png"); }
            Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }
}
