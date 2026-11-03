using System.Diagnostics;
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

public sealed class LocalEditorPerformanceProductFactAttribute : FactAttribute
{
    public LocalEditorPerformanceProductFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_PERFORMANCE_SQL_TESTS") != "1") Skip = "Run Verify-EditorPerformance.ps1 explicitly; fresh local QA databases only.";
        Timeout = 420000;
    }
}

public sealed class EditorPerformanceAcceptanceTests
{
    [LocalEditorPerformanceProductFact]
    public async Task Large_product_documents_save_print_reuse_archived_pdf_and_reject_over_page_limit_within_local_budgets()
    {
        await using var host = new IsolatedSqlProductHost(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_PERFORMANCE_SERVER")!, Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_PERFORMANCE_ROOT")!, "editor-performance");
        List<object> cases = []; List<string> errors = [], external = [], csp = [];
        using var api = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        async Task<JsonElement> Api(string service, HttpMethod method, string path, object? body = null, int expected = 200) {
            using var request = new HttpRequestMessage(method, host.Urls[service] + path); if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await api.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
            Assert.True((int)response.StatusCode == expected, $"{service} {path}: expected {expected}, got {(int)response.StatusCode}. {text[..Math.Min(300, text.Length)]}");
            if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { }); using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
        }
        try {
            await host.StartAsync(); using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1600, Height = 1100 }, ServiceWorkers = ServiceWorkerPolicy.Block });
            await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('PERFORMANCE_CSP:'+e.effectiveDirective));");
            await context.RouteAsync("**/*", async route => { if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"]) { external.Add(route.Request.Url); await route.AbortAsync(); } else await route.ContinueAsync(); });
            var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error); page.Console += (_, msg) => { if (msg.Text.StartsWith("PERFORMANCE_CSP:")) csp.Add(msg.Text); };
            page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
            var email = $"performance-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
            await Api("identity-access", HttpMethod.Post, "/api/v1/auth/register", new { email, password });
            var token = await Api("identity-access", HttpMethod.Post, "/api/v1/auth/login", new { email, password }); api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
            var tenant = (await Api("identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = "Synthetic performance QA", slug = "perf-" + host.RunId, type = "TranslationOffice" }, 201)).GetProperty("id").GetGuid();
            var trial = false; for (var n = 0; n < 100; n++) { trial = (await Api("billing", HttpMethod.Get, $"/api/v1/tenants/{tenant}/subscription/write-access")).GetProperty("canWrite").GetBoolean(); if (trial) break; await Task.Delay(200); } Assert.True(trial);
            var layout = (DocumentPageLayout.Default with { FooterPlacement = "LastPage" }).ToJson();
            var template = (await Api("documents", HttpMethod.Post, $"/api/v1/tenants/{tenant}/document-templates", new {
                name = "Synthetic performance letterhead", editorContentJson = "{\"type\":\"doc\",\"content\":[]}", headerContentJson = EditorPerformanceSamples.Header,
                footerContentJson = EditorPerformanceSamples.Footer, pageLayoutJson = layout, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 1
            }, 201)).GetProperty("id").GetGuid();
            await page.GotoAsync(host.Urls["web"] + "/Account/Login"); await page.Locator("#Email").FillAsync(email); await page.Locator("#Password").FillAsync(password); await page.Locator(".auth-card button[type=submit]").ClickAsync(); await page.WaitForURLAsync("**/Workspace");

            foreach (var name in new[] { "long-text", "mixed-images", "over-pages" }) {
                var jobs = $"/api/v1/tenants/{tenant}/translation-jobs";
                var job = (await Api("translation-workflow", HttpMethod.Post, jobs, new { title = "Synthetic performance " + name, sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201)).GetProperty("id").GetGuid();
                var collection = jobs + $"/{job}/documents"; var doc = (await Api("documents", HttpMethod.Post, collection, expected: 201)).GetProperty("id").GetGuid(); var path = collection + "/" + doc;
                await Api("documents", HttpMethod.Post, path + "/template", new { templateId = template, revisionNumber = 1 });
                var initialRevision = (await Api("documents", HttpMethod.Get, collection)).GetProperty("currentDraftRevision").GetInt32();
                var image = Guid.Empty;
                if (name == "mixed-images") {
                    using var form = new MultipartFormDataContent(); var data = new ByteArrayContent(BodyImageFixture.Png); data.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(data, "file", "synthetic.png");
                    using var uploaded = await api.PostAsync(host.Urls["documents"] + path + $"/images?expectedRevision={initialRevision}", form); Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode); image = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                }
                var body = EditorPerformanceSamples.Body(name, image); Assert.True(DocumentContentDescriptor.RichV10.TryRead(body, out _));
                await Api("documents", HttpMethod.Post, path + "/draft-revisions", new { expectedCurrentRevision = initialRevision, editorContentJson = body, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 201); initialRevision++;
                var watch = Stopwatch.StartNew(); await page.GotoAsync(host.Urls["web"] + $"/Workspace/Editor?tenantId={tenant}&jobId={job}");
                var textbox = page.Locator("#rich-text [role=textbox]"); var json = page.Locator("#document-json"); var viewport = page.Locator("#editor-viewport");
                await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true"); var readyMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); await page.Locator("#editor-pages-toggle").ClickAsync();
                if (name == "over-pages") {
                    await Assertions.Expect(page.Locator("#editor-pages-toggle")).ToHaveAttributeAsync("aria-pressed", "false");
                    await Assertions.Expect(page.Locator("#editor-pagination-status")).ToContainTextAsync("desteklenmiyor"); Assert.Equal(body, await json.InputValueAsync());
                    await Api("documents", HttpMethod.Post, path + $"/draft-revisions/{initialRevision}/pdfs", expected: 422);
                    await using var rejected = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
                    Assert.Equal(0, await rejected.PdfVersions.CountAsync(x => x.DocumentId == doc));
                    cases.Add(new { name, rejectedBeyondPageLimit = true, bodyPreserved = true, passed = true }); continue;
                }
                await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged"); await EditorAdapterBrowserTests.PerformanceFrames(page); var enableMs = watch.Elapsed.TotalMilliseconds;
                await textbox.Locator(":scope > p").First.EvaluateAsync("p=>{p.closest('[role=textbox]').focus();const r=document.createRange();r.selectNodeContents(p);r.collapse(false);const s=getSelection();s.removeAllRanges();s.addRange(r);}");
                await page.Keyboard.InsertTextAsync(" SERVER_EDIT"); var edited = await json.InputValueAsync();
                watch.Restart(); await page.Locator("#draft-form button[type=submit]").ClickAsync(); await Assertions.Expect(page.Locator("#draft-form input[name=expectedRevision]")).ToHaveValueAsync((initialRevision + 1).ToString(), new() { Timeout = 20000 });
                await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true"); Assert.Equal(edited, await json.InputValueAsync()); var saveReloadMs = watch.Elapsed.TotalMilliseconds;
                await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
                var count = int.Parse((await viewport.GetAttributeAsync("data-page-count"))!); var markers = await EditorAdapterBrowserTests.PerformanceMarkerPages(page);
                await File.WriteAllTextAsync(Path.Combine(host.Output, name + "-markers.json"), JsonSerializer.Serialize(new { pageCount = count, markers }));
                var button = page.Locator("#draft-pdf-form button"); watch.Restart(); await button.ClickAsync(); await Assertions.Expect(page.Locator("#draft-pdf-list > div")).ToHaveCountAsync(1, new() { Timeout = 30000 }); await Assertions.Expect(button).ToBeEnabledAsync(); var pdfMs = watch.Elapsed.TotalMilliseconds;
                var pdfId = await page.Locator("#draft-pdf-list > div").GetAttributeAsync("data-pdf-id");
                var url = new Uri(new Uri(host.Urls["web"]), (await page.Locator("#draft-pdf-list a").GetAttributeAsync("href"))!); Assert.Equal(new Uri(host.Urls["web"]).Authority, url.Authority);
                var downloaded = await page.APIRequest.GetAsync(url.ToString()); Assert.Equal(200, downloaded.Status); var bytes = await downloaded.BodyAsync(); await File.WriteAllBytesAsync(Path.Combine(host.Output, name + ".pdf"), bytes);
                using (var stream = new MemoryStream(bytes)) using (var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import)) Assert.Equal(count, pdf.PageCount);
                watch.Restart(); var cachedResponse = page.WaitForResponseAsync(r => new Uri(r.Url).AbsolutePath == "/Workspace/GenerateDraftPdf" && r.Request.Method == "POST"); await button.ClickAsync(); Assert.Equal(200, (await cachedResponse).Status); await Assertions.Expect(button).ToBeEnabledAsync(); var cachedMs = watch.Elapsed.TotalMilliseconds;
                Assert.Equal(1, await page.Locator("#draft-pdf-list > div").CountAsync()); Assert.Equal(pdfId, await page.Locator("#draft-pdf-list > div").GetAttributeAsync("data-pdf-id"));
                await using (var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options)) {
                    Assert.Equal(body, (await db.DraftRevisions.SingleAsync(x => x.DocumentId == doc && x.RevisionNumber == initialRevision)).EditorContentJson);
                    Assert.Equal(edited, (await db.DraftRevisions.SingleAsync(x => x.DocumentId == doc && x.RevisionNumber == initialRevision + 1)).EditorContentJson);
                    var stored = Assert.Single(await db.PdfVersions.Where(x => x.DocumentId == doc).ToArrayAsync()); Assert.Equal(initialRevision + 1, stored.RevisionNumber); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256);
                    Assert.Equal(name == "mixed-images" ? 1 : 0, await db.DocumentImages.CountAsync(x => x.DocumentId == doc));
                }
                Assert.True(readyMs < 15000 && enableMs < 15000 && saveReloadMs < 15000 && pdfMs < 25000 && cachedMs < 5000, name + ": exceeded local acceptance budgets.");
                cases.Add(new { name, pageCount = count, readyMs, enableMs, saveReloadMs, pdfMs, cachedMs, bytes = bytes.Length, sameArchivedPdf = true, passed = true });
            }
            Assert.Equal(3, cases.Count); Assert.Empty(errors); Assert.Empty(external); Assert.Empty(csp);
            var latest = Path.Combine(host.Root, "tmp/editor-performance"); Directory.CreateDirectory(latest); await File.WriteAllTextAsync(Path.Combine(latest, "latest-product.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally { await File.WriteAllTextAsync(Path.Combine(host.Output, "performance-report.json"), JsonSerializer.Serialize(new { runId = host.RunId, cases, errors, external, csp, existingDataUsed = false }, new JsonSerializerOptions { WriteIndented = true })); }
    }
}
