using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETranslate.Contracts.Documents;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorCompatibilityTheoryAttribute : TheoryAttribute
{
    public LocalEditorCompatibilityTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_COMPATIBILITY_TESTS") != "1")
            Skip = "Run Verify-EditorCompatibility.ps1; installed Chrome/Edge, independent headless profiles, synthetic loopback host, no database.";
    }
}

public sealed partial class EditorAdapterBrowserTests
{
    [LocalEditorCompatibilityTheory(Timeout = 180000)]
    [InlineData("chrome-desktop", "chrome", 1440, 1000, 1, false)]
    [InlineData("edge-desktop", "msedge", 1440, 1000, 1, false)]
    [InlineData("chrome-narrow-touch", "chrome", 320, 740, 2, true)]
    [InlineData("chrome-phone-touch", "chrome", 390, 844, 3, true)]
    [InlineData("edge-phone-touch", "msedge", 390, 844, 3, true)]
    [InlineData("chrome-landscape-touch", "chrome", 844, 390, 3, true)]
    [InlineData("chrome-tablet-touch", "chrome", 768, 1024, 2, true)]
    [InlineData("edge-short-window", "msedge", 1024, 480, 1, false)]
    public async Task Target_browsers_keep_tools_pages_touch_edits_and_locks_reachable(
        string name, string channel, int width, int height, int density, bool touch)
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_COMPATIBILITY_OUTPUT")!);
        Directory.CreateDirectory(output);
        var source = CompatibilityDocument(BrowserFixture.ImageId);
        Assert.True(DocumentContentDescriptor.RichV10.TryRead(source, out _));
        await using var fixture = await BrowserFixture.CreateAsync(source, channel: channel, contextOptions: new() {
            ViewportSize = new() { Width = width, Height = height }, DeviceScaleFactor = density,
            HasTouch = touch, IsMobile = touch, Locale = "tr-TR"
        });
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        fixture.Header = """{"type":"header","content":[{"type":"paragraph","content":[{"type":"text","text":"SENTETİK ÜSTBİLGİ"}]}]}""";
        fixture.Footer = """{"type":"footer","content":[{"type":"paragraph","content":[{"type":"text","text":"SON SAYFA ALTBİLGİ"}]}]}""";
        fixture.Layout = (DocumentPageLayout.Default with { FooterPlacement = "LastPage" }).ToJson();
        var page = fixture.Page; page.SetDefaultTimeout(10000); var json = page.Locator("#document-json");
        var textbox = page.Locator("#rich-text [role=textbox]"); var viewport = page.Locator("#editor-viewport");
        async Task Activate(ILocator control) { if (touch) await control.TapAsync(); else await control.ClickAsync(); }
        async Task Screenshot(string stage) => await page.ScreenshotAsync(new() { Path = Path.Combine(output, name + "-" + stage + ".png"), Scale = ScreenshotScale.Css });
        try
        {
            await fixture.OpenAsync();
            Assert.Equal(width, await page.EvaluateAsync<int>("innerWidth"));
            Assert.Equal(source, await json.InputValueAsync());
            await AssertNoOuterOverflowAsync(page);
            // Opening every product tool panel must not force the outer document wider.
            foreach (var id in new[] { "editor-paragraph-advanced", "editor-table-settings", "editor-table-widths", "editor-cell-style", "editor-horizontal-merge", "editor-images", "editor-page-breaks" })
            {
                var panel = page.Locator("#" + id);
                await Activate(panel.Locator(":scope > summary"));
                await AssertNoOuterOverflowAsync(page);
                Assert.Equal(width, await page.EvaluateAsync<int>("innerWidth"));
                await AssertControlsInWidthAsync(page);
                if (id == "editor-table-settings") await Screenshot("table-picker");
                await Activate(panel.Locator(":scope > summary"));
            }
            await Activate(page.Locator("#editor-focus-toggle"));
            await Assertions.Expect(page.Locator(".editor-controls")).ToHaveAttributeAsync("aria-modal", "true");
            await AssertNoOuterOverflowAsync(page);
            await Activate(page.Locator("#editor-pages-toggle"));
            await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
            var count = int.Parse((await viewport.GetAttributeAsync("data-page-count"))!);
            Assert.InRange(count, 3, 20);
            await page.WaitForFunctionAsync("() => [...document.querySelectorAll('#rich-text img')].every(i => i.complete && i.naturalWidth > 0)");
            Assert.Equal(1, await textbox.Locator("img").CountAsync());
            Assert.Equal(count, await page.Locator(".pagination-header").CountAsync());
            Assert.Equal(1, await page.Locator(".pagination-footer").CountAsync());
            var markerPages = await CompatibilityMarkerPagesAsync(page);
            await File.WriteAllTextAsync(Path.Combine(output, name + "-markers.json"), JsonSerializer.Serialize(markerPages));
            Assert.Equal(1, markerPages["COMPAT_START"]); Assert.Equal(count, markerPages["COMPAT_LAST"]);
            foreach (var zoom in new[] { "125", "fit" })
            {
                await page.Locator("#editor-zoom").SelectOptionAsync(zoom);
                await page.Locator("#editor-page-index").SelectOptionAsync(count.ToString());
                await Assertions.Expect(viewport).ToHaveAttributeAsync("data-page-index", count.ToString());
                await AssertNoOuterOverflowAsync(page);
                Assert.Equal(source, await json.InputValueAsync());
            }
            await viewport.ScrollIntoViewIfNeededAsync(); await Screenshot("last-page");
            await Activate(textbox.Locator(":scope > p").Last);
            await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync(" COMPAT_EDIT_İış · ویرایش");
            await Assertions.Expect(json).ToHaveValueAsync(new Regex("COMPAT_EDIT_İış"));
            await Activate(Command(page, "undo")); await Assertions.Expect(textbox).Not.ToContainTextAsync("COMPAT_EDIT_İış");
            await Activate(Command(page, "redo")); await Assertions.Expect(textbox).ToContainTextAsync("COMPAT_EDIT_İış");
            await page.Locator("#editor-page-index").SelectOptionAsync("1");
            await Activate(textbox.Locator("td p").First);
            await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync(" CELL_EDIT");
            await Activate(Command(page, "addRowAfter"));
            Assert.Equal(48, await textbox.Locator("tr").CountAsync());
            await Activate(Command(page, "undo")); Assert.Equal(47, await textbox.Locator("tr").CountAsync());
            var edited = await json.InputValueAsync(); Assert.True(DocumentContentDescriptor.RichV10.TryRead(edited, out _));
            // Resizing models a smaller visual work area; it does not model an OS keyboard or IME.
            await page.SetViewportSizeAsync(width, Math.Max(300, height / 2));
            await AssertNoOuterOverflowAsync(page);
            await page.Locator("#editor-page-index").SelectOptionAsync(count.ToString());
            await viewport.ScrollIntoViewIfNeededAsync(); await Screenshot("short-height");
            await Activate(page.Locator("#editor-focus-toggle"));
            Assert.False(await page.Locator(".source-panel").EvaluateAsync<bool>("e => e.inert"));
            Assert.Equal(edited, await json.InputValueAsync());
            var retry = fixture.Enqueue(); await Activate(page.Locator("#draft-form button[type=submit]"));
            await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); await AssertFrozenAsync(page);
            Assert.Equal(edited, SubmittedJson(retry.Body)); retry.Release.SetResult(503);
            await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true");
            var saved = fixture.Enqueue(); await Activate(page.Locator("#draft-form button[type=submit]"));
            await saved.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Source = SubmittedJson(saved.Body); saved.Release.SetResult(303); await page.WaitForURLAsync("**after-save=1");
            Assert.Equal(edited, await json.InputValueAsync());
            await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
            fixture.Editable = false; await fixture.OpenAsync();
            await Activate(page.Locator("#editor-focus-toggle")); await Activate(page.Locator("#editor-pages-toggle"));
            await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
            await page.Locator("#editor-page-index").SelectOptionAsync(count.ToString());
            await AssertFrozenAsync(page); Assert.Equal(edited, await json.InputValueAsync());
            Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
                name, channel, browserVersion = fixture.BrowserVersion, width, height, density, touch,
                emulatedMobile = touch, pageCount = count, markerPages, passed = true,
                errors = fixture.Errors, unexpected = fixture.Unexpected,
                runtime = await page.EvaluateAsync<JsonElement>("() => ({userAgent:navigator.userAgent,devicePixelRatio,maxTouchPoints:navigator.maxTouchPoints})")
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { await Screenshot("failure"); throw; }
    }

    internal static string CompatibilityDocument(Guid image)
    {
        var body = JsonNode.Parse(TableDocument())!;
        body["content"]!.AsArray().Insert(0, JsonNode.Parse("""{"type":"paragraph","content":[{"type":"text","text":"COMPAT_START İstanbul · ترجمه با نیم‌فاصله"}]}"""));
        body["content"]!.AsArray().Insert(1, JsonNode.Parse("""{"type":"image","attrs":{"assetId":"$IMAGE$","assetScope":"document","widthPercent":40,"align":"right"}}""".Replace("$IMAGE$", image.ToString())));
        body["content"]!.AsArray().Add(JsonNode.Parse("""{"type":"paragraph","attrs":{"pageBreakBefore":true,"dir":"rtl"},"content":[{"type":"text","text":"COMPAT_LAST · پایان ترجمه"}]}"""));
        return body.ToJsonString();
    }

    internal static async Task AssertControlsInWidthAsync(IPage page)
    {
        var outside = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('.editor-controls button,.editor-controls input:not([type=hidden]),.editor-controls select,.editor-controls summary')]
                .filter(e => e.getClientRects().length && !e.closest('[hidden]'))
                .filter(e => {const r=e.getBoundingClientRect();return r.left < -1 || r.right > innerWidth + 1})
                .map(e => e.id || e.dataset.command || e.textContent.trim())
            """);
        Assert.True(outside.Length == 0, "Controls outside viewport: " + string.Join(", ", outside));
    }

    internal static async Task<Dictionary<string, int>> CompatibilityMarkerPagesAsync(IPage page) =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(await page.EvaluateAsync<string>("""
            () => {const root=document.querySelector('#rich-text [role=textbox]'),paper=document.querySelector('#editor-paper'),
                scale=new DOMMatrixReadOnly(getComputedStyle(paper).transform).a,pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),
                origin=root.getBoundingClientRect().left,result={},walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
                for(let n=walker.nextNode();n;n=walker.nextNode()) for(const m of n.data.matchAll(/COMPAT_START|COMPAT_LAST|MARK_\d{3}|END_MARKER/g)) {
                    const range=document.createRange();range.setStart(n,m.index);range.setEnd(n,m.index+m[0].length);
                    const rect=[...range.getClientRects()].find(r=>r.width>0);result[m[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;
                }return JSON.stringify(result);}
            """))!;
}
