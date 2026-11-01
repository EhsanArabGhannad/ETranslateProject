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

public sealed class LocalEditorUploadFactAttribute : FactAttribute
{
    public LocalEditorUploadFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_UPLOAD_TESTS") != "1") Skip = "Run Verify-EditorUploads.ps1 explicitly; fresh local QA databases only.";
        Timeout = 420000;
    }
}

public sealed class EditorUploadAcceptanceTests
{
    private readonly HttpClient api = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly List<object> cases = [];
    private readonly List<string> errors = [], external = [], csp = [];
    private Guid tenant;
    private sealed record Sample(string Name, Guid Job, Guid Id, string Body, Guid? Pdf)
    {
        public string Path(Guid tenant) => $"/api/v1/tenants/{tenant}/translation-jobs/{Job}/documents/{Id}";
        public string Editor(Guid tenant) => $"/Workspace/Editor?tenantId={tenant}&jobId={Job}";
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static DocumentsDbContext Db(IsolatedSqlProductHost host) => new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
    private static Task<string> Source(IPage page) => page.Locator("#document-json").InputValueAsync();

    [LocalEditorUploadFact]
    public async Task Upload_retries_races_and_unknown_outcomes_preserve_source_and_create_one_owned_asset_per_identity()
    {
        await using var host = new IsolatedSqlProductHost(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_UPLOAD_SERVER")!, Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_UPLOAD_ROOT")!, "editor-uploads");
        try
        {
            await host.StartAsync(); using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1050 }, ServiceWorkers = ServiceWorkerPolicy.Block });
            await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('UPLOAD_CSP:'+e.effectiveDirective));");
            await context.RouteAsync("**/*", async route => { if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"]) { external.Add(route.Request.Url); await route.AbortAsync(); } else await route.ContinueAsync(); });
            var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error);
            page.Console += (_, msg) => { if (msg.Text.StartsWith("UPLOAD_CSP:")) csp.Add(msg.Text); }; page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
            var email = $"uploads-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
            await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/register", new { email, password });
            var token = await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/login", new { email, password });
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
            tenant = (await Api(host, "identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = "Synthetic upload QA", slug = "uploads-" + host.RunId, type = "TranslationOffice" }, 201)).GetProperty("id").GetGuid();
            var trial = false; for (var n = 0; n < 100; n++) { trial = (await Api(host, "billing", HttpMethod.Get, $"/api/v1/tenants/{tenant}/subscription/write-access")).GetProperty("canWrite").GetBoolean(); if (trial) break; await Task.Delay(200); } Assert.True(trial);
            await page.GotoAsync(host.Urls["web"] + "/Account/Login"); await page.Locator("#Email").FillAsync(email); await page.Locator("#Password").FillAsync(password);
            await page.Locator(".auth-card button[type=submit]").ClickAsync(); await page.WaitForURLAsync("**/Workspace");

            var d = await Seed(host, "offline-source"); await Open(host, page, d); await Type(page); var source = await Source(page); await Choose(page, false);
            try { await context.SetOfflineAsync(true); await UploadButton(page, false).ClickAsync(); await Unconfirmed(page, false, source); }
            finally { await context.SetOfflineAsync(false); }
            await AssertAssets(host, d, 0, 0); await UploadButton(page, false).ClickAsync(); await Confirmed(page, false); Assert.Equal(source, await Source(page)); await AssertAssets(host, d, 1, 0);
            cases.Add(new { name = d.Name, beforeSend = true, manualRetry = true, passed = true });

            foreach (var image in new[] { false, true }) {
                d = await Seed(host, image ? "lost-image" : "lost-source"); await Open(host, page, d); await Type(page); source = await Source(page); await Choose(page, image);
                var url = host.Urls["web"] + (image ? "/Workspace/UploadBodyImage" : "/Workspace/UploadSource");
                var committed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                await page.RouteAsync(url, async route => { try { var response = await route.FetchAsync(new() { MaxRedirects = 0 }); await route.AbortAsync("failed"); committed.SetResult(response.Status); } catch (Exception e) { committed.TrySetException(e); } });
                await UploadButton(page, image).ClickAsync(); Assert.Equal(200, await committed.Task.WaitAsync(TimeSpan.FromSeconds(60))); await Unconfirmed(page, image, source); await page.UnrouteAsync(url);
                await AssertAssets(host, d, image ? 0 : 1, image ? 1 : 0); await UploadButton(page, image).ClickAsync(); await Confirmed(page, image);
                await AssertAssets(host, d, image ? 0 : 1, image ? 1 : 0);
                if (!image) Assert.Equal(source, await Source(page)); else Assert.Contains("UPLOAD_LOCAL", await Source(page));
                cases.Add(new { name = d.Name, committedBeforeResponseLost = true, oneAssetAfterRetry = true, passed = true });
            }

            d = await Seed(host, "image-decode-retry"); await Open(host, page, d); await Choose(page, true); source = await Source(page);
            const string imageRoute = "**/Workspace/BodyImage?**";
            await page.RouteAsync(imageRoute, route => route.AbortAsync("failed")); await UploadButton(page, true).ClickAsync(); await Unconfirmed(page, true, source);
            await AssertAssets(host, d, 0, 1); await page.UnrouteAsync(imageRoute); await UploadButton(page, true).ClickAsync(); await Confirmed(page, true); await AssertAssets(host, d, 0, 1);
            await page.Locator("#editor-image-status").ScrollIntoViewIfNeededAsync(); await page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, "image-recovered.png") });
            cases.Add(new { name = d.Name, failedPrivateImageReadDoesNotDuplicateUpload = true, passed = true });

            d = await Seed(host, "pending-source"); await Open(host, page, d); await Choose(page, true); await Choose(page, false); source = await Source(page);
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var count = 0;
            var sourceUrl = host.Urls["web"] + "/Workspace/UploadSource";
            await page.RouteAsync(sourceUrl, async route => { count++; pending.TrySetResult(); await release.Task; try { await route.AbortAsync("failed"); } catch (PlaywrightException) { } });
            await page.Clock.InstallAsync(); await UploadButton(page, false).ClickAsync(); await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.EvaluateAsync("() => {for(const selector of ['#source-upload-form','#draft-form','.review-form'])document.querySelector(selector)?.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));document.querySelector('#editor-image-upload').dispatchEvent(new MouseEvent('click',{bubbles:true}));}");
            Assert.Equal(1, count); await AssertAssets(host, d, 0, 0);
            await page.Clock.FastForwardAsync(45001); await Unconfirmed(page, false, source); release.SetResult(); await page.UnrouteAsync(sourceUrl); await page.Clock.ResumeAsync();
            await UploadButton(page, false).ClickAsync(); await Confirmed(page, false); await AssertAssets(host, d, 1, 0);
            cases.Add(new { name = d.Name, deadline = 45, duplicateSubmitAndOtherWritesBlocked = true, passed = true });

            foreach (var image in new[] { false, true }) {
                d = await Seed(host, image ? "same-image-id" : "same-source-id"); var id = Guid.NewGuid();
                var responses = await Task.WhenAll(Upload(host, d, image, id), Upload(host, d, image, id));
                Assert.Contains(201, responses.Select(x => x.Status)); Assert.All(responses, r => { Assert.Contains(r.Status, new[] { 200, 201 }); Assert.Equal(id, r.Body.GetProperty("id").GetGuid()); });
                await AssertAssets(host, d, image ? 0 : 1, image ? 1 : 0);
                cases.Add(new { name = d.Name, statuses = responses.Select(x => x.Status).ToArray(), oneIdentity = id, passed = true });
            }

            d = await Seed(host, "identity-scope"); var other = await Seed(host, "identity-other-document");
            foreach (var image in new[] { false, true }) {
                var id = Guid.NewGuid(); Assert.Equal(201, (await Upload(host, d, image, id)).Status);
                var altered = (byte[])BodyImageFixture.Png.Clone(); altered[^1] ^= 1;
                Assert.Equal(409, (await Upload(host, d, image, id, altered)).Status);
                Assert.Equal(409, (await Upload(host, other, image, id)).Status);
                using var anonymous = new HttpClient(); using var form = Form(BodyImageFixture.Png);
                using var denial = await anonymous.PostAsync(host.Urls["documents"] + d.Path(tenant) + (image ? "/images?expectedRevision=1&" : "/source-files?") + "uploadId=" + id, form);
                Assert.Equal(HttpStatusCode.Unauthorized, denial.StatusCode);
            }
            await AssertAssets(host, d, 1, 1); await AssertAssets(host, other, 0, 0);
            cases.Add(new { name = d.Name, changedBytesOtherDocumentAndAnonymousDenied = true, passed = true });

            foreach (var image in new[] { false, true }) foreach (var review in new[] { false, true }) {
                d = await Seed(host, $"race-{(image ? "image" : "source")}-{(review ? "review" : "save")}", review); var id = Guid.NewGuid();
                var uploadTask = Upload(host, d, image, id);
                var operation = await ApiStatus(host, d.Path(tenant) + (review ? "/reviews" : "/draft-revisions"), review ? new { expectedRevision = 1, expectedRound = 0, pdfVersionId = d.Pdf } : (object)new { expectedCurrentRevision = 1, editorContentJson = d.Body, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 });
                var uploaded = await uploadTask; Assert.Contains(operation, new[] { 201, 409 }); Assert.Contains(uploaded.Status, new[] { 201, 409 }); Assert.True(operation == 201 || uploaded.Status == 201);
                await AssertAssets(host, d, !image && uploaded.Status == 201 ? 1 : 0, image && uploaded.Status == 201 ? 1 : 0, !review && operation == 201 ? 2 : 1);
                await using var db = Db(host); var doc = await db.TranslationDocuments.AsNoTracking().SingleAsync(x => x.Id == d.Id);
                if (review && operation == 201) {
                    Assert.True(doc.IsReviewLocked); var record = await db.Reviews.AsNoTracking().SingleAsync(x => x.DocumentId == d.Id);
                    if (!image) Assert.Equal(uploaded.Status == 201, record.SourceFilesJson.Contains(id.ToString()));
                    Assert.Equal(409, (await Upload(host, d, image, id)).Status);
                }
                cases.Add(new { name = d.Name, layer = "concurrent-http-valid-serializations", uploadStatus = uploaded.Status, otherStatus = operation, bothMaySucceedInValidOrder = true, passed = true });
            }
            Assert.Equal(12, cases.Count); await AssertStorage(host); Assert.Empty(errors); Assert.Empty(external); Assert.Empty(csp);
            var latest = Path.Combine(host.Root, "tmp/editor-uploads"); Directory.CreateDirectory(latest); await File.WriteAllTextAsync(Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally { api.Dispose(); await File.WriteAllTextAsync(Path.Combine(host.Output, "uploads-report.json"), JsonSerializer.Serialize(new { runId = host.RunId, cases, errors, external, csp, existingDataUsed = false }, new JsonSerializerOptions { WriteIndented = true })); }
    }

    private static ILocator UploadButton(IPage page, bool image) => page.Locator(image ? "#editor-image-upload" : "#source-upload-form button");
    private async Task Open(IsolatedSqlProductHost host, IPage page, Sample d) { await page.GotoAsync(host.Urls["web"] + d.Editor(tenant)); await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true"); }
    private static async Task Type(IPage page) { await page.Locator("#rich-text [role=textbox]").PressAsync("Control+End"); await page.Keyboard.InsertTextAsync(" UPLOAD_LOCAL"); }
    private static async Task Choose(IPage page, bool image) {
        if (image) { await page.Locator("#editor-images > summary").ClickAsync(); await page.Locator("#rich-text [role=textbox]").PressAsync("Control+End"); }
        await page.Locator(image ? "#editor-image-file" : "#source-file").SetInputFilesAsync(new FilePayload { Name = "synthetic.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
    }
    private static async Task Unconfirmed(IPage page, bool image, string source) {
        await Assertions.Expect(page.Locator(image ? "#editor-image-status" : "#source-status")).ToContainTextAsync("sonucu doğrulanamadı");
        await Assertions.Expect(UploadButton(page, image)).ToBeEnabledAsync(); Assert.Equal(source, await Source(page));
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
    }
    private static async Task Confirmed(IPage page, bool image) {
        if (image) await Assertions.Expect(page.Locator("#rich-text img")).ToHaveCountAsync(1);
        else await Assertions.Expect(page.Locator("#source-select option")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#source-file")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
    }
    private static MultipartFormDataContent Form(byte[] bytes) { var form = new MultipartFormDataContent(); var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(content, "file", "synthetic.png"); return form; }
    private async Task<(int Status, JsonElement Body)> Upload(IsolatedSqlProductHost host, Sample d, bool image, Guid id, byte[]? bytes = null) {
        using var form = Form(bytes ?? BodyImageFixture.Png); using var response = await api.PostAsync(host.Urls["documents"] + d.Path(tenant) + (image ? "/images?expectedRevision=1&" : "/source-files?") + "uploadId=" + id, form);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); return ((int)response.StatusCode, body);
    }
    private async Task AssertAssets(IsolatedSqlProductHost host, Sample d, int sources, int images, int revisions = 1) {
        await using var db = Db(host); Assert.Equal(sources, await db.SourceFiles.CountAsync(x => x.DocumentId == d.Id)); Assert.Equal(images, await db.DocumentImages.CountAsync(x => x.DocumentId == d.Id));
        var drafts = await db.DraftRevisions.AsNoTracking().Where(x => x.DocumentId == d.Id).OrderBy(x => x.RevisionNumber).ToArrayAsync(); Assert.Equal(revisions, drafts.Length); Assert.All(drafts, r => Assert.Equal(d.Body, r.EditorContentJson));
    }
    private static async Task AssertStorage(IsolatedSqlProductHost host) {
        await using var db = Db(host); var expected = (await db.SourceFiles.Select(x => new { Key = x.StorageKey, Hash = x.Sha256 }).ToArrayAsync())
            .Concat(await db.DocumentImages.Select(x => new { Key = x.StorageKey, Hash = x.Sha256 }).ToArrayAsync()).Concat(await db.PdfVersions.Select(x => new { Key = x.StorageKey, Hash = x.Sha256 }).ToArrayAsync()).ToArray();
        var actual = Directory.GetFiles(host.Storage, "*", SearchOption.AllDirectories); Assert.Equal(expected.Length, actual.Length);
        foreach (var item in expected) Assert.Equal(item.Hash, Hash(await File.ReadAllBytesAsync(Path.Combine(host.Storage, item.Key))));
        Assert.DoesNotContain(actual, name => name.EndsWith(".uploading", StringComparison.Ordinal));
    }
    private async Task<Sample> Seed(IsolatedSqlProductHost host, string name, bool pdf = false) {
        var jobs = $"/api/v1/tenants/{tenant}/translation-jobs"; var job = (await Api(host, "translation-workflow", HttpMethod.Post, jobs, new { title = "Synthetic upload " + name, sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201)).GetProperty("id").GetGuid();
        var id = (await Api(host, "documents", HttpMethod.Post, jobs + $"/{job}/documents", expected: 201)).GetProperty("id").GetGuid();
        const string body = """{"type":"doc","content":[{"type":"paragraph","attrs":{"dir":"rtl"},"content":[{"type":"text","text":"UPLOAD_BASE ترجمهٔ آزمایشی İstanbul"}]},{"type":"paragraph","content":[{"type":"text","text":"END_BASE"}]}]}""";
        var d = new Sample(name, job, id, body, null); await Api(host, "documents", HttpMethod.Post, d.Path(tenant) + "/draft-revisions", new { expectedCurrentRevision = 0, editorContentJson = body, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 201);
        if (pdf) d = d with { Pdf = (await Api(host, "documents", HttpMethod.Post, d.Path(tenant) + "/draft-revisions/1/pdfs", expected: 201)).GetProperty("id").GetGuid() }; return d;
    }
    private async Task<int> ApiStatus(IsolatedSqlProductHost host, string path, object body) { using var response = await api.PostAsJsonAsync(host.Urls["documents"] + path, body); return (int)response.StatusCode; }
    private async Task<JsonElement> Api(IsolatedSqlProductHost host, string service, HttpMethod method, string path, object? body = null, int expected = 200) {
        using var request = new HttpRequestMessage(method, host.Urls[service] + path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await api.SendAsync(request); var text = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == expected, $"{service} {path}: expected {expected}, got {(int)response.StatusCode}. {text[..Math.Min(text.Length, 300)]}");
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { }); using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
    }
}
