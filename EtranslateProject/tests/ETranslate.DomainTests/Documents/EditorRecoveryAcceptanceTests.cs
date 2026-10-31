using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorRecoveryFactAttribute : FactAttribute
{
    public LocalEditorRecoveryFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_RECOVERY_TESTS") != "1") Skip = "Run Verify-EditorRecovery.ps1 explicitly; fresh local QA databases only.";
        Timeout = 420_000;
    }
}

public sealed class EditorRecoveryAcceptanceTests
{
    private readonly HttpClient api = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly List<object> cases = [];
    private readonly List<string> errors = [], external = [], csp = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid tenant;
    private sealed record Sample(string Name, Guid Job, Guid Id, Guid Image, Guid Source, string Body)
    {
        public string Collection(Guid tenant) => $"/api/v1/tenants/{tenant}/translation-jobs/{Job}/documents";
        public string Path(Guid tenant) => Collection(tenant) + "/" + Id;
        public string Editor(Guid tenant) => $"/Workspace/Editor?tenantId={tenant}&jobId={Job}";
    }

    [LocalEditorRecoveryFact]
    public async Task Offline_lost_responses_and_owned_service_outages_preserve_drafts_and_recover_without_duplicate_mutations()
    {
        await using var host = new IsolatedSqlProductHost(
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_RECOVERY_SERVER") ?? throw new InvalidOperationException("Explicit local SQL required."),
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_RECOVERY_ROOT") ?? throw new InvalidOperationException("Explicit solution root required."), "editor-recovery");
        try
        {
            await host.StartAsync();
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1100 }, ServiceWorkers = ServiceWorkerPolicy.Block });
            await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('RECOVERY_CSP:'+e.effectiveDirective));");
            await context.RouteAsync("**/*", async route => { if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"]) { external.Add(route.Request.Url); await route.AbortAsync(); } else await route.ContinueAsync(); });
            var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error);
            page.Console += (_, msg) => { if (msg.Text.StartsWith("RECOVERY_CSP:")) csp.Add(msg.Text); };
            page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
            var email = $"recovery-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
            await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/register", new { email, password });
            var token = await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/login", new { email, password });
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
            tenant = (await Api(host, "identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = "Synthetic recovery QA", slug = "recovery-" + host.RunId, type = "TranslationOffice" }, 201)).GetProperty("id").GetGuid();
            for (var n = 0; n < 100; n++) { if ((await Api(host, "billing", HttpMethod.Get, $"/api/v1/tenants/{tenant}/subscription/write-access")).GetProperty("canWrite").GetBoolean()) break; await Task.Delay(200); }
            await page.GotoAsync(host.Urls["web"] + "/Account/Login"); await page.Locator("#Email").FillAsync(email); await page.Locator("#Password").FillAsync(password);
            await page.Locator(".auth-card button[type=submit]").ClickAsync(); await page.WaitForURLAsync("**/Workspace");

            var d = await Seed(host, "offline-save"); await Open(host, page, d); await Type(page, " OFFLINE_UNSAVED");
            var source = await Source(page); var before = await Snapshot(host, d);
            try { await context.SetOfflineAsync(true); await SaveButton(page).ClickAsync(); await Unconfirmed(page, "#save-state", source); }
            finally { await context.SetOfflineAsync(false); }
            Assert.Equal(before, await Snapshot(host, d)); await SaveSuccessfully(page); await Saved(host, d, 2, source); await Archives(host, page, d);
            cases.Add(new { name = d.Name, failureBeforeSend = true, oneManualRetrySavedOnce = true, passed = true });

            d = await Seed(host, "lost-save-response"); await Open(host, page, d); await Type(page, " LOST_SAVE_UNSAVED"); source = await Source(page);
            await LoseResponse(host, page, "/Workspace/SaveDraft", () => SaveButton(page).ClickAsync(), 302);
            await Unconfirmed(page, "#save-state", source); await Saved(host, d, 2, source);
            var denied = page.WaitForResponseAsync(r => r.Url == host.Urls["web"] + "/Workspace/SaveDraft" && r.Request.Method == "POST");
            await SaveButton(page).ClickAsync(); Assert.Equal(409, (await denied).Status);
            await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
            Assert.Equal(source, await Source(page)); await Saved(host, d, 2, source); await Archives(host, page, d);
            await page.Locator("#save-state").ScrollIntoViewIfNeededAsync(); await page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, "lost-save-conflict.png") });
            cases.Add(new { name = d.Name, serverCommittedBeforeResponseLost = true, retryRejectedAsStale = true, revisions = 2, passed = true });

            d = await Seed(host, "lost-review-response"); await Open(host, page, d); source = await Source(page);
            await LoseResponse(host, page, "/Workspace/SubmitReview", () => page.Locator("#review-submit").ClickAsync(), 200);
            await Unconfirmed(page, "#review-status", source);
            await using (var db = Db(host)) { var review = Assert.Single(await db.Reviews.Where(x => x.DocumentId == d.Id).ToArrayAsync()); Assert.Equal(1, review.Round); Assert.Contains(d.Source.ToString(), review.SourceFilesJson); }
            await page.Locator("#review-submit").ClickAsync(); await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
            await Assertions.Expect(page.Locator("#review-status")).ToContainTextAsync("değişti"); Assert.Equal(source, await Source(page));
            await using (var db = Db(host)) Assert.Equal(1, await db.Reviews.CountAsync(x => x.DocumentId == d.Id));
            await Saved(host, d, 1, d.Body); await Archives(host, page, d);
            cases.Add(new { name = d.Name, committedRound = 1, retryDoesNotCreateAnotherRound = true, passed = true });

            d = await Seed(host, "lost-pdf-response", pdf: false); await Open(host, page, d); await Type(page, " PDF_LOCAL_UNSAVED"); source = await Source(page);
            await LoseResponse(host, page, "/Workspace/GenerateDraftPdf", () => page.Locator("#draft-pdf-form button").ClickAsync(), 200);
            await Unconfirmed(page, "#draft-pdf-status", source); Assert.Equal(0, await page.Locator("#draft-pdf-list > div").CountAsync());
            Guid pdfId; await using (var db = Db(host)) pdfId = (await db.PdfVersions.SingleAsync(x => x.DocumentId == d.Id)).Id;
            await page.Locator("#draft-pdf-form button").ClickAsync(); await Assertions.Expect(page.Locator($"#draft-pdf-list [data-pdf-id='{pdfId}']")).ToBeVisibleAsync();
            Assert.Equal(1, await page.Locator("#draft-pdf-list > div").CountAsync()); Assert.Equal(source, await Source(page));
            await using (var db = Db(host)) Assert.Equal(pdfId, (await db.PdfVersions.SingleAsync(x => x.DocumentId == d.Id)).Id);
            await Saved(host, d, 1, d.Body); await Archives(host, page, d);
            cases.Add(new { name = d.Name, oneArchivedPdf = true, manualRetryReturnsSamePdf = true, unsavedBodyWasNotPrintedOrSaved = true, passed = true });

            d = await Seed(host, "documents-outage"); await Open(host, page, d); await Type(page, " DOCUMENTS_DOWN_UNSAVED"); source = await Source(page); before = await Snapshot(host, d);
            await host.StopOwnedServiceAsync("documents");
            try
            {
                await SaveButton(page).ClickAsync(); await Assertions.Expect(page.Locator("#save-state")).ToContainTextAsync("doğrulanamadı", new() { Timeout = 60_000 });
                await Assertions.Expect(SaveButton(page)).ToBeEnabledAsync(); Assert.Equal(source, await Source(page)); Assert.Equal(before, await Snapshot(host, d));
            }
            finally { await host.RestartOwnedServiceAsync("documents"); }
            await SaveSuccessfully(page); await Saved(host, d, 2, source); await Archives(host, page, d);
            cases.Add(new { name = d.Name, ownedServiceStoppedAndRestarted = true, draftSurvived = true, recoveredWithOneNewRevision = true, passed = true });

            d = await Seed(host, "billing-outage"); await Open(host, page, d); await Type(page, " BILLING_DOWN_UNSAVED"); source = await Source(page); before = await Snapshot(host, d);
            await host.StopOwnedServiceAsync("billing");
            try
            {
                await Api(host, "documents", HttpMethod.Get, d.Collection(tenant)); await Archives(host, page, d);
                var denial = await Api(host, "documents", HttpMethod.Post, d.Path(tenant) + "/draft-revisions", new { expectedCurrentRevision = 1, editorContentJson = d.Body, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 503);
                Assert.Equal("subscription_unavailable", denial.GetProperty("code").GetString());
                await SaveButton(page).ClickAsync(); await Assertions.Expect(page.Locator("#save-state")).ToContainTextAsync("doğrulanamadı", new() { Timeout = 60_000 });
                Assert.Equal(source, await Source(page)); Assert.Equal(before, await Snapshot(host, d));
                var readonlyPage = await context.NewPageAsync();
                try { var read = await readonlyPage.GotoAsync(host.Urls["web"] + d.Editor(tenant), new() { Timeout = 60_000 }); Assert.Equal(200, read!.Status); await Assertions.Expect(readonlyPage.Locator("[data-subscription-readonly=true]")).ToBeVisibleAsync(); await Assertions.Expect(readonlyPage.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false"); }
                finally { await readonlyPage.CloseAsync(); }
            }
            finally { await host.RestartOwnedServiceAsync("billing"); }
            await SaveSuccessfully(page); await Saved(host, d, 2, source); await Archives(host, page, d);
            cases.Add(new { name = d.Name, unavailableIsNotExpired = true, newWritesFailedClosed = true, authorizedReadsPreserved = true, recoverySavedOnce = true, passed = true });
            Assert.Equal(6, cases.Count); Assert.Empty(errors); Assert.Empty(external); Assert.Empty(csp);
            var latest = Path.Combine(host.Root, "tmp/editor-recovery"); Directory.CreateDirectory(latest); await File.WriteAllTextAsync(Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally
        {
            api.Dispose();
            await File.WriteAllTextAsync(Path.Combine(host.Output, "recovery-report.json"), JsonSerializer.Serialize(new { runId = host.RunId, cases, errors, external, csp, customerDataUsed = false,
                networkScope = "Only the independent QA browser context; response-loss routes forward real mutations and then discard the response.",
                stoppedServices = new[] { "documents", "billing" }, existingServicesStopped = false }, Json));
        }
    }

    private static ILocator SaveButton(IPage page) => page.Locator("#draft-form button[type=submit]");
    private static Task<string> Source(IPage page) => page.Locator("#document-json").InputValueAsync();
    private static async Task Type(IPage page, string text) { await page.Locator("#rich-text [role=textbox]").PressAsync("Control+End"); await page.Keyboard.InsertTextAsync(text); Assert.Contains(text.Trim(), await Source(page)); }
    private async Task Open(IsolatedSqlProductHost host, IPage page, Sample d)
    {
        var response = await page.GotoAsync(host.Urls["web"] + d.Editor(tenant)); Assert.Equal(200, response!.Status);
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
    }
    private static async Task Unconfirmed(IPage page, string status, string source)
    {
        await Assertions.Expect(page.Locator(status)).ToContainTextAsync("sonucu doğrulanamadı");
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
        Assert.Equal(source, await Source(page));
    }
    private static async Task SaveSuccessfully(IPage page)
    {
        await Assertions.Expect(SaveButton(page)).ToBeEnabledAsync(); await SaveButton(page).ClickAsync();
        await Assertions.Expect(page.Locator("#draft-form input[name=expectedRevision]")).ToHaveValueAsync("2", new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator("#save-state")).ToHaveTextAsync("Otomatik kayıt yok");
    }
    private static async Task LoseResponse(IsolatedSqlProductHost host, IPage page, string path, Func<Task> click, int expected)
    {
        var url = host.Urls["web"] + path; var committed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(url, async route => {
            try { var response = await route.FetchAsync(new() { MaxRedirects = 0 }); var status = response.Status; await route.AbortAsync("failed"); committed.TrySetResult(status); }
            catch (Exception error) { committed.TrySetException(error); await route.AbortAsync("failed"); }
        });
        try { await click(); Assert.Equal(expected, await committed.Task.WaitAsync(TimeSpan.FromSeconds(60))); }
        finally { await page.UnrouteAsync(url); }
    }
    private static DocumentsDbContext Db(IsolatedSqlProductHost host) => new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
    private async Task Saved(IsolatedSqlProductHost host, Sample d, int revision, string body)
    {
        await using var db = Db(host); var doc = await db.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == d.Id);
        Assert.Equal(revision, doc.CurrentDraftRevision); var drafts = await db.DraftRevisions.AsNoTracking().Where(x => x.DocumentId == d.Id).OrderBy(x => x.RevisionNumber).ToArrayAsync();
        Assert.Equal(revision, drafts.Length); Assert.Equal(d.Body, drafts[0].EditorContentJson); Assert.Equal(body, drafts[^1].EditorContentJson); Assert.All(drafts, r => Assert.Equal(10, r.ContentSchemaVersion));
        Assert.Equal(1, await db.DocumentImages.CountAsync(x => x.DocumentId == d.Id)); Assert.Equal(1, await db.SourceFiles.CountAsync(x => x.DocumentId == d.Id));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task<string> Snapshot(IsolatedSqlProductHost host, Sample d)
    {
        await using var db = Db(host); var doc = await db.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == d.Id);
        return JsonSerializer.Serialize(new { doc.CurrentDraftRevision, doc.ReviewRound, doc.ReviewStatus,
            drafts = await db.DraftRevisions.AsNoTracking().Where(x => x.DocumentId == d.Id).OrderBy(x => x.RevisionNumber).Select(x => x.EditorContentJson).ToArrayAsync(),
            pdfs = await db.PdfVersions.CountAsync(x => x.DocumentId == d.Id), images = await db.DocumentImages.CountAsync(x => x.DocumentId == d.Id), sources = await db.SourceFiles.CountAsync(x => x.DocumentId == d.Id),
            blobs = Directory.GetFiles(host.Storage, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p => new { p, hash = Hash(File.ReadAllBytes(p)) }) });
    }
    private async Task Archives(IsolatedSqlProductHost host, IPage page, Sample d)
    {
        await using var db = Db(host);
        var image = await page.APIRequest.GetAsync(host.Urls["web"] + $"/Workspace/BodyImage?tenantId={tenant}&jobId={d.Job}&assetId={d.Image}"); Assert.Equal(200, image.Status); Assert.Equal(Hash(BodyImageFixture.Png), Hash(await image.BodyAsync()));
        var source = await page.APIRequest.GetAsync(host.Urls["web"] + $"/Workspace/Source?tenantId={tenant}&jobId={d.Job}&sourceFileId={d.Source}&download=true"); Assert.Equal(200, source.Status); Assert.Equal(Hash(BodyImageFixture.Png), Hash(await source.BodyAsync()));
        var pdf = await db.PdfVersions.AsNoTracking().SingleAsync(x => x.DocumentId == d.Id);
        var downloaded = await page.APIRequest.GetAsync(host.Urls["web"] + $"/Workspace/DraftPdf?tenantId={tenant}&jobId={d.Job}&pdfId={pdf.Id}"); Assert.Equal(200, downloaded.Status); Assert.Equal(pdf.Sha256, Hash(await downloaded.BodyAsync())); Assert.Equal(1, pdf.RevisionNumber);
    }
    private async Task<Sample> Seed(IsolatedSqlProductHost host, string name, bool pdf = true)
    {
        var jobs = $"/api/v1/tenants/{tenant}/translation-jobs";
        var job = (await Api(host, "translation-workflow", HttpMethod.Post, jobs, new { title = "Synthetic recovery " + name, sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201)).GetProperty("id").GetGuid();
        var collection = jobs + $"/{job}/documents"; var id = (await Api(host, "documents", HttpMethod.Post, collection, expected: 201)).GetProperty("id").GetGuid(); var path = collection + "/" + id;
        var image = await Upload(host, path + "/images?expectedRevision=0"); var file = await Upload(host, path + "/source-files");
        var body = JsonSerializer.Serialize(new { type = "doc", content = new object[] {
            new { type = "paragraph", attrs = new { dir = "rtl" }, content = new[] { new { type = "text", text = "RECOVERY_START ترجمهٔ آزمایشی İstanbul " + name } } },
            new { type = "image", attrs = new { assetId = image, assetScope = "document", widthPercent = 25, align = "center" } },
            new { type = "paragraph", attrs = new { pageBreakBefore = true }, content = new[] { new { type = "text", text = "RECOVERY_SECOND " + name } } }
        } });
        await Api(host, "documents", HttpMethod.Post, path + "/draft-revisions", new { expectedCurrentRevision = 0, editorContentJson = body, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 201);
        if (pdf) await Api(host, "documents", HttpMethod.Post, path + "/draft-revisions/1/pdfs", expected: 201);
        return new(name, job, id, image, file, body);
    }
    private async Task<Guid> Upload(IsolatedSqlProductHost host, string path)
    {
        using var form = new MultipartFormDataContent(); var image = new ByteArrayContent(BodyImageFixture.Png); image.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(image, "file", "recovery-fixture.png");
        using var response = await api.PostAsync(host.Urls["documents"] + path, form); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private async Task<JsonElement> Api(IsolatedSqlProductHost host, string service, HttpMethod method, string path, object? body = null, int expected = 200)
    {
        using var request = new HttpRequestMessage(method, host.Urls[service] + path); if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await api.SendAsync(request); var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == expected, $"{service} {method} {path}: expected {expected}, received {(int)response.StatusCode}. {text[..Math.Min(text.Length, 300)]}");
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { }); using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
    }
}
