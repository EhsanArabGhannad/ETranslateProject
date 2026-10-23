using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETranslate.Contracts.Documents;
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
            var buttons = string.Join("", commands.Select(command => $"<button type='button' data-command='{command}' aria-pressed='false'>{command}</button>"));
            var metadata = Format is null && SchemaVersion is null ? "" : $"data-content-format='{WebUtility.HtmlEncode(Format)}' data-content-schema-version='{WebUtility.HtmlEncode(SchemaVersion)}'";
            var templateMetadata = TemplateFormat is null && TemplateSchemaVersion is null ? "" : $"data-template-content-format='{WebUtility.HtmlEncode(TemplateFormat)}' data-template-content-schema-version='{WebUtility.HtmlEncode(TemplateSchemaVersion)}'";
            return $$"""
                <!doctype html><html lang="tr"><head><meta charset="utf-8"><link rel="stylesheet" href="/css/site.css"></head><body>
                <main id="editor-workspace" data-rich-compatible="true" data-editable="{{Editable.ToString().ToLowerInvariant()}}" {{metadata}} {{templateMetadata}}>
                <section class="editor-controls"><p id="save-state">CLEAN</p>
                <form id="draft-form" action="/Workspace/SaveDraft" method="post"><input type="hidden" name="mode" value="{{(rich ? "text" : "json")}}">
                <div id="rich-region" hidden><div class="editor-toolbar">{{buttons}}</div><div id="rich-text"></div></div>
                <p id="editor-format-status"></p><label for="document-json">JSON</label>
                <textarea id="document-json" name="editorContentJson">{{WebUtility.HtmlEncode(Source)}}</textarea>
                <button type="submit">Kaydet</button></form>
                <form class="review-form" action="/Workspace/Review" method="post"><button type="submit">İncele</button></form><p id="review-status"></p>
                </section><div id="preview-notice"></div><div id="preview-watermark"></div><div id="preview-header"></div><div id="preview-body"></div><div id="preview-footer"></div>
                <textarea id="template-header" hidden></textarea><textarea id="template-footer" hidden></textarea><textarea id="template-watermark" hidden></textarea><textarea id="template-layout" hidden></textarea>
                </main><script type="module" src="/js/generated/workspace.js"></script></body></html>
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
