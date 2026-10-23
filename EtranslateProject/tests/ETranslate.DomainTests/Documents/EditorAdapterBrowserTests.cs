using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETranslate.Contracts.Documents;
using ETranslate.Web.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorAdapterFactAttribute : FactAttribute
{
    public LocalEditorAdapterFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_TESTS") != "1")
            Skip = "Run Verify-EditorAdapter.ps1; production bundle, synthetic loopback HTTP, no database.";
    }
}

public sealed class EditorAdapterBrowserTests
{
    private const string Initial = """
        {"type":"doc","content":[{"type":"paragraph","attrs":{"dir":"rtl"},"content":[{"type":"text","text":"ترجمه با نیم‌فاصله و شماره ۱۲۳"}]},{"type":"paragraph","attrs":{"dir":"ltr"},"content":[{"type":"text","text":"İstanbul şĞı Çeviri 123"}]}]}
        """;

    [LocalEditorAdapterFact]
    public async Task Production_host_preserves_edits_history_tables_and_save_retry_conflict_locks()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial);
        var page = fixture.Page;
        await fixture.OpenAsync();
        var textbox = page.Locator("#rich-text [role=textbox]");
        Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
        Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        await textbox.Locator("p").Last.ClickAsync();
        Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.InsertTextAsync(" ADAPTER_EDIT");
        await Assertions.Expect(textbox).ToContainTextAsync("ADAPTER_EDIT");
        await Command(page, "undo").ClickAsync();
        await Assertions.Expect(textbox).Not.ToContainTextAsync("ADAPTER_EDIT");
        await Command(page, "redo").ClickAsync();
        await Assertions.Expect(textbox).ToContainTextAsync("ADAPTER_EDIT");
        await Command(page, "bold").ClickAsync();
        await page.Keyboard.InsertTextAsync(" BOLD_MARKER");
        await Assertions.Expect(textbox.Locator("strong")).ToContainTextAsync("BOLD_MARKER");
        await Command(page, "insertTable").ClickAsync();
        Assert.Equal(3, await textbox.Locator("table tr").CountAsync());
        await Command(page, "addRowAfter").ClickAsync();
        Assert.Equal(4, await textbox.Locator("table tr").CountAsync());
        await Command(page, "addColumnAfter").ClickAsync();
        Assert.Equal(4, await textbox.Locator("table tr").First.Locator("th,td").CountAsync());
        await Command(page, "deleteColumn").ClickAsync();
        await Command(page, "deleteRow").ClickAsync();
        Assert.Equal(3, await textbox.Locator("table tr").CountAsync());
        Assert.Equal(1, await page.Locator("#preview-body table").CountAsync());

        var edited = await page.Locator("#document-json").InputValueAsync();
        Assert.True(DocumentContentJson.TryGetPlainText(edited, out var plain));
        Assert.Contains("نیم‌فاصله", plain); Assert.Contains("İstanbul şĞı", plain); Assert.Contains("ADAPTER_EDIT", plain);
        Assert.Contains("\"bold\"", edited);
        var retry = fixture.Enqueue();
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertFrozenAsync(page);
        Assert.Equal(edited, SubmittedJson(retry.Body));
        retry.Release.SetResult(503);
        await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true");
        Assert.Equal(edited, await page.Locator("#document-json").InputValueAsync());
        // The retry must recompute command eligibility, not indiscriminately enable all buttons.
        await Assertions.Expect(Command(page, "insertTable")).ToBeDisabledAsync();
        await Assertions.Expect(Command(page, "addRowAfter")).ToBeEnabledAsync();

        var success = fixture.Enqueue();
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await success.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Source = SubmittedJson(success.Body); success.Release.SetResult(303);
        try { await page.WaitForURLAsync("**/Workspace/Editor?after-save=1", new() { Timeout = 10000 }); }
        catch (TimeoutException error) { throw new InvalidOperationException("Save redirect: " + await page.Locator("#save-state").InnerTextAsync() +
            "; responses=" + string.Join(",", fixture.Responses) + "; unexpected=" + string.Join(",", fixture.Unexpected), error); }
        await Assertions.Expect(page.Locator("#rich-region")).ToBeVisibleAsync();
        Assert.Equal(edited, await page.Locator("#document-json").InputValueAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());
        Assert.Equal(1, await textbox.Locator("table").CountAsync());

        // A failed review releases only eligible controls and must not dirty the body.
        await textbox.Locator(":scope > p").First.ClickAsync();
        await Assertions.Expect(Command(page, "addRowAfter")).ToBeDisabledAsync();
        var review = fixture.Enqueue();
        await page.Locator(".review-form button").ClickAsync();
        await review.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertFrozenAsync(page); review.Release.SetResult(503);
        await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true");
        await Assertions.Expect(Command(page, "addRowAfter")).ToBeDisabledAsync();
        Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());

        await textbox.Locator(":scope > p").First.ClickAsync();
        await page.Keyboard.InsertTextAsync(" CONFLICT_EDIT ");
        var conflicted = await page.Locator("#document-json").InputValueAsync();
        var conflict = fixture.Enqueue();
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await conflict.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); conflict.Release.SetResult(409);
        await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
        await AssertFrozenAsync(page);
        Assert.Equal(conflicted, await page.Locator("#document-json").InputValueAsync());
        Assert.Empty(fixture.Unexpected); Assert.Empty(fixture.Errors);
    }

    [LocalEditorAdapterFact]
    public async Task Readonly_legacy_unsupported_and_table_limits_survive_the_adapter_boundary()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial, editable: false);
        var page = fixture.Page;
        await fixture.OpenAsync(); await AssertFrozenAsync(page);
        Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
        var review = fixture.Enqueue();
        await page.Locator(".review-form button").ClickAsync();
        await review.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); review.Release.SetResult(503);
        await Assertions.Expect(page.Locator("#review-status")).ToContainTextAsync("Synthetic review failure");
        await AssertFrozenAsync(page);

        fixture.Editable = true;
        fixture.Source = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":""}]}]}""";
        await fixture.OpenAsync();
        Assert.Equal(fixture.Source, await page.Locator("#document-json").InputValueAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());

        fixture.Source = """{"type":"doc","vendorMetadata":{"keep":"original"},"content":[]}""";
        // Even if the server's compatibility flag is true, the adapter revalidates and fails closed.
        await fixture.OpenAsync(rich: false);
        Assert.Equal(fixture.Source, await page.Locator("#document-json").InputValueAsync());
        Assert.True(await page.Locator("#document-json").IsVisibleAsync());
        Assert.True(await page.Locator("#rich-region").IsHiddenAsync());
        Assert.Equal("json", await page.Locator("input[name=mode]").InputValueAsync());

        fixture.Source = JsonSerializer.Serialize(new { type = "doc", content = new object[] {
            new { type = "table", content = Enumerable.Range(0, 50).Select(_ => new { type = "tableRow", content =
                Enumerable.Range(0, 12).Select(_ => new { type = "tableCell", content = new[] { new { type = "paragraph" } } }).ToArray() }).ToArray() }
        } });
        await fixture.OpenAsync();
        await page.Locator("#rich-text td p").First.ClickAsync();
        await Assertions.Expect(Command(page, "addRowAfter")).ToBeDisabledAsync();
        await Assertions.Expect(Command(page, "addColumnAfter")).ToBeDisabledAsync();
        Assert.True(DocumentContentJson.TryGetPlainText(await page.Locator("#document-json").InputValueAsync(), out _));
        Assert.Empty(fixture.Unexpected); Assert.Empty(fixture.Errors);
    }

    [LocalEditorAdapterFact]
    public async Task Stored_metadata_gates_editing_and_preview_even_if_a_stale_page_claims_compatibility()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial);
        var page = fixture.Page;
        foreach (var pair in new[] { (DocumentContentDescriptor.RichFormat, "1"), (DocumentContentDescriptor.LegacyDisplayFormat, "0") })
        {
            fixture.Format = pair.Item1; fixture.SchemaVersion = pair.Item2;
            await fixture.OpenAsync();
            Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
        }
        foreach (var pair in new[] { (DocumentContentDescriptor.RichFormat, "2"), ("sfdt", "1"), (DocumentContentDescriptor.RichFormat, "") })
        {
            fixture.Format = pair.Item1; fixture.SchemaVersion = pair.Item2;
            await fixture.OpenAsync(rich: false);
            await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
            Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
            Assert.True(await page.Locator("#document-json").EvaluateAsync<bool>("input => input.readOnly"));
            Assert.True(await page.Locator("#rich-region").IsHiddenAsync());
            await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("#preview-body")).Not.ToContainTextAsync("İstanbul");
        }
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "1";
        fixture.TemplateFormat = DocumentContentDescriptor.RichFormat; fixture.TemplateSchemaVersion = "2";
        await fixture.OpenAsync(rich: false);
        await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
        Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
        Assert.Empty(fixture.Unexpected); Assert.Empty(fixture.Errors);
    }

    [LocalEditorAdapterFact]
    public async Task Paper_zoom_and_focus_are_view_only_and_preserve_selection_history_and_locks()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial);
        var page = fixture.Page;
        await page.SetViewportSizeAsync(1440, 1000);
        await fixture.OpenAsync();
        var textbox = page.Locator("#rich-text [role=textbox]");
        var paper = page.Locator("#editor-paper");
        var toggle = page.Locator("#editor-focus-toggle");
        var zoom = page.Locator("#editor-zoom");
        await Assertions.Expect(page.Locator("#editor-view-controls")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#editor-paper-description")).ToContainTextAsync("A4");
        await Assertions.Expect(page.Locator("#editor-paper-description")).ToContainTextAsync("Otomatik sayfalama yoktur");
        var metrics = await textbox.EvaluateAsync<double[]>("e => [e.offsetWidth, e.offsetHeight, parseFloat(getComputedStyle(e).fontSize)]");
        await textbox.Locator("p").First.ClickAsync();
        await page.Keyboard.PressAsync("Home"); await page.Keyboard.PressAsync("Shift+ArrowLeft");
        var selection = await page.EvaluateAsync<string>("() => JSON.stringify({anchor: getSelection().anchorOffset, focus: getSelection().focusOffset, text: getSelection().toString()})");
        await paper.EvaluateAsync("e => { window.originalEditorNode = e.querySelector('[role=textbox]'); }");
        foreach (var scale in new[] { "75", "100", "125", "150", "fit" })
        {
            await zoom.SelectOptionAsync(scale);
            if (scale != "fit") await Assertions.Expect(page.Locator("#editor-zoom-status")).ToHaveTextAsync("%" + scale);
            Assert.Equal(metrics, await textbox.EvaluateAsync<double[]>("e => [e.offsetWidth, e.offsetHeight, parseFloat(getComputedStyle(e).fontSize)]"));
            Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
            Assert.Equal("CLEAN", await page.Locator("#save-state").InnerTextAsync());
            await Assertions.Expect(Command(page, "undo")).ToBeDisabledAsync();
        }
        Assert.Equal(selection, await page.EvaluateAsync<string>("() => JSON.stringify({anchor: getSelection().anchorOffset, focus: getSelection().focusOffset, text: getSelection().toString()})"));
        await fixture.ScreenshotAsync("paper-desktop.png");
        await textbox.Locator("p").Last.ClickAsync(); await page.Keyboard.PressAsync("End");
        await page.Keyboard.InsertTextAsync(" VIEW_EDIT");
        var edited = await page.Locator("#document-json").InputValueAsync();
        var beforeFocusSelection = await page.EvaluateAsync<int>("() => getSelection().anchorOffset");
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.Locator(".editor-controls")).ToHaveAttributeAsync("aria-modal", "true");
        Assert.True(await page.Locator(".source-panel").EvaluateAsync<bool>("e => e.inert"));
        Assert.True(await page.Locator(".topbar").EvaluateAsync<bool>("e => e.inert"));
        Assert.True(await textbox.EvaluateAsync<bool>("e => e === window.originalEditorNode"));
        Assert.Equal(beforeFocusSelection, await page.EvaluateAsync<int>("() => getSelection().anchorOffset"));
        Assert.Equal(edited, await page.Locator("#document-json").InputValueAsync());
        await Assertions.Expect(Command(page, "undo")).ToBeEnabledAsync();
        await fixture.ScreenshotAsync("paper-focus.png");
        await page.Keyboard.InsertTextAsync("_FOCUS");
        await Assertions.Expect(textbox).ToContainTextAsync("VIEW_EDIT_FOCUS");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.False(await page.Locator(".source-panel").EvaluateAsync<bool>("e => e.inert"));
        await Command(page, "undo").ClickAsync();
        await Assertions.Expect(textbox).Not.ToContainTextAsync("VIEW_EDIT");
        await Command(page, "redo").ClickAsync();
        await Assertions.Expect(textbox).ToContainTextAsync("VIEW_EDIT_FOCUS");

        // Keyboard entry, tab containment, return focus and pre-existing inert state.
        await page.Locator(".source-panel").EvaluateAsync("e => e.inert = true");
        await toggle.FocusAsync(); await page.Keyboard.PressAsync("Enter");
        await page.Locator(".review-form button").FocusAsync(); await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator(".review-form button")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape"); await Assertions.Expect(toggle).ToBeFocusedAsync();
        Assert.True(await page.Locator(".source-panel").EvaluateAsync<bool>("e => e.inert"));
        await page.Locator(".source-panel").EvaluateAsync("e => e.inert = false");

        await textbox.Locator("p").Last.ClickAsync(); await page.Keyboard.PressAsync("End");
        await Command(page, "insertTable").ClickAsync();
        await textbox.Locator("td p").First.ClickAsync(); await page.Keyboard.InsertTextAsync("İstanbul · جدول");
        var tableWidth = await textbox.Locator("table").EvaluateAsync<int>("e => e.offsetWidth");
        await toggle.ClickAsync(); await zoom.SelectOptionAsync("125");
        await Assertions.Expect(page.Locator("#editor-zoom-status")).ToHaveTextAsync("%125");
        Assert.Equal(tableWidth, await textbox.Locator("table").EvaluateAsync<int>("e => e.offsetWidth"));
        Assert.Equal(3, await textbox.Locator("table tr").CountAsync());
        await fixture.ScreenshotAsync("paper-table.png");
        await page.Keyboard.PressAsync("Escape");

        // The same view controls remain usable during save and after a conflict, without unlocking text.
        var conflict = fixture.Enqueue();
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await conflict.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await toggle.ClickAsync(); await zoom.SelectOptionAsync("125"); await AssertFrozenAsync(page);
        conflict.Release.SetResult(409);
        await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
        await page.Keyboard.PressAsync("Escape"); await AssertFrozenAsync(page);
        Assert.Equal(SubmittedJson(conflict.Body), await page.Locator("#document-json").InputValueAsync());

        fixture.Editable = false; await fixture.OpenAsync();
        await toggle.ClickAsync(); await zoom.SelectOptionAsync("75"); await AssertFrozenAsync(page);
        Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
        await page.Keyboard.PressAsync("Escape"); await AssertFrozenAsync(page);
        Assert.Empty(fixture.Unexpected); Assert.Empty(fixture.Errors);
    }

    [LocalEditorAdapterFact]
    public async Task Paper_view_fits_mobile_keeps_long_documents_reachable_and_rejects_unknown_layout()
    {
        var content = Enumerable.Range(1, 90).Select(i => new {
            type = "paragraph", attrs = new { dir = i % 2 == 0 ? "rtl" : "ltr" }, content = new[] {
                new { type = "text", text = $"{i} — ترجمه آزمایشی با نیم‌فاصله · İstanbul şĞı Çeviri. Synthetic document for local layout checks only." }
            }
        }).ToArray();
        await using var fixture = await BrowserFixture.CreateAsync(JsonSerializer.Serialize(new { type = "doc", content }));
        var page = fixture.Page;
        await page.SetViewportSizeAsync(390, 844); await fixture.OpenAsync();
        var zoom = page.Locator("#editor-zoom"); var viewport = page.Locator("#editor-viewport");
        await AssertNoOuterOverflowAsync(page);
        Assert.True(await viewport.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"));
        Assert.True(await viewport.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"));
        await page.Locator("#editor-focus-toggle").ClickAsync();
        await fixture.ScreenshotAsync("paper-mobile.png");
        await AssertNoOuterOverflowAsync(page);
        var width = await page.Locator("#rich-text [role=textbox]").EvaluateAsync<int>("e => e.offsetWidth");
        await zoom.SelectOptionAsync("150");
        await Assertions.Expect(page.Locator("#editor-zoom-status")).ToHaveTextAsync("%150");
        Assert.True(await viewport.EvaluateAsync<bool>("e => e.scrollWidth > e.clientWidth"));
        await AssertNoOuterOverflowAsync(page);
        await page.Locator("#rich-text p").Last.ClickAsync(); await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync(" LAST_PARAGRAPH_EDIT");
        await Assertions.Expect(page.Locator("#document-json")).ToHaveValueAsync(new Regex("LAST_PARAGRAPH_EDIT"));
        await zoom.SelectOptionAsync("fit");
        Assert.Equal(width, await page.Locator("#rich-text [role=textbox]").EvaluateAsync<int>("e => e.offsetWidth"));
        await page.Keyboard.PressAsync("Escape");

        // Geometry comes from the pinned template, including landscape and asymmetric margins.
        fixture.Layout = new DocumentPageLayout("A5", "Landscape", 10, 15, 20, 25).ToJson();
        await fixture.OpenAsync();
        await Assertions.Expect(page.Locator("#editor-paper-description")).ToContainTextAsync("A5 · Yatay · 210 × 148");
        var geometry = await page.Locator("#editor-paper").EvaluateAsync<double[]>("e => { const s = getComputedStyle(e); return [parseFloat(s.width),parseFloat(s.paddingTop),parseFloat(s.paddingRight),parseFloat(s.paddingBottom),parseFloat(s.paddingLeft)]; }");
        var expected = new[] { 210d, 10, 15, 20, 25 }.Select(mm => mm * 96 / 25.4).ToArray();
        for (var i = 0; i < expected.Length; i++) Assert.InRange(geometry[i], expected[i] - 0.1, expected[i] + 0.1);
        await AssertNoOuterOverflowAsync(page);
        fixture.Layout = "{\"pageSize\":\"Unknown\"}"; await fixture.OpenAsync();
        Assert.True(await page.Locator("#editor-view-controls").IsHiddenAsync());
        await Assertions.Expect(page.Locator("#editor-paper-description")).ToContainTextAsync(UiText.Get("EditorPaperUnavailable"));
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
        Assert.Equal(fixture.Source, await page.Locator("#document-json").InputValueAsync());
        Assert.Empty(fixture.Unexpected); Assert.Empty(fixture.Errors);
    }

    private static async Task AssertNoOuterOverflowAsync(IPage page) => Assert.True(await page.EvaluateAsync<bool>(
        "() => document.documentElement.scrollWidth <= innerWidth + 1"), "Only the paper viewport may scroll horizontally, not the page.");

    private static ILocator Command(IPage page, string name) => page.Locator($"button[data-command={name}]");
    private static async Task AssertFrozenAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false");
        Assert.True(await page.Locator(".editor-toolbar button").EvaluateAllAsync<bool>("buttons => buttons.every(button => button.disabled)"));
    }
    private static string SubmittedJson(string body)
    {
        var match = Regex.Match(body, "name=\"editorContentJson\"\\r\\n\\r\\n(.*?)\\r\\n--", RegexOptions.Singleline);
        Assert.True(match.Success, "The production host must submit the unchanged editorContentJson field.");
        return match.Groups[1].Value;
    }

    private sealed class ResponseGate
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Body { get; set; } = "";
    }

    // Temporary loopback-only HTTP host. Never starts product APIs or touches account/SQL/storage.
    private sealed class BrowserFixture : IAsyncDisposable
    {
        private string origin = "";
        private readonly IPlaywright playwright;
        private readonly IBrowser browser;
        private readonly IBrowserContext context;
        private readonly WebApplication server;
        private readonly Queue<ResponseGate> responses = new();
        private readonly List<ResponseGate> allGates = [];
        private readonly string root;
        private bool rich = true;
        public IPage Page { get; }
        public List<string> Unexpected { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Responses { get; } = [];
        public string Source { get; set; }
        public bool Editable { get; set; }
        public string? Format { get; set; }
        public string? SchemaVersion { get; set; }
        public string? TemplateFormat { get; set; }
        public string? TemplateSchemaVersion { get; set; }
        public string? Layout { get; set; }
        private BrowserFixture(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page,
            WebApplication server, string source, bool editable)
        {
            this.playwright = playwright; this.browser = browser; this.context = context; Page = page; Source = source; Editable = editable;
            this.server = server;
            root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!);
        }
        public static async Task<BrowserFixture> CreateAsync(string source, bool editable = true)
        {
            var playwright = await Playwright.CreateAsync();
            IBrowser? browser = null; IBrowserContext? context = null; WebApplication? server = null;
            try
            {
                browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
                context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block, AcceptDownloads = false });
                var page = await context.NewPageAsync();
                var builder = WebApplication.CreateSlimBuilder();
                builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                server = builder.Build();
                var fixture = new BrowserFixture(playwright, browser, context, page, server, source, editable);
                server.Run(fixture.HandleAsync);
                await server.StartAsync();
                fixture.origin = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                page.PageError += (_, error) => fixture.Errors.Add(error);
                page.Console += (_, message) => { if (message.Type == "error" && message.Text.Contains("Content Security Policy")) fixture.Errors.Add(message.Text); };
                page.Response += (_, response) => fixture.Responses.Add(response.Status + " " + response.Url);
                page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
                await context.RouteAsync("**/*", async route => {
                    if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != fixture.origin)
                    { fixture.Unexpected.Add(route.Request.Url); await route.AbortAsync(); }
                    else await route.ContinueAsync();
                });
                return fixture;
            }
            catch
            {
                if (context is not null) await context.DisposeAsync();
                if (browser is not null) await browser.DisposeAsync();
                playwright.Dispose();
                if (server is not null) await server.DisposeAsync();
                throw;
            }
        }
        public ResponseGate Enqueue() { var gate = new ResponseGate(); responses.Enqueue(gate); allGates.Add(gate); return gate; }
        public async Task ScreenshotAsync(string name)
        {
            var directory = Path.GetFullPath(Path.Combine(root, "../../../tmp/editor-adapter"));
            Directory.CreateDirectory(directory);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(directory, name) });
        }
        public async Task OpenAsync(bool rich = true)
        {
            this.rich = rich;
            await Page.GotoAsync(origin + "/Workspace/Editor");
            if (rich) await Assertions.Expect(Page.Locator("#rich-region")).ToBeVisibleAsync();
            else await Assertions.Expect(Page.Locator("#editor-format-status")).Not.ToBeEmptyAsync();
        }
        private async Task HandleAsync(HttpContext request)
        {
            var path = request.Request.Path.Value;
            if (request.Request.Method == "POST" && path is "/Workspace/SaveDraft" or "/Workspace/Review" && responses.TryDequeue(out var gate))
            {
                using var reader = new StreamReader(request.Request.Body);
                gate.Body = await reader.ReadToEndAsync(request.RequestAborted); gate.Received.SetResult();
                var status = await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
                request.Response.StatusCode = status; request.Response.ContentType = "application/json";
                if (status == 303) request.Response.Headers.Location = "/Workspace/Editor?after-save=1";
                await request.Response.WriteAsync("{\"message\":\"Synthetic review failure\"}", request.RequestAborted);
                return;
            }
            if (request.Request.Method == "GET" && path == "/Workspace/Editor")
            {
                request.Response.ContentType = "text/html; charset=utf-8";
                request.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self'; style-src 'self'; img-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
                await request.Response.WriteAsync(Html(), request.RequestAborted); return;
            }
            var file = path switch {
                "/js/generated/workspace.js" => ("wwwroot/js/generated/workspace.js", "text/javascript"),
                "/css/site.css" => ("wwwroot/css/site.css", "text/css"),
                _ => default
            };
            if (request.Request.Method == "GET" && file.Item1 is not null)
            {
                request.Response.ContentType = file.Item2;
                await request.Response.Body.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(root, file.Item1)), request.RequestAborted); return;
            }
            Unexpected.Add(request.Request.Method + " " + path); request.Response.StatusCode = 404;
        }
        private string Html()
        {
            var commands = new[] { "bold", "italic", "underline", "paragraph", "heading", "bulletList", "orderedList", "rtl", "ltr", "auto", "undo", "redo", "insertTable", "addRowAfter", "addColumnAfter", "deleteRow", "deleteColumn", "deleteTable", "notSupported" };
            var labels = new[] { "KalinMetin", "ItalikMetin", "AltiCizili", "Paragraf", "Baslik", "MaddeIsaretliListe", "NumaraliListe", "SagdanSola", "SoldanSaga", "OtomatikYon", "GeriAl", "Yinele", "Baslikli33Tablo", "SonrakiSatir", "SonrakiSutun", "SatiriSil", "SutunuSil", "TabloyuSil" };
            var buttons = string.Join("", commands.Select((command, i) => $"<button type='button' data-command='{command}' aria-pressed='false' {(i == labels.Length ? "hidden" : "")}>{(i < 3 ? new[] { "<strong>B</strong>", "<em>I</em>", "<u>U</u>" }[i] : i < labels.Length ? UiText.Get(labels[i]) : command)}</button>"));
            var metadata = Format is null && SchemaVersion is null ? "" : $"data-content-format='{WebUtility.HtmlEncode(Format)}' data-content-schema-version='{WebUtility.HtmlEncode(SchemaVersion)}'";
            var templateMetadata = TemplateFormat is null && TemplateSchemaVersion is null ? "" : $"data-template-content-format='{WebUtility.HtmlEncode(TemplateFormat)}' data-template-content-schema-version='{WebUtility.HtmlEncode(TemplateSchemaVersion)}'";
            return $$"""
                <!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/css/site.css"></head><body>
                <header class="topbar"><a href="/Workspace/Editor">ETranslate · Yerel test</a></header><main class="shell"><h1>Çeviri stüdyosu · Deneme belgesi</h1>
                <fieldset class="subscription-lock"><div class="editor-grid" id="editor-workspace" data-rich-compatible="true" data-editable="{{Editable.ToString().ToLowerInvariant()}}" {{metadata}} {{templateMetadata}}>
                <section class="card source-panel"><h2>Kaynak belge</h2><div class="source-display">Yalnızca sentetik test verisi</div></section>
                <section class="card editor-controls"><div class="section-heading"><h2 id="editor-heading">{{UiText.Get("CeviriMetni")}}</h2><p id="save-state">CLEAN</p></div>
                <div id="editor-view-controls" class="editor-view-controls" role="group" aria-label="{{UiText.Get("EditorViewControls")}}" hidden>
                    <button type="button" class="secondary" id="editor-focus-toggle" aria-pressed="false">{{UiText.Get("EditorEnterFocus")}}</button>
                    <label for="editor-zoom">{{UiText.Get("EditorZoom")}}</label><select id="editor-zoom" aria-describedby="editor-paper-description"><option value="fit">{{UiText.Get("EditorFitWidth")}}</option><option value="75">75%</option><option value="100">100%</option><option value="125">125%</option><option value="150">150%</option></select>
                    <span id="editor-zoom-status" class="muted" role="status"></span>
                </div>
                <form id="draft-form" action="/Workspace/SaveDraft" method="post"><input type="hidden" name="mode" value="{{(rich ? "text" : "json")}}">
                <div id="rich-region" hidden><div class="editor-toolbar" role="toolbar" aria-label="{{UiText.Get("CeviriBicimlendirme")}}">{{buttons}}</div><p id="editor-paper-description" class="muted"></p>
                <div id="editor-viewport" class="editor-viewport" tabindex="0" role="region" aria-label="{{UiText.Get("EditorPaperRegion")}}" aria-describedby="editor-paper-description"><div id="editor-paper-stage"><div id="editor-paper"><div id="rich-text"></div></div></div></div></div>
                <p id="editor-format-status"></p><label for="document-json">JSON</label>
                <textarea id="document-json" name="editorContentJson">{{WebUtility.HtmlEncode(Source)}}</textarea>
                <button type="submit">Kaydet</button></form>
                <form class="review-form" action="/Workspace/Review" method="post"><button type="submit">İncele</button></form><p id="review-status"></p>
                </section><div class="preview-panel" hidden><div id="preview-notice"></div><div id="preview-watermark"></div><div id="preview-header"></div><div id="preview-body"></div><div id="preview-footer"></div></div>
                <textarea id="template-header" hidden></textarea><textarea id="template-footer" hidden></textarea><textarea id="template-watermark" hidden></textarea><textarea id="template-layout" hidden>{{WebUtility.HtmlEncode(Layout)}}</textarea>
                </div></fieldset></main><script type="module" src="/js/generated/workspace.js"></script></body></html>
                """;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var gate in allGates) gate.Release.TrySetResult(503);
            await context.DisposeAsync(); await browser.DisposeAsync(); playwright.Dispose();
            await server.StopAsync(); await server.DisposeAsync();
        }
    }
}
