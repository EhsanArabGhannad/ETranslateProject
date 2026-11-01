using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorCompatibilityProductFactAttribute : FactAttribute
{
    public LocalEditorCompatibilityProductFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_COMPATIBILITY_SQL_TESTS") != "1")
            Skip = "Run Verify-EditorCompatibility.ps1 explicitly; fresh local QA databases only.";
        Timeout = 420000;
    }
}

public sealed class EditorCompatibilityAcceptanceTests
{
    [LocalEditorCompatibilityProductFact]
    public async Task Real_product_shell_keeps_narrow_landscape_and_desktop_edit_save_pdf_and_review_usable()
    {
        await using var host = new IsolatedSqlProductHost(
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_COMPATIBILITY_SERVER") ?? throw new InvalidOperationException("Explicit local SQL required."),
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_COMPATIBILITY_ROOT") ?? throw new InvalidOperationException("Explicit solution root required."), "editor-compatibility");
        List<object> cases = []; List<string> errors = [], external = [], csp = [];
        using var api = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        async Task<JsonElement> Api(string service, HttpMethod method, string path, object? body = null, int expected = 200)
        {
            using var request = new HttpRequestMessage(method, host.Urls[service] + path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await api.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
            Assert.True((int)response.StatusCode == expected, $"{service} {method} {path}: expected {expected}, got {(int)response.StatusCode}. {text[..Math.Min(300, text.Length)]}");
            if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { });
            using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
        }
        async Task<Guid> Upload(string path)
        {
            using var form = new MultipartFormDataContent(); var bytes = new ByteArrayContent(BodyImageFixture.Png);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(bytes, "file", "synthetic-source.png");
            using var response = await api.PostAsync(host.Urls["documents"] + path, form); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        try
        {
            await host.StartAsync();
            using var playwright = await Playwright.CreateAsync();
            var email = $"compatibility-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
            await Api("identity-access", HttpMethod.Post, "/api/v1/auth/register", new { email, password });
            var token = await Api("identity-access", HttpMethod.Post, "/api/v1/auth/login", new { email, password });
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
            var tenant = (await Api("identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = "Synthetic browser QA", slug = "compat-" + host.RunId, type = "TranslationOffice" }, 201)).GetProperty("id").GetGuid();
            var trial = false;
            for (var n = 0; n < 100; n++) { trial = (await Api("billing", HttpMethod.Get, $"/api/v1/tenants/{tenant}/subscription/write-access")).GetProperty("canWrite").GetBoolean(); if (trial) break; await Task.Delay(200); }
            Assert.True(trial);
            var template = (await Api("documents", HttpMethod.Post, $"/api/v1/tenants/{tenant}/document-templates", new {
                name = "Synthetic browser letterhead", editorContentJson = "{\"type\":\"doc\",\"content\":[]}",
                headerContentJson = """{"type":"header","content":[{"type":"paragraph","content":[{"type":"text","text":"SENTETİK ÜSTBİLGİ"}]}]}""",
                footerContentJson = """{"type":"footer","content":[{"type":"paragraph","content":[{"type":"text","text":"SON SAYFA ALTBİLGİ"}]}]}""",
                pageLayoutJson = (DocumentPageLayout.Default with { FooterPlacement = "LastPage" }).ToJson(),
                contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 1
            }, 201)).GetProperty("id").GetGuid();

            foreach (var profile in new (string Name, string Channel, int Width, int Height, bool Touch)[] { ("chrome-narrow", "chrome", 320, 740, true),
                ("chrome-landscape", "chrome", 844, 390, true), ("edge-desktop", "msedge", 1440, 1000, false) })
            {
                await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Channel = profile.Channel, ChromiumSandbox = true });
                await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = profile.Width, Height = profile.Height },
                    HasTouch = profile.Touch, IsMobile = profile.Touch, DeviceScaleFactor = profile.Touch ? 2 : 1, ServiceWorkers = ServiceWorkerPolicy.Block, Locale = "tr-TR" });
                await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('COMPAT_CSP:'+e.effectiveDirective));");
                await context.RouteAsync("**/*", async route => { if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"]) { external.Add(route.Request.Url); await route.AbortAsync(); } else await route.ContinueAsync(); });
                var page = await context.NewPageAsync(); page.SetDefaultTimeout(15000); page.PageError += (_, error) => errors.Add(error);
                page.Console += (_, msg) => { if (msg.Text.StartsWith("COMPAT_CSP:")) csp.Add(msg.Text); };
                page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
                async Task Activate(ILocator control) { if (profile.Touch) await control.TapAsync(); else await control.ClickAsync(); }
                async Task CheckWidth()
                {
                    Assert.Equal(profile.Width, await page.EvaluateAsync<int>("innerWidth"));
                    Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), profile.Name + ": outer overflow");
                    await EditorAdapterBrowserTests.AssertControlsInWidthAsync(page);
                }
                async Task Shot(string name) => await page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, profile.Name + "-" + name + ".png"), Scale = ScreenshotScale.Css });
                try
                {
                    await page.GotoAsync(host.Urls["web"] + "/Account/Login"); await page.Locator("#Email").FillAsync(email); await page.Locator("#Password").FillAsync(password);
                    await Activate(page.Locator(".auth-card button[type=submit]")); await page.WaitForURLAsync("**/Workspace");
                    var jobs = $"/api/v1/tenants/{tenant}/translation-jobs";
                    var job = (await Api("translation-workflow", HttpMethod.Post, jobs, new { title = "Synthetic browser " + profile.Name, sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201)).GetProperty("id").GetGuid();
                    var collection = jobs + $"/{job}/documents"; var doc = (await Api("documents", HttpMethod.Post, collection, expected: 201)).GetProperty("id").GetGuid(); var path = collection + "/" + doc;
                    await Api("documents", HttpMethod.Post, path + "/template", new { templateId = template, revisionNumber = 1 });
                    var revision = (await Api("documents", HttpMethod.Get, collection)).GetProperty("currentDraftRevision").GetInt32();
                    var image = await Upload(path + $"/images?expectedRevision={revision}"); var file = await Upload(path + "/source-files");
                    var source = EditorAdapterBrowserTests.CompatibilityDocument(image);
                    await Api("documents", HttpMethod.Post, path + "/draft-revisions", new { expectedCurrentRevision = revision, editorContentJson = source, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 201); revision++;
                    var editor = host.Urls["web"] + $"/Workspace/Editor?tenantId={tenant}&jobId={job}";
                    Assert.Equal(200, (await page.GotoAsync(editor))!.Status);
                    var textbox = page.Locator("#rich-text [role=textbox]"); var json = page.Locator("#document-json"); var viewport = page.Locator("#editor-viewport");
                    await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true"); Assert.Equal(source, await json.InputValueAsync());
                    await CheckWidth();
                    await page.Locator("#source-select").SelectOptionAsync(file.ToString());
                    await page.WaitForFunctionAsync("() => document.querySelector('#source-display img')?.naturalWidth > 0"); await CheckWidth();
                    await page.Locator("#source-display").ScrollIntoViewIfNeededAsync(); await Shot("source");
                    foreach (var id in new[] { "editor-table-settings", "editor-images", "editor-search" }) {
                        await Activate(page.Locator("#" + id + " > summary")); await CheckWidth(); await Activate(page.Locator("#" + id + " > summary"));
                    }
                    await Activate(page.Locator("#editor-focus-toggle")); await Activate(page.Locator("#editor-pages-toggle"));
                    await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged"); await CheckWidth();
                    var count = int.Parse((await viewport.GetAttributeAsync("data-page-count"))!);
                    await page.Locator("#editor-page-index").SelectOptionAsync(count.ToString());
                    await Activate(textbox.Locator(":scope > p").Last); await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync(" PRODUCT_EDIT");
                    var edited = await json.InputValueAsync(); Assert.Contains("PRODUCT_EDIT", edited);
                    await page.Locator("#editor-zoom").SelectOptionAsync("125"); await CheckWidth(); await page.Locator("#editor-zoom").SelectOptionAsync("fit");
                    await viewport.ScrollIntoViewIfNeededAsync(); await Shot("pages");
                    await Activate(page.Locator("#editor-focus-toggle"));
                    await Activate(page.Locator("#draft-form button[type=submit]"));
                    await Assertions.Expect(page.Locator("#draft-form input[name=expectedRevision]")).ToHaveValueAsync((revision + 1).ToString(), new() { Timeout = 60000 });
                    Assert.Equal(edited, await json.InputValueAsync()); await CheckWidth();
                    await Activate(page.Locator("#editor-pages-toggle")); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
                    var markers = await EditorAdapterBrowserTests.CompatibilityMarkerPagesAsync(page);
                    await File.WriteAllTextAsync(Path.Combine(host.Output, profile.Name + "-markers.json"), JsonSerializer.Serialize(new { pageCount = count, markers }));
                    await Activate(page.Locator("#draft-pdf-form button"));
                    await Assertions.Expect(page.Locator("#draft-pdf-list > div")).ToHaveCountAsync(1, new() { Timeout = 60000 });
                    var pdfLink = page.Locator("#draft-pdf-list a"); var pdfUrl = new Uri(new Uri(host.Urls["web"]), (await pdfLink.GetAttributeAsync("href"))!);
                    Assert.Equal(new Uri(host.Urls["web"]).Authority, pdfUrl.Authority);
                    var response = await page.APIRequest.GetAsync(pdfUrl.ToString()); Assert.Equal(200, response.Status);
                    var bytes = await response.BodyAsync(); await File.WriteAllBytesAsync(Path.Combine(host.Output, profile.Name + ".pdf"), bytes);
                    using (var stream = new MemoryStream(bytes)) using (var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import)) Assert.Equal(count, pdf.PageCount);
                    await Activate(page.Locator("#review-submit"));
                    // Pending requests also freeze the textbox. Require the reloaded server state,
                    // not merely that temporary pending lock, before asserting review success.
                    await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false", new() { Timeout = 60000 });
                    await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "false");
                    await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync(); Assert.Equal(edited, await json.InputValueAsync());
                    await CheckWidth(); await Shot("review-locked");
                    await using (var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options)) {
                        var drafts = await db.DraftRevisions.Where(x => x.DocumentId == doc).OrderBy(x => x.RevisionNumber).ToArrayAsync();
                        Assert.Equal(source, drafts.Single(x => x.RevisionNumber == revision).EditorContentJson); Assert.Equal(edited, drafts.Last().EditorContentJson); Assert.Equal(revision + 1, drafts.Last().RevisionNumber);
                        var pdf = Assert.Single(await db.PdfVersions.Where(x => x.DocumentId == doc).ToArrayAsync()); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), pdf.Sha256);
                        Assert.Equal(revision + 1, pdf.RevisionNumber); Assert.Equal(1, await db.Reviews.CountAsync(x => x.DocumentId == doc));
                    }
                    cases.Add(new { profile.Name, profile.Channel, profile.Width, profile.Height, profile.Touch, browserVersion = browser.Version, count, savedRevision = revision + 1, pdfDownloaded = true, reviewLocked = true, passed = true });
                }
                catch { await Shot("failure"); throw; }
            }
            Assert.Equal(3, cases.Count); Assert.Empty(errors); Assert.Empty(external); Assert.Empty(csp);
            var latest = Path.Combine(host.Root, "tmp/editor-compatibility"); Directory.CreateDirectory(latest);
            await File.WriteAllTextAsync(Path.Combine(latest, "latest-product.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally { await File.WriteAllTextAsync(Path.Combine(host.Output, "compatibility-report.json"), JsonSerializer.Serialize(new { runId = host.RunId, cases, errors, external, csp, customerDataUsed = false, realDevicesUsed = false }, new JsonSerializerOptions { WriteIndented = true })); }
    }
}
