using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Documents.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalSqlPaginationFactAttribute : FactAttribute
{
    public LocalSqlPaginationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_SQL_PAGINATION_TESTS") != "1")
            Skip = "Run Verify-SqlPagination.ps1 explicitly; creates and removes fresh local QA databases only.";
        Timeout = 300_000;
    }
}

public sealed class SqlPaginationAcceptanceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient api = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly List<object> samples = [];

    [LocalSqlPaginationFact]
    public async Task Real_services_preserve_paginated_content_archives_and_review_locks()
    {
        await using var host = new IsolatedSqlProductHost(
            Environment.GetEnvironmentVariable("ETRANSLATE_SQL_PAGINATION_SERVER") ?? throw new InvalidOperationException("Explicit local SQL instance required."),
            Environment.GetEnvironmentVariable("ETRANSLATE_SQL_PAGINATION_ROOT") ?? throw new InvalidOperationException("Explicit solution root required."));
        try
        {
            await host.StartAsync();
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1100 }, ServiceWorkers = ServiceWorkerPolicy.Block });
            List<string> errors = []; List<string> unexpected = []; List<string> csp = [];
            await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('QA_CSP:'+JSON.stringify({url:location.href,source:e.sourceFile,line:e.lineNumber,column:e.columnNumber,directive:e.effectiveDirective,blocked:e.blockedURI,sample:e.sample}))); ");
            await context.RouteAsync("**/*", async route => {
                if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"])
                { unexpected.Add(route.Request.Url); await route.AbortAsync(); } else await route.ContinueAsync();
            });
            var page = await context.NewPageAsync();
            page.PageError += (_, error) => errors.Add(error);
            page.Console += (_, message) => {
                if(message.Text.StartsWith("QA_CSP:"))csp.Add(message.Text);
                if (message.Type == "error" && message.Text.Contains("Content Security Policy")) errors.Add(page.Url+" "+message.Location+" "+message.Text);
            };
            var email = $"qa-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
            await page.GotoAsync(host.Urls["web"] + "/Account/Register");
            var invalidForm=page.APIRequest.CreateFormData();invalidForm.Set("Email",email);invalidForm.Set("Password","x");
            invalidForm.Set("__RequestVerificationToken",await page.Locator("input[name=__RequestVerificationToken]").InputValueAsync());
            var invalid=await page.APIRequest.PostAsync(host.Urls["web"]+"/Account/Register",new(){Form=invalidForm,MaxRedirects=0});
            Assert.Equal(200,invalid.Status);var invalidHtml=await invalid.TextAsync();
            Assert.Contains("validation-summary-errors",invalidHtml);Assert.DoesNotContain("style=",invalidHtml);
            Assert.DoesNotMatch("name=\"Password\"[^>]*value=\"x\"",invalidHtml);
            await page.Locator("#Email").FillAsync(email); await page.Locator("#Password").FillAsync(password);
            await page.Locator(".auth-card button[type=submit]").ClickAsync(); await page.WaitForURLAsync("**/Workspace");
            var token = await Api(host, "identity-access", HttpMethod.Post, "/api/v1/auth/login", new { email, password }, HttpStatusCode.OK);
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
            var tenant = await Api(host, "identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = "Sentetik sayfalama testi", slug = "qa-" + host.RunId, type = "TranslationOffice" }, HttpStatusCode.Created);
            var tenantId = tenant.GetProperty("id").GetGuid();
            await AwaitTrial(host, tenantId);
            var access = await Api(host, "identity-access", HttpMethod.Get, $"/api/v1/tenants/{tenantId}/access", null, HttpStatusCode.OK);
            var actor = access.GetProperty("userId").GetGuid();

            // A doc-root letterhead is created by the real existing Web form, not a hand-written HTML fixture.
            await page.GotoAsync(host.Urls["web"] + $"/Settings/Templates?tenantId={tenantId}");
            await WebPost(page, "/Workspace/CreateTemplate", new() { ["tenantId"] = tenantId.ToString(), ["name"] = "Legacy root letterhead",
                ["header"] = "QA_LEGACY_HEADER", ["footer"] = "QA_LEGACY_FOOTER", ["starterText"] = "", ["watermark"] = "" });
            var templates = await Api(host, "documents", HttpMethod.Get, $"/api/v1/tenants/{tenantId}/document-templates", null, HttpStatusCode.OK);
            var legacyTemplate = templates.EnumerateArray().Single().GetProperty("id").GetGuid();
            await RunCase(host, page, tenantId, actor, "table-no-header", "none", null, false, archive: true);
            await RunCase(host, page, tenantId, actor, "table-headed-legacy", "headed", legacyTemplate, false);

            var root = $"/api/v1/tenants/{tenantId}/document-templates";
            var template = await Api(host, "documents", HttpMethod.Post, root, new {
                name = "Sentetik banner ve son altbilgi", editorContentJson = "{\"type\":\"doc\",\"content\":[]}",
                headerContentJson = Region("header", "QA_HEADER", "right"), footerContentJson = Region("footer", "QA_LAST_FOOTER", "center"),
                pageLayoutJson = (DocumentPageLayout.Default with { FooterPlacement = "LastPage" }).ToJson(),
                watermarkJson = (string?)null, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 1
            }, HttpStatusCode.Created);
            var templateId = template.GetProperty("id").GetGuid();
            using var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jVloAAAAASUVORK5CYII="));
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(image, "file", "synthetic.png");
            using var assetResponse = await api.PostAsync(host.Urls["documents"] + $"{root}/{templateId}/assets", form);
            Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
            var asset = (await assetResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var header = JsonNode.Parse(Region("header", "QA_HEADER", "right"))!;
            header["content"]!.AsArray().Insert(0, JsonSerializer.SerializeToNode(new { type = "image", attrs = new { assetId = asset, align = "right", layout = "banner" } }));
            await Api(host, "documents", HttpMethod.Post, $"{root}/{templateId}/revisions", new {
                expectedCurrentRevision = 1, editorContentJson = "{\"type\":\"doc\",\"content\":[]}", headerContentJson = header.ToJsonString(),
                footerContentJson = Region("footer", "QA_LAST_FOOTER", "center"), pageLayoutJson = (DocumentPageLayout.Default with { FooterPlacement = "LastPage" }).ToJson(),
                watermarkJson = (string?)null, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 1
            }, HttpStatusCode.Created);
            await RunCase(host, page, tenantId, actor, "table-mixed-banner-last", "mixed", templateId, true);
            await File.WriteAllTextAsync(Path.Combine(host.Output,"browser-errors.json"),JsonSerializer.Serialize(new{errors,unexpected,csp},Json));
            await File.WriteAllTextAsync(Path.Combine(host.Output, "browser-report.json"), JsonSerializer.Serialize(new {
                runId = host.RunId, productServices = 6, isolatedDatabases = 6, samples,
                archiveFixture = "Synthetic PDF seeded as v10 metadata in the fresh QA database; not a claim of rendering the old engine.",
                savedThroughWeb = true, reviewLockVerified = true, customerDataUsed = false, externalRequests = unexpected.Count
            }, Json));
            Assert.Empty(errors); Assert.Empty(unexpected);
            await File.WriteAllTextAsync(Path.Combine(host.Root, "tmp/sql-pagination/latest.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally { api.Dispose(); }
    }

    private async Task RunCase(IsolatedSqlProductHost host, IPage page, Guid tenantId, Guid actor, string name, string heading, Guid? template, bool lastFooter, bool archive = false)
    {
        var job = await Api(host, "translation-workflow", HttpMethod.Post, $"/api/v1/tenants/{tenantId}/translation-jobs", new {
            title = "SENTETİK " + name, sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired"
        }, HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        var collection = $"/api/v1/tenants/{tenantId}/translation-jobs/{jobId}/documents";
        var document = await Api(host, "documents", HttpMethod.Post, collection, null, HttpStatusCode.Created);
        var documentId = document.GetProperty("id").GetGuid(); var path = collection + "/" + documentId;
        Guid? bodyImageId = null;
        if (template.HasValue)
        {
            var detail = await Api(host, "documents", HttpMethod.Get, $"/api/v1/tenants/{tenantId}/document-templates/{template}", null, HttpStatusCode.OK);
            await Api(host, "documents", HttpMethod.Post, path + "/template", new { templateId = template, revisionNumber = detail.GetProperty("currentRevision").GetInt32() }, HttpStatusCode.OK);
        }
        var current = await Api(host, "documents", HttpMethod.Get, collection, null, HttpStatusCode.OK);
        var revision = current.GetProperty("currentDraftRevision").GetInt32(); var body = TableBody(heading);
        await Api(host, "documents", HttpMethod.Post, path + "/draft-revisions", new {
            expectedCurrentRevision = revision, editorContentJson = body, plainText = (string?)null,
            contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 1
        }, HttpStatusCode.Created);
        revision++;
        var editor = host.Urls["web"] + $"/Workspace/Editor?tenantId={tenantId}&jobId={jobId}";
        await page.GotoAsync(editor);
        var textbox = page.Locator("#rich-text [role=textbox]"); var viewport = page.Locator("#editor-viewport");
        await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "true");
        Assert.Equal(body, await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync();
        await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
        await page.Locator("#editor-page-index").SelectOptionAsync("2");
        var marker = await page.EvaluateAsync<string>("""
            ()=>{const root=document.querySelector('#rich-text [role=textbox]'),scale=parseFloat(document.querySelector('#editor-paper').style.transform.slice(6)),
                pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left;
                return [...root.querySelectorAll('td p')].find(p=>p.textContent.startsWith('MARK_')&&Math.floor(((p.getBoundingClientRect().left-origin)/scale+1)/pitch)+1===2).textContent;}
            """);
        await textbox.Locator("td p").Filter(new() { HasText = marker }).First.ClickAsync(new(){Position=new(){X=5,Y=5}});
        if(lastFooter){
            // Select via the product's find control, avoiding a synthetic DOM range racing
            // the editor's native-selection observer while paginated layout is settling.
            await page.Locator("#editor-search").EvaluateAsync("e=>e.open=true");await page.Locator("#editor-search-query").FillAsync(marker);
            await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync("1 / 1");await page.Locator("#editor-search-next").ClickAsync();
            await page.WaitForFunctionAsync("text=>getSelection().toString()===text",marker);
            await page.Locator("#editor-font-family").SelectOptionAsync("serif");await page.Locator("#editor-font-size").SelectOptionAsync("18");
            await page.Locator("#editor-text-color").SelectOptionAsync("#b91c1c");await page.Locator("#editor-highlight").SelectOptionAsync("#fef08a");
            await page.Locator("#editor-text-align").SelectOptionAsync("right");await page.Locator("#editor-line-height").SelectOptionAsync("2");await page.Locator("#editor-indent-level").SelectOptionAsync("1");
            await page.Locator("#editor-paragraph-advanced").EvaluateAsync("e=>e.open=true");
            await page.Locator("#editor-space-before").SelectOptionAsync("6");await page.Locator("#editor-space-after").SelectOptionAsync("12");await page.Locator("#editor-first-line-indent").SelectOptionAsync("5");
            Assert.Equal(marker,await page.EvaluateAsync<string>("()=>getSelection().toString()"));
        }
        await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync(" QA_EDIT");
        await Assertions.Expect(page.Locator("#document-json")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("QA_EDIT"));
        await page.Locator("button[data-command=undo]").ClickAsync(); await Assertions.Expect(textbox).Not.ToContainTextAsync("QA_EDIT");
        await page.Locator("button[data-command=redo]").ClickAsync(); await Assertions.Expect(textbox).ToContainTextAsync("QA_EDIT");
        if(lastFooter){
            for(var index=0;index<7;index++)await page.Keyboard.PressAsync("Shift+ArrowLeft");
            Assert.Equal("QA_EDIT",await page.EvaluateAsync<string>("()=>getSelection().toString()"));
            await page.Locator("button[data-command=superscript]").ClickAsync();await page.Locator("button[data-command=strike]").ClickAsync();
        }
        if(archive){
            await page.Locator("#editor-search").EvaluateAsync("e=>e.open=true");await page.Locator("#editor-search-query").FillAsync("QA_EDIT");
            await Assertions.Expect(page.Locator("#editor-search-status")).ToContainTextAsync("1 / 1");
            await page.Locator("#editor-search-replacement").FillAsync("QA_REPLACED");await page.Locator("#editor-search-replace").ClickAsync();
            await Assertions.Expect(textbox).ToContainTextAsync("QA_REPLACED");
            await page.Locator("button[data-command=undo]").ClickAsync();await Assertions.Expect(textbox).ToContainTextAsync("QA_EDIT");
            await page.Locator("button[data-command=redo]").ClickAsync();await Assertions.Expect(textbox).ToContainTextAsync("QA_REPLACED");
            await page.Locator("#editor-table-widths").EvaluateAsync("e=>e.open=true");
            await page.Locator("#editor-column-inputs input").Nth(0).FillAsync("35");
            await page.Locator("#editor-column-inputs input").Nth(1).FillAsync("65");
            await page.Locator("#editor-column-apply").ClickAsync();
            await page.Locator("#editor-cell-style").EvaluateAsync("e=>e.open=true");
            await page.Locator("#editor-cell-scope").SelectOptionAsync("table");
            await page.Locator("#editor-cell-background").SelectOptionAsync("#bfdbfe");
            await page.Locator("#editor-cell-border-color").SelectOptionAsync("#1d4ed8");
            await page.Locator("#editor-cell-border-width").SelectOptionAsync("1.5");
            await page.Locator("#editor-horizontal-merge").EvaluateAsync("e=>e.open=true");
            await page.Locator("button[data-command=mergeNextCell]").ClickAsync();
            await page.Locator("#editor-pages-toggle").ClickAsync();
            await textbox.Locator(":scope > p").Last.ClickAsync(); await page.Keyboard.PressAsync("End");
            await page.Locator("#editor-images").EvaluateAsync("e=>e.open=true");
            await page.Locator("#editor-image-file").SetInputFilesAsync(new FilePayload { Name = "private-body.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
            await page.Locator("#editor-image-upload").ClickAsync(); await Assertions.Expect(textbox.Locator("img")).ToHaveCountAsync(1);
            bodyImageId = Guid.Parse((await textbox.Locator("img").GetAttributeAsync("data-asset-id"))!);
            await page.Locator("#editor-image-width").FillAsync("40"); await page.Locator("#editor-image-align").SelectOptionAsync("right"); await page.Locator("#editor-image-apply").ClickAsync();
            await textbox.Locator(":scope > p").Last.ClickAsync();
            await page.Locator("#editor-image-file").SetInputFilesAsync(new FilePayload { Name = "private-body.jpg", MimeType = "image/jpeg", Buffer = BodyImageFixture.Jpeg });
            await page.Locator("#editor-image-upload").ClickAsync(); await Assertions.Expect(textbox.Locator("img")).ToHaveCountAsync(2);
            await page.Locator("#editor-image-width").FillAsync("25"); await page.Locator("#editor-image-align").SelectOptionAsync("left"); await page.Locator("#editor-image-apply").ClickAsync();
            await VerifyPrivateImageAccess(host, tenantId, jobId, documentId, bodyImageId.Value, path, revision);
        }
        var edited = await page.Locator("#document-json").InputValueAsync();
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await page.WaitForFunctionAsync("revision=>Number(document.querySelector('#draft-form input[name=expectedRevision]').value)===revision", revision + 1);
        revision++;
        var persisted = await Api(host, "documents", HttpMethod.Get, path + $"/draft-revisions/{revision}", null, HttpStatusCode.OK);
        Assert.Equal(edited, persisted.GetProperty("editorContentJson").GetString());
        if(archive){Assert.Contains("QA_REPLACED",edited);Assert.DoesNotContain("editor-search",edited);}
        Assert.Equal(DocumentContentDescriptor.RichFormat, persisted.GetProperty("contentFormat").GetString());
        Assert.Equal(9,persisted.GetProperty("contentSchemaVersion").GetInt32());
        if (archive) {
            using var widthBody = JsonDocument.Parse(edited);
            var bodyImages = widthBody.RootElement.GetProperty("content").EnumerateArray().Where(node => node.GetProperty("type").GetString() == "image").ToArray();
            Assert.Equal(2, bodyImages.Length); Assert.Equal(new[] { 40, 25 }, bodyImages.Select(node => node.GetProperty("attrs").GetProperty("widthPercent").GetInt32()).ToArray());
            Assert.All(bodyImages, node => Assert.Equal("document", node.GetProperty("attrs").GetProperty("assetScope").GetString()));
            Assert.Equal(new[] { 35, 65 }, widthBody.RootElement.GetProperty("content")[0].GetProperty("attrs").GetProperty("columnWidths").EnumerateArray().Select(width => width.GetInt32()).ToArray());
            Assert.Contains(widthBody.RootElement.GetProperty("content")[0].GetProperty("content").EnumerateArray().SelectMany(row => row.GetProperty("content").EnumerateArray()), cell => cell.GetProperty("attrs").GetProperty("colspan").GetInt32() == 2);
            foreach (var row in widthBody.RootElement.GetProperty("content")[0].GetProperty("content").EnumerateArray()) foreach (var cell in row.GetProperty("content").EnumerateArray()) {
                Assert.Equal("#bfdbfe",cell.GetProperty("attrs").GetProperty("backgroundColor").GetString());
                Assert.Equal("#1d4ed8",cell.GetProperty("attrs").GetProperty("borderColor").GetString());
                Assert.Equal(1.5m,cell.GetProperty("attrs").GetProperty("borderWidthPt").GetDecimal());
            }
        }
        if(lastFooter){Assert.Contains("typography",edited);Assert.Contains("serif",edited);Assert.Contains("#b91c1c",edited);
            using var styled=JsonDocument.Parse(edited);var target=styled.RootElement.GetProperty("content")[0].GetProperty("content").EnumerateArray()
                .SelectMany(row=>row.GetProperty("content").EnumerateArray()).SelectMany(cell=>cell.GetProperty("content").EnumerateArray())
                .Single(p=>p.GetProperty("content").EnumerateArray().Any(text=>text.TryGetProperty("text",out var value)&&value.GetString()!.Contains("QA_EDIT")));
            var settings=target.GetProperty("attrs");Assert.Equal("right",settings.GetProperty("textAlign").GetString());Assert.Equal(2,settings.GetProperty("lineHeight").GetDecimal());Assert.Equal(1,settings.GetProperty("indentLevel").GetInt32());
            Assert.Equal(6,settings.GetProperty("spaceBeforePt").GetInt32());Assert.Equal(12,settings.GetProperty("spaceAfterPt").GetInt32());Assert.Equal(5,settings.GetProperty("firstLineIndentMm").GetDecimal());
            var script=target.GetProperty("content").EnumerateArray().Single(text=>text.GetProperty("text").GetString()=="QA_EDIT");
            var marks=script.GetProperty("marks").EnumerateArray().Select(mark=>mark.GetProperty("type").GetString()).ToArray();
            Assert.Contains("superscript",marks);Assert.Contains("strike",marks);Assert.DoesNotContain("subscript",marks);
        }
        var original = await Api(host, "documents", HttpMethod.Get, path + $"/draft-revisions/{revision-1}", null, HttpStatusCode.OK);
        Assert.Equal(body, original.GetProperty("editorContentJson").GetString());
        await page.ReloadAsync(); Assert.Equal(edited, await page.Locator("#document-json").InputValueAsync());
        await Assertions.Expect(page.Locator("button[data-command=undo]")).ToBeDisabledAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");

        DocumentPdfVersion? old = null;
        if (archive)
        {
            old = await SeedArchive(host, documentId, persisted.GetProperty("id").GetGuid(), revision, actor);
            var oldBytes = await Download(host, page, tenantId, jobId, old.Id);
            Assert.Equal(old.Sha256, Convert.ToHexString(SHA256.HashData(oldBytes)).ToLowerInvariant());
            // Bind review to the old identity before producing the current renderer for the same revision.
            await page.ReloadAsync(); await page.Locator("#review-pdf").SelectOptionAsync(old.Id.ToString());
            await page.Locator("#review-submit").ClickAsync(); await page.WaitForFunctionAsync("()=>document.querySelector('#editor-workspace').dataset.editable==='false'");
            await AssertLocked(page);
            await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
            await page.Locator("#editor-page-index").SelectOptionAsync("2"); await AssertLocked(page);
            await Api(host, "documents", HttpMethod.Post, path + "/draft-revisions", new {
                expectedCurrentRevision = revision, editorContentJson = edited, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 9
            }, HttpStatusCode.Conflict);
            using var blockedForm = new MultipartFormDataContent(); var blockedImage = new ByteArrayContent(BodyImageFixture.Png); blockedImage.Headers.ContentType = new MediaTypeHeaderValue("image/png"); blockedForm.Add(blockedImage, "file", "blocked.png");
            using var blockedUpload = await api.PostAsync(host.Urls["documents"] + path + $"/images?expectedRevision={revision}", blockedForm); Assert.Equal(HttpStatusCode.Conflict, blockedUpload.StatusCode);
        }
        var map = await MarkerMap(page);
        var generateResponse = page.WaitForResponseAsync(response => response.Request.Method == "POST" && response.Url.EndsWith("/Workspace/GenerateDraftPdf"));
        await page.Locator("#draft-pdf-form button[type=submit]").ClickAsync();
        var generated = await generateResponse; Assert.Equal(200, generated.Status);
        using var result = JsonDocument.Parse(await generated.TextAsync()); var pdf = result.RootElement.GetProperty("pdf");
        var pdfId = pdf.GetProperty("id").GetGuid(); var sha = pdf.GetProperty("sha256").GetString();
        Assert.Equal(DraftPdfPolicy.RendererVersion, pdf.GetProperty("rendererVersion").GetString());
        var bytes = await Download(host, page, tenantId, jobId, pdfId);
        Assert.Equal(sha, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using (var stream = new MemoryStream(bytes)) using (var printed = PdfReader.Open(stream, PdfDocumentOpenMode.Import))
            Assert.Equal(map.GetProperty("pageCount").GetInt32(), printed.PageCount);
        await File.WriteAllBytesAsync(Path.Combine(host.Output, name + ".pdf"), bytes);
        await File.WriteAllTextAsync(Path.Combine(host.Output, name + "-markers.json"), map.GetRawText());
        var cached = await Api(host, "documents", HttpMethod.Post, path + $"/draft-revisions/{revision}/pdfs", null, HttpStatusCode.OK);
        Assert.Equal(pdfId, cached.GetProperty("id").GetGuid()); Assert.Equal(sha, cached.GetProperty("sha256").GetString());
        if (old is not null)
        {
            Assert.NotEqual(old.Id, pdfId); Assert.NotEqual(old.Sha256, sha);
            var oldAgain = await Download(host, page, tenantId, jobId, old.Id);
            Assert.Equal(old.Sha256, Convert.ToHexString(SHA256.HashData(oldAgain)).ToLowerInvariant());
            var review = await Api(host, "documents", HttpMethod.Get, path + "/reviews", null, HttpStatusCode.OK);
            var attached = review.GetProperty("reviews")[0]; Assert.Equal(old.Id, attached.GetProperty("pdfVersionId").GetGuid());
            Assert.Equal(old.Sha256, attached.GetProperty("pdfSha256").GetString());
            var pdfs = await Api(host, "documents", HttpMethod.Get, path + "/pdfs", null, HttpStatusCode.OK); Assert.Equal(2, pdfs.GetArrayLength());
            using var anonymous = new HttpClient(); using var response = await anonymous.GetAsync(host.Urls["documents"] + path + $"/pdfs/{pdfId}");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await Api(host, "documents", HttpMethod.Get, $"/api/v1/tenants/{Guid.NewGuid()}/translation-jobs/{jobId}/documents/{documentId}/pdfs/{pdfId}", null, HttpStatusCode.Forbidden);
        }
        if (template.HasValue)
        {
            var pages = map.GetProperty("pageCount").GetInt32();
            Assert.Equal(pages, await page.Locator("#editor-page-sheets .pagination-header").CountAsync());
            Assert.Equal(lastFooter ? 1 : pages, await page.Locator("#editor-page-sheets .pagination-footer").CountAsync());
            if (lastFooter) Assert.Equal(pages, await page.Locator("#editor-page-sheets img.template-banner.image-right").CountAsync());
        }
        await page.Locator("#editor-page-index").SelectOptionAsync(map.GetProperty("pageCount").GetInt32().ToString());
        await page.Locator("#editor-viewport").ScrollIntoViewIfNeededAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, name + ".png") });
        await page.Locator("#editor-viewport").EvaluateAsync("e=>e.scrollTop=e.scrollHeight");
        await page.ScreenshotAsync(new() { Path = Path.Combine(host.Output, name + "-footer.png") });
        await page.GotoAsync(editor + $"&revisionNumber={revision-1}");
        await AssertLocked(page); await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(viewport).ToHaveAttributeAsync("data-pagination", "paged");
        await page.Locator("#editor-page-index").SelectOptionAsync("2"); await AssertLocked(page);
        Assert.Equal(body, await page.Locator("#document-json").InputValueAsync());
        samples.Add(new { name, revision, pages = map.GetProperty("pageCount").GetInt32(), markers = 47, lastFooter, hasTemplate = template.HasValue, archiveVerified = old is not null, typographyVerified=lastFooter,paragraphSettingsVerified=lastFooter,paragraphSpacingVerified=lastFooter,inlineScriptVerified=lastFooter,searchReplaceVerified=archive,columnWidthsVerified=archive,cellStyleVerified=archive,horizontalMergeVerified=archive,bodyImagesVerified=archive });
    }

    private async Task VerifyPrivateImageAccess(IsolatedSqlProductHost host, Guid tenantId, Guid jobId, Guid documentId, Guid imageId, string path, int revision)
    {
        var imageUrl = host.Urls["documents"] + path + $"/images/{imageId}";
        using var download = await api.GetAsync(imageUrl); Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.True(download.Headers.CacheControl?.NoStore); Assert.Equal(BodyImageFixture.Png, await download.Content.ReadAsByteArrayAsync());
        using var anonymous = new HttpClient(); using var denied = await anonymous.GetAsync(imageUrl); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await Api(host, "documents", HttpMethod.Get, $"/api/v1/tenants/{Guid.NewGuid()}/translation-jobs/{jobId}/documents/{documentId}/images/{imageId}", null, HttpStatusCode.Forbidden);
        foreach (var valid in new[] { false, true }) {
            using var form = new MultipartFormDataContent(); var bytes = new ByteArrayContent(valid ? BodyImageFixture.Png : "fake image"u8.ToArray()); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(bytes, "file", "test.png");
            using var result = await api.PostAsync(host.Urls["documents"] + path + $"/images?expectedRevision={(valid ? revision - 1 : revision)}", form); Assert.Equal(valid ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, result.StatusCode);
        }
        var otherJob = await Api(host, "translation-workflow", HttpMethod.Post, $"/api/v1/tenants/{tenantId}/translation-jobs", new { title = "Sentetik görsel erişim sınırı", sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, HttpStatusCode.Created);
        var otherCollection = $"/api/v1/tenants/{tenantId}/translation-jobs/{otherJob.GetProperty("id").GetGuid()}/documents";
        var otherDocument = await Api(host, "documents", HttpMethod.Post, otherCollection, null, HttpStatusCode.Created); var other = otherCollection + "/" + otherDocument.GetProperty("id").GetGuid();
        using var outside = await api.GetAsync(host.Urls["documents"] + other + $"/images/{imageId}"); Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        var forged = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "image", attrs = new { assetId = imageId, assetScope = "document", widthPercent = 50, align = "center" } } } });
        await Api(host, "documents", HttpMethod.Post, other + "/draft-revisions", new { expectedCurrentRevision = 0, editorContentJson = forged, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 9 }, HttpStatusCode.BadRequest);
        // This second document owns its own JPEG; changing its letterhead must retain it.
        using var ownForm = new MultipartFormDataContent(); var jpeg = new ByteArrayContent(BodyImageFixture.Jpeg); jpeg.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg"); ownForm.Add(jpeg, "file", "own-private.jpg");
        using var ownUpload = await api.PostAsync(host.Urls["documents"] + other + "/images?expectedRevision=0", ownForm); Assert.Equal(HttpStatusCode.Created, ownUpload.StatusCode);
        var ownId = (await ownUpload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var owned = forged.Replace(imageId.ToString(), ownId.ToString());
        await Api(host, "documents", HttpMethod.Post, other + "/draft-revisions", new { expectedCurrentRevision = 0, editorContentJson = owned, contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 9 }, HttpStatusCode.Created);
        var templates = await Api(host, "documents", HttpMethod.Get, $"/api/v1/tenants/{tenantId}/document-templates", null, HttpStatusCode.OK); var target = templates[0];
        await Api(host, "documents", HttpMethod.Post, other + "/template-change", new { expectedCurrentRevision = 1, templateId = target.GetProperty("id").GetGuid(), revisionNumber = target.GetProperty("currentRevision").GetInt32() }, HttpStatusCode.OK);
        foreach (var version in new[] { 1, 2 }) {
            var saved = await Api(host, "documents", HttpMethod.Get, other + $"/draft-revisions/{version}", null, HttpStatusCode.OK); Assert.Equal(owned, saved.GetProperty("editorContentJson").GetString());
            await Api(host, "documents", HttpMethod.Post, other + $"/draft-revisions/{version}/pdfs", null, HttpStatusCode.Created);
        }
    }

    private async Task<JsonElement> Api(IsolatedSqlProductHost host, string service, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, host.Urls[service] + path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await api.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{service} {method} {path}: expected {(int)expected}, received {(int)response.StatusCode}. {text[..Math.Min(text.Length,500)]}");
        if(string.IsNullOrWhiteSpace(text))return JsonSerializer.SerializeToElement(new { });
        using var parsed=JsonDocument.Parse(text);return parsed.RootElement.Clone();
    }

    private async Task AwaitTrial(IsolatedSqlProductHost host, Guid tenant)
    {
        for (var i = 0; i < 100; i++)
        {
            var state = await Api(host, "billing", HttpMethod.Get, $"/api/v1/tenants/{tenant}/subscription/write-access", null, HttpStatusCode.OK);
            if (state.GetProperty("canWrite").GetBoolean()) return;
            await Task.Delay(200);
        }
        throw new TimeoutException("Isolated SQL transport did not provision the trial.");
    }

    private static async Task WebPost(IPage page, string path, Dictionary<string,string> values)
    {
        values["__RequestVerificationToken"] = await page.Locator("input[name=__RequestVerificationToken]").First.InputValueAsync();
        var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        var form=page.APIRequest.CreateFormData();foreach(var pair in values)form.Set(pair.Key,pair.Value);
        var response = await page.APIRequest.PostAsync(origin + path, new() { Form = form, MaxRedirects = 0 });
        Assert.Equal(302, response.Status); await page.GotoAsync(origin + response.Headers["location"]);
    }

    private static async Task<byte[]> Download(IsolatedSqlProductHost host, IPage page, Guid tenant, Guid job, Guid pdf)
    {
        var response = await page.APIRequest.GetAsync(host.Urls["web"] + $"/Workspace/DraftPdf?tenantId={tenant}&jobId={job}&pdfId={pdf}");
        Assert.Equal(200, response.Status); Assert.Contains("application/pdf", response.Headers["content-type"]);
        Assert.Contains("no-store", response.Headers["cache-control"]); var bytes = await response.BodyAsync();
        Assert.Equal(response.Headers["x-content-sha256"], Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()); return bytes;
    }

    private static async Task AssertLocked(IPage page)
    {
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false");
        await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
        Assert.True(await page.Locator(".editor-toolbar button").EvaluateAllAsync<bool>("buttons=>buttons.every(button=>button.disabled)"));
    }

    private static async Task<JsonElement> MarkerMap(IPage page)
    {
        // Return JSON text explicitly: do not let the browser client's object serializer
        // insert its own reference metadata into the evidence map.
        var json=await page.EvaluateAsync<string>("""
        ()=>{const root=document.querySelector('#rich-text [role=textbox]'),world=document.querySelector('#editor-paper'),scale=parseFloat(world.style.transform.slice(6)),
            pitch=parseFloat(root.style.width)+parseFloat(root.style.columnGap),origin=root.getBoundingClientRect().left,markers={},walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
            for(let node=walker.nextNode();node;node=walker.nextNode())for(const match of node.data.matchAll(/MARK_\d{3}|END_MARKER/g)){
                const range=document.createRange();range.setStart(node,match.index);range.setEnd(node,match.index+match[0].length);const rect=[...range.getClientRects()].find(r=>r.width>0);
                markers[match[0]]=Math.floor(((rect.left-origin)/scale+1)/pitch)+1;
            }return JSON.stringify({pageCount:Number(document.querySelector('#editor-viewport').dataset.pageCount),markers});}
        """);
        using var parsed=JsonDocument.Parse(json);return parsed.RootElement.Clone();
    }

    private static string Region(string type, string text, string align) => JsonSerializer.Serialize(new { type, content = new[] {
        new { type = "paragraph", attrs = new { textAlign = align }, content = new[] { new { type = "text", text } } }
    } });

    private static string TableBody(string heading)
    {
        object P(string text) => new { type = "paragraph", attrs = new { dir = "ltr" }, content = new[] { new { type = "text", text } } };
        var rows = Enumerable.Range(0,47).Select(i => new { type = "tableRow", content = new[] {
            new { type = i==0 && heading is "headed" or "mixed" ? "tableHeader" : "tableCell", content = new[] { P(i==0 ? "Sıra" : $"MARK_{i:000}") } },
            new { type = i==0 && heading=="headed" ? "tableHeader" : "tableCell", content = new[] { P(i==0 ? "Sentetik açıklama" : string.Concat(Enumerable.Repeat("İstanbul - جدول ساختگی / Türkçe. ",3))) } }
        } });
        return JsonSerializer.Serialize(new { type = "doc", content = new object[] { new { type = "table", content = rows }, P("END_MARKER") } });
    }

    private static async Task<DocumentPdfVersion> SeedArchive(IsolatedSqlProductHost host, Guid document, Guid draft, int revision, Guid actor)
    {
        // A pre-existing archived-file fixture, not a reimplementation or claim of running renderer v10.
        using var pdf = new PdfDocument(); pdf.Info.Title = "SYNTHETIC ARCHIVED PDF FIXTURE - UNSIGNED"; pdf.AddPage();
        using var stream = new MemoryStream(); pdf.Save(stream, false); stream.Position = 0;
        var store = new LocalDocumentBlobStore(new DocumentStorageOptions { RootPath = host.Storage });
        var key = $"qa-archives/{Guid.NewGuid():N}.pdf"; var blob = await store.SaveAsync(key, stream, "application/pdf", DraftPdfPolicy.MaximumBytes, default);
        var item = new DocumentPdfVersion(document,draft,revision,null,"draft-html-v10-footer-policy-pw1.63.0-noto-pdfsharp6.2.4",key,blob.SizeBytes,blob.Sha256,actor,DateTimeOffset.UtcNow);
        await using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
        db.PdfVersions.Add(item); await db.SaveChangesAsync(); return item;
    }
}
