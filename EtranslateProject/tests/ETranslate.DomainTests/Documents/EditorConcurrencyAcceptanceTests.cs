using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Endpoints;
using ETranslate.Documents.Api.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorConcurrencyFactAttribute : FactAttribute
{
    public LocalEditorConcurrencyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_CONCURRENCY_TESTS") != "1")
            Skip = "Run Verify-EditorConcurrency.ps1 explicitly; fresh local QA databases only.";
        Timeout = 300_000;
    }
}

public sealed class EditorConcurrencyAcceptanceTests
{
    private readonly HttpClient api = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly List<object> cases = [];
    private readonly List<string> errors = [], external = [], csp = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid tenant, actor, template, templateRevision;
    private sealed record Sample(string Name, Guid Job, Guid Id, Guid Image, Guid Source, Guid Pdf, string Body, string Files)
    {
        public string Collection(Guid tenant) => $"/api/v1/tenants/{tenant}/translation-jobs/{Job}/documents";
        public string Path(Guid tenant) => Collection(tenant) + "/" + Id;
        public string Editor(Guid tenant) => $"/Workspace/Editor?tenantId={tenant}&jobId={Job}";
    }
    private sealed record Pending(string Kind, IPage Page, Gate Gate, Task Click, string Source);
    private sealed class Gate : IAsyncDisposable
    {
        private readonly IPage page;
        private readonly string url;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IResponse> Response { get; }
        private Gate(IPage page, string url) { this.page = page; this.url = url; Response = page.WaitForResponseAsync(r => r.Url == url && r.Request.Method == "POST"); }
        public static async Task<Gate> Create(IPage page, string url)
        {
            var gate = new Gate(page, url);
            await page.RouteAsync(url, async route => {
                if (route.Request.Method == "POST") { gate.Reached.TrySetResult(); await gate.release.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
                await route.ContinueAsync();
            }); return gate;
        }
        public void Release() => release.TrySetResult();
        public async ValueTask DisposeAsync() { Release(); if (!page.IsClosed) await page.UnrouteAsync(url); }
    }

    [LocalEditorConcurrencyFact]
    public async Task Multiple_tabs_and_real_SQL_reject_stale_save_template_and_review_operations_without_lost_text()
    {
        await using var host = new IsolatedSqlProductHost(
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_CONCURRENCY_SERVER") ?? throw new InvalidOperationException("Explicit local SQL required."),
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_CONCURRENCY_ROOT") ?? throw new InvalidOperationException("Explicit solution root required."), "editor-concurrency");
        try
        {
            await host.StartAsync();
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1100 }, ServiceWorkers = ServiceWorkerPolicy.Block });
            await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('RACE_CSP:'+e.effectiveDirective));");
            await context.RouteAsync("**/*", async route => {
                if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"]) { external.Add(route.Request.Url); await route.AbortAsync(); }
                else await route.ContinueAsync();
            });
            var login = await NewPage(context);
            var email = $"concurrency-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
            await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/register", new { email, password });
            var token = await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/login", new { email, password });
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
            tenant = (await Api(host, "identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = "Synthetic concurrency QA", slug = "race-" + host.RunId, type = "TranslationOffice" }, 201)).GetProperty("id").GetGuid();
            actor = (await Api(host, "identity-access", HttpMethod.Get, $"/api/v1/tenants/{tenant}/access")).GetProperty("userId").GetGuid();
            for (var n = 0; n < 100; n++) { if ((await Api(host, "billing", HttpMethod.Get, $"/api/v1/tenants/{tenant}/subscription/write-access")).GetProperty("canWrite").GetBoolean()) break; await Task.Delay(200); }
            var target = await Api(host, "documents", HttpMethod.Post, $"/api/v1/tenants/{tenant}/document-templates", new {
                name = "Concurrent letterhead", editorContentJson = "{\"type\":\"doc\",\"content\":[]}", headerContentJson = "{\"type\":\"doc\",\"content\":[]}",
                footerContentJson = "{\"type\":\"doc\",\"content\":[]}", pageLayoutJson = DocumentPageLayout.Default.ToJson(), contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 1
            }, 201);
            template = target.GetProperty("id").GetGuid();
            templateRevision = (await Api(host, "documents", HttpMethod.Get, $"/api/v1/tenants/{tenant}/document-templates/{template}/revisions/1")).GetProperty("id").GetGuid();
            await login.GotoAsync(host.Urls["web"] + "/Account/Login"); await login.Locator("#Email").FillAsync(email); await login.Locator("#Password").FillAsync(password);
            await login.Locator(".auth-card button[type=submit]").ClickAsync(); await login.WaitForURLAsync("**/Workspace"); await login.CloseAsync();

            await BrowserRace(host, context, "two-tab-save", "save", "save", "together");
            foreach (var pair in new[] { ("save", "template"), ("save", "review"), ("template", "review") })
                foreach (var order in new[] { "left", "right" }) await BrowserRace(host, context, $"{pair.Item1}-{pair.Item2}-{order}", pair.Item1, pair.Item2, order);
            await BrowserRace(host, context, "double-review", "review", "review", "together");
            await BrowserRace(host, context, "competing-decisions", "approve", "return", "together");
            foreach (var pair in new[] { ("save", "template"), ("save", "review"), ("template", "review") }) await ApiRace(host, pair.Item1, pair.Item2);
            foreach (var other in new[] { "template", "review" }) foreach (var leftFirst in new[] { true, false }) await SqlStaleSnapshots(host, other, leftFirst);
            Assert.Equal(16, cases.Count); Assert.Empty(errors); Assert.Empty(external); Assert.Empty(csp);
            var latest = Path.Combine(host.Root, "tmp/editor-concurrency"); Directory.CreateDirectory(latest);
            await File.WriteAllTextAsync(Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally
        {
            api.Dispose();
            await File.WriteAllTextAsync(Path.Combine(host.Output, "concurrency-report.json"), JsonSerializer.Serialize(new {
                runId = host.RunId, cases, errors, external, csp, customerDataUsed = false,
                browserScheduling = "Requests held at the browser before forwarding; both ordered interleavings and together-release cases.",
                sqlScheduling = "Two EF contexts load the same real SQL rowversion before either commits; alternate the first commit."
            }, Json));
        }
    }

    private async Task BrowserRace(IsolatedSqlProductHost host, IBrowserContext context, string name, string leftKind, string rightKind, string order)
    {
        var d = await Seed(host, name);
        if (leftKind == "approve") await Api(host, "documents", HttpMethod.Post, d.Path(tenant) + "/reviews", Review(d), 201);
        var left = await NewPage(context); var right = await NewPage(context);
        Pending? a = null, b = null;
        try
        {
            await Prepare(host, left, d, leftKind, " TAB_LEFT"); await Prepare(host, right, d, rightKind, " TAB_RIGHT");
            a = await Queue(host, left, leftKind); b = await Queue(host, right, rightKind);
            await Task.WhenAll(a.Gate.Reached.Task, b.Gate.Reached.Task).WaitAsync(TimeSpan.FromSeconds(20));
            int l, r;
            if (order == "together") { a.Gate.Release(); b.Gate.Release(); var results = await Task.WhenAll(Settle(a), Settle(b)); l = results[0]; r = results[1]; }
            else if (order == "left") { a.Gate.Release(); l = await Settle(a); b.Gate.Release(); r = await Settle(b); }
            else { b.Gate.Release(); r = await Settle(b); a.Gate.Release(); l = await Settle(a); }
            bool leftWon;
            if (leftKind == "template" || rightKind == "template")
            {
                var current = await Api(host, "documents", HttpMethod.Get, d.Collection(tenant));
                var pinned = current.GetProperty("templateRevisionId").ValueKind != JsonValueKind.Null;
                leftWon = leftKind == "template" ? pinned : !pinned;
                var templateSide = leftKind == "template" ? a : b;
                var templateWon = leftKind == "template" ? leftWon : !leftWon;
                if (!templateWon) await Assertions.Expect(templateSide.Page.Locator("main > .notice[role=status]")).ToContainTextAsync("doğrulanamadı");
            }
            else { Assert.Single(new[] { l, r }, status => status != 409); leftWon = l != 409; }
            var winner = leftWon ? a : b; var loser = leftWon ? b : a;
            if (loser.Kind != "template")
            {
                Assert.Equal(409, leftWon ? r : l); await Locked(loser.Page, loser.Source);
                if (loser.Kind != "save") Assert.True(await loser.Page.Locator(".review-form button").EvaluateAllAsync<bool>("buttons=>buttons.every(b=>b.disabled)"));
                if (name == "save-review-left") { await loser.Page.Locator("#editor-focus-toggle").ClickAsync(); await loser.Page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, "review-conflict.png") }); }
                if (name == "two-tab-save") { await loser.Page.Locator("#editor-focus-toggle").ClickAsync(); await loser.Page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, "save-conflict.png") }); }
            }
            await Verify(host, d, winner.Kind, winner.Source);
            // A retransmission retains the old expected revision and cannot silently become a new save.
            if (winner.Kind == "save") { await Api(host, "documents", HttpMethod.Post, d.Path(tenant) + "/draft-revisions", Save(d, "RETRY_MUST_NOT_SAVE"), 409); await Verify(host, d, winner.Kind, winner.Source); }
            cases.Add(new { name, layer = "browser-and-services", order, winner = winner.Kind, leftStatus = l, rightStatus = r, localTextPreserved = true, passed = true });
        }
        finally { if (a is not null) await a.Gate.DisposeAsync(); if (b is not null) await b.Gate.DisposeAsync(); await left.CloseAsync(); await right.CloseAsync(); }
    }

    private async Task Prepare(IsolatedSqlProductHost host, IPage page, Sample d, string kind, string marker)
    {
        var path = kind == "template" ? $"/Workspace/DocumentTemplate?tenantId={tenant}&jobId={d.Job}" : d.Editor(tenant);
        var response = await page.GotoAsync(host.Urls["web"] + path); Assert.Equal(200, response!.Status);
        if (kind == "template") await page.Locator("#template-select").SelectOptionAsync(template.ToString());
        else
        {
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToBeVisibleAsync();
            if (kind == "save") { await page.Locator("#rich-text [role=textbox]").PressAsync("Control+End"); await page.Keyboard.InsertTextAsync(marker); Assert.Contains(marker.Trim(), await page.Locator("#document-json").InputValueAsync()); }
            if (kind == "return") await page.Locator("#note-RequestChanges").FillAsync("Synthetic competing correction");
        }
    }
    private static async Task<Pending> Queue(IsolatedSqlProductHost host, IPage page, string kind)
    {
        var action = kind switch { "save" => "SaveDraft", "template" => "ChangeDocumentTemplate", "review" => "SubmitReview", _ => "DecideReview" };
        var button = kind switch {
            "save" => page.Locator("#draft-form button[type=submit]"), "template" => page.Locator("#document-template-change-form button[type=submit]"),
            "review" => page.Locator("#review-submit"), _ => page.Locator($".review-form:has(input[name=reviewAction][value={(kind == "approve" ? "Approve" : "RequestChanges")}]) button[type=submit]") };
        var gate = await Gate.Create(page, host.Urls["web"] + "/Workspace/" + action);
        var source = kind == "template" ? "" : await page.Locator("#document-json").InputValueAsync();
        return new(kind, page, gate, button.ClickAsync(), source);
    }
    private static async Task<int> Settle(Pending operation)
    {
        var response = await operation.Gate.Response; await operation.Click;
        if (operation.Kind == "save" && response.Status == 302) await Assertions.Expect(operation.Page.Locator("#draft-form input[name=expectedRevision]")).ToHaveValueAsync("2");
        if (operation.Kind == "template") await operation.Page.WaitForURLAsync("**/Workspace/DocumentTemplate?**");
        if (response.Status == 409 && operation.Kind != "save") await Assertions.Expect(operation.Page.Locator("#review-status")).ToContainTextAsync("değişti");
        return response.Status;
    }
    private static async Task Locked(IPage page, string source)
    {
        await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false");
        await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
        Assert.Equal(source, await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync(); await page.Locator("#editor-page-index").SelectOptionAsync("2");
        Assert.Equal(source, await page.Locator("#document-json").InputValueAsync());
    }

    private async Task ApiRace(IsolatedSqlProductHost host, string left, string right)
    {
        var d = await Seed(host, "api-" + left + "-" + right);
        async Task<int> Send(string kind)
        {
            var suffix = kind == "save" ? "/draft-revisions" : kind == "template" ? "/template-change" : "/reviews";
            var body = kind == "save" ? Save(d, "API_SAVE") : kind == "template" ? Template() : Review(d);
            using var response = await api.PostAsJsonAsync(host.Urls["documents"] + d.Path(tenant) + suffix, body, Json); return (int)response.StatusCode;
        }
        var result = await Task.WhenAll(Send(left), Send(right)); Assert.Single(result, code => code is 200 or 201); Assert.Single(result, code => code == 409);
        var winner = result[0] == 409 ? right : left; await Verify(host, d, winner, Body("API_SAVE", d.Image));
        cases.Add(new { name = d.Name, layer = "concurrent-http", winner, statuses = result, passed = true });
    }
    private async Task SqlStaleSnapshots(IsolatedSqlProductHost host, string other, bool saveFirst)
    {
        var d = await Seed(host, $"sql-save-{other}-{saveFirst}");
        await using var left = Db(host); await using var right = Db(host);
        var a = await left.TranslationDocuments.SingleAsync(x => x.Id == d.Id); var b = await right.TranslationDocuments.SingleAsync(x => x.Id == d.Id);
        Assert.Equal(left.Entry(a).Property<byte[]>("RowVersion").CurrentValue, right.Entry(b).Property<byte[]>("RowVersion").CurrentValue);
        var old = await left.DraftRevisions.AsNoTracking().SingleAsync(x => x.DocumentId == d.Id);
        left.DraftRevisions.Add(a.AddDraftRevision(1, Body("SQL_SAVE", d.Image), null, actor, DateTimeOffset.UtcNow, DocumentContentDescriptor.RichFormat, 10, old));
        if (other == "template")
        {
            var target = await right.DocumentTemplates.Include(x => x.Revisions).SingleAsync(x => x.Id == template);
            right.DraftRevisions.Add(b.ChangeTemplate(1, target, target.Revisions.Single(), old, actor, DateTimeOffset.UtcNow));
        }
        else
        {
            var pdf = await right.PdfVersions.SingleAsync(x => x.Id == d.Pdf); var file = await right.SourceFiles.SingleAsync(x => x.DocumentId == d.Id);
            right.Reviews.Add(b.SubmitReview(1, 0, pdf, null, JsonSerializer.Serialize(new[] { new ReviewSourceFile(file.Id, file.OriginalFileName, file.SizeBytes, file.Sha256) }), actor, DateTimeOffset.UtcNow));
        }
        var first = saveFirst ? left : right; var stale = saveFirst ? right : left;
        await first.SaveChangesAsync(); var error = await Record.ExceptionAsync(() => stale.SaveChangesAsync());
        Assert.True(error is DbUpdateConcurrencyException || error is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }, error?.ToString() ?? "Stale SQL transaction unexpectedly committed.");
        var winner = saveFirst ? "save" : other; await Verify(host, d, winner, Body("SQL_SAVE", d.Image));
        cases.Add(new { name = d.Name, layer = "preloaded-real-SQL-rowversion", winner, staleTransactionRolledBack = true, passed = true });
    }

    private async Task Verify(IsolatedSqlProductHost host, Sample d, string winner, string source)
    {
        await using var db = Db(host); var doc = await db.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == d.Id);
        var drafts = await db.DraftRevisions.AsNoTracking().Where(x => x.DocumentId == d.Id).OrderBy(x => x.RevisionNumber).ToArrayAsync();
        var reviews = await db.Reviews.AsNoTracking().Where(x => x.DocumentId == d.Id).ToArrayAsync();
        var newDraft = winner is "save" or "template";
        Assert.Equal(newDraft ? 2 : 1, doc.CurrentDraftRevision); Assert.Equal(doc.CurrentDraftRevision, drafts.Length);
        Assert.Equal(d.Body, drafts[0].EditorContentJson); Assert.Null(drafts[0].TemplateRevisionId);
        Assert.Equal(winner == "save" ? source : d.Body, drafts[^1].EditorContentJson);
        Assert.Equal(winner == "template" ? templateRevision : (Guid?)null, doc.TemplateRevisionId); Assert.Equal(doc.TemplateRevisionId, drafts[^1].TemplateRevisionId);
        Assert.All(drafts, draft => { Assert.Equal(10, draft.ContentSchemaVersion); Assert.Equal(DocumentContentDescriptor.RichFormat, draft.ContentFormat); Assert.Equal(actor, draft.CreatedByUserId); });
        var reviewed = !newDraft; Assert.Equal(reviewed ? 1 : 0, reviews.Length); Assert.Equal(reviews.Length, doc.ReviewRound);
        var expectedState = winner == "approve" ? DocumentReviewStatus.Approved : winner == "return" ? DocumentReviewStatus.ChangesRequested : reviewed ? DocumentReviewStatus.AwaitingReview : DocumentReviewStatus.Draft;
        Assert.Equal(expectedState, doc.ReviewStatus);
        if (reviewed) { var review = Assert.Single(reviews); Assert.Equal(d.Pdf, review.PdfVersionId); Assert.Equal(1, review.RevisionNumber); Assert.Contains(d.Source.ToString(), review.SourceFilesJson); Assert.Equal(expectedState, review.Status); }
        Assert.Equal(1, await db.DocumentImages.CountAsync(x => x.DocumentId == d.Id)); Assert.Equal(1, await db.SourceFiles.CountAsync(x => x.DocumentId == d.Id));
        var pdf = await db.PdfVersions.AsNoTracking().SingleAsync(x => x.DocumentId == d.Id); Assert.Equal(d.Pdf, pdf.Id); Assert.Equal(1, pdf.RevisionNumber); Assert.Null(pdf.TemplateRevisionId);
        Assert.Equal(pdf.Sha256, Hash(await File.ReadAllBytesAsync(Path.Combine(host.Storage, pdf.StorageKey))).ToLowerInvariant());
        Assert.Equal(d.Files, Files(host));
    }
    private async Task<Sample> Seed(IsolatedSqlProductHost host, string name)
    {
        var jobs = $"/api/v1/tenants/{tenant}/translation-jobs";
        var job = (await Api(host, "translation-workflow", HttpMethod.Post, jobs, new { title = "Synthetic concurrency " + name, sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201)).GetProperty("id").GetGuid();
        var collection = jobs + $"/{job}/documents"; var id = (await Api(host, "documents", HttpMethod.Post, collection, expected: 201)).GetProperty("id").GetGuid(); var path = collection + "/" + id;
        var image = await Upload(host, path + "/images?expectedRevision=0"); var file = await Upload(host, path + "/source-files");
        var body = Body("SEED_" + name, image);
        await Api(host, "documents", HttpMethod.Post, path + "/draft-revisions", new { expectedCurrentRevision = 0, editorContentJson = body, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 201);
        var pdf = (await Api(host, "documents", HttpMethod.Post, path + "/draft-revisions/1/pdfs", expected: 201)).GetProperty("id").GetGuid();
        return new(name, job, id, image, file, pdf, body, Files(host));
    }
    private static string Body(string text, Guid image) => JsonSerializer.Serialize(new { type = "doc", content = new object[] {
        new { type = "paragraph", attrs = new { dir = "rtl" }, content = new[] { new { type = "text", text = "RACE_START ترجمهٔ آزمایشی İstanbul " + text } } },
        new { type = "image", attrs = new { assetId = image, assetScope = "document", widthPercent = 25, align = "center" } },
        new { type = "paragraph", attrs = new { pageBreakBefore = true }, content = new[] { new { type = "text", text = "RACE_SECOND " + text } } }
    } });
    private object Save(Sample d, string marker) => new { expectedCurrentRevision = 1, editorContentJson = Body(marker, d.Image), contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 };
    private object Template() => new { expectedCurrentRevision = 1, templateId = template, revisionNumber = 1 };
    private static object Review(Sample d) => new { expectedRevision = 1, expectedRound = 0, pdfVersionId = d.Pdf };
    private static DocumentsDbContext Db(IsolatedSqlProductHost host) => new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Files(IsolatedSqlProductHost host) => JsonSerializer.Serialize(Directory.GetFiles(host.Storage, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p => new { path = Path.GetRelativePath(host.Storage, p), hash = Hash(File.ReadAllBytes(p)) }));
    private async Task<IPage> NewPage(IBrowserContext context)
    {
        var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, msg) => { if (msg.Text.StartsWith("RACE_CSP:")) csp.Add(msg.Text); };
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync(); return page;
    }
    private async Task<Guid> Upload(IsolatedSqlProductHost host, string path)
    {
        using var form = new MultipartFormDataContent(); var image = new ByteArrayContent(BodyImageFixture.Png); image.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(image, "file", "race-fixture.png");
        using var response = await api.PostAsync(host.Urls["documents"] + path, form); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private async Task<JsonElement> Api(IsolatedSqlProductHost host, string service, HttpMethod method, string path, object? body = null, int expected = 200)
    {
        using var request = new HttpRequestMessage(method, host.Urls[service] + path); if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await api.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{service} {method} {path}: expected {expected}, received {(int)response.StatusCode}. {text[..Math.Min(300, text.Length)]}");
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { }); using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
    }
}
