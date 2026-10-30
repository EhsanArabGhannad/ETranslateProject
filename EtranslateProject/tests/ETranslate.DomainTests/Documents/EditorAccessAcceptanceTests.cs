using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Billing.Api.Persistence;
using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorAccessFactAttribute : FactAttribute
{
    public LocalEditorAccessFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ACCESS_TESTS") != "1")
            Skip = "Run Verify-EditorAccess.ps1 explicitly; uses six new local QA databases only.";
        Timeout = 300_000;
    }
}

public sealed class EditorAccessAcceptanceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly List<object> evidence = [];
    private readonly List<string> errors = [], external = [], csp = [];
    private sealed record Account(string Email, string Password, string Token);
    private sealed record Document(Guid Tenant, Guid Job, Guid Id, Guid Image, Guid Source, Guid Pdf, byte[] PdfBytes)
    {
        public string Collection => $"/api/v1/tenants/{Tenant}/translation-jobs/{Job}/documents";
        public string Path => $"{Collection}/{Id}";
        public string Editor => $"/Workspace/Editor?tenantId={Tenant}&jobId={Job}";
    }

    [LocalEditorAccessFact]
    public async Task Editor_roles_live_membership_subscription_and_tenant_boundaries_hold_in_real_services()
    {
        await using var host = new IsolatedSqlProductHost(
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ACCESS_SERVER") ?? throw new InvalidOperationException("Explicit local SQL required."),
            Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ACCESS_ROOT") ?? throw new InvalidOperationException("Explicit solution root required."), "editor-access");
        try
        {
            await host.StartAsync();
            var owner = await Register(host, "owner"); var admin = await Register(host, "admin");
            var translator = await Register(host, "translator"); var reviewer = await Register(host, "reader"); var guest = await Register(host, "other");
            var office = await Tenant(host, owner, "TranslationOffice");
            await Join(host, owner, admin, office, "Administrator"); await Join(host, owner, translator, office, "Translator");
            await Join(host, owner, reviewer, office, "Reviewer");
            var independent = await Tenant(host, guest, "IndependentTranslator");
            var document = await Seed(host, owner, office); var personal = await Seed(host, guest, independent);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            var sessions = new Dictionary<string, (IBrowserContext Context, IPage Page)>();
            foreach (var (role, user, writable) in new[] { ("Owner", owner, true), ("Administrator", admin, true), ("Translator", translator, true), ("Reviewer", reviewer, false) })
            {
                var session = await Login(host, browser, user); sessions[role] = session;
                await Open(host, session.Page, document, writable);
                await ReadAssets(host, session.Page, document, 200);
                if (writable)
                {
                    await TypeProbe(session.Page, " ACCESS_" + role.ToUpperInvariant());
                    var submitted = await session.Page.Locator("#document-json").InputValueAsync();
                    await session.Page.Locator("#draft-form button[type=submit]").ClickAsync();
                    await Assertions.Expect(session.Page.Locator("#save-state")).ToHaveTextAsync("Otomatik kayıt yok");
                    Assert.Equal(submitted, await session.Page.Locator("#document-json").InputValueAsync());
                    var access = await Api(host, "identity-access", user, HttpMethod.Get, $"/api/v1/tenants/{office}/access");
                    var saved = await Current(host, owner, document);
                    Assert.Equal(access.GetProperty("userId").GetGuid(), saved.GetProperty("createdByUserId").GetGuid());
                    Assert.Equal(10, saved.GetProperty("contentSchemaVersion").GetInt32());
                }
                else
                {
                    await Assertions.Expect(session.Page.Locator("#draft-pdf-form button[type=submit]")).ToBeDisabledAsync();
                    await DeniedWrites(host, user, session.Page, document, 403);
                    var emptyJob = await Api(host, "translation-workflow", owner, HttpMethod.Post, $"/api/v1/tenants/{office}/translation-jobs", new { title = "Synthetic unstarted document", sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201);
                    var emptyId = emptyJob.GetProperty("id").GetGuid(); var emptyPath = $"/api/v1/tenants/{office}/translation-jobs/{emptyId}/documents";
                    await session.Page.GotoAsync(host.Urls["web"] + $"/Workspace/Editor?tenantId={office}&jobId={emptyId}");
                    await Assertions.Expect(session.Page.Locator("form[action='/Workspace/StartDocument'] button")).ToBeDisabledAsync();
                    await Api(host, "documents", reviewer, HttpMethod.Post, emptyPath, expected: 403);
                    await Api(host, "documents", owner, HttpMethod.Post, emptyPath, expected: 201);
                    await session.Page.ReloadAsync(); await Assertions.Expect(session.Page.Locator("#template-form button")).ToBeDisabledAsync();
                    await Open(host, session.Page, document, false);
                    await session.Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(host.Output, "reviewer-readonly.png"), FullPage = false });
                }
                await session.Page.GotoAsync(host.Urls["web"] + document.Editor + "&revisionNumber=1"); await Locked(session.Page);
                evidence.Add(new { scenario = role, editAllowed = writable, assetsReadable = true, historyLocked = true, passed = true });
            }

            var outsider = await Login(host, browser, guest);
            await Open(host, outsider.Page, personal, true); // Independent translator owns a separate, writable workspace.
            await TypeProbe(outsider.Page, " ACCESS_INDEPENDENT_SAVED");
            await outsider.Page.Locator("#draft-form button[type=submit]").ClickAsync();
            await Assertions.Expect(outsider.Page.Locator("#save-state")).ToHaveTextAsync("Otomatik kayıt yok");
            Assert.Contains("ACCESS_INDEPENDENT_SAVED", (await Current(host, guest, personal)).GetProperty("editorContentJson").GetString());
            await ReadAssets(host, outsider.Page, personal, 200);
            await Api(host, "documents", guest, HttpMethod.Get, document.Collection, expected: 403);
            await DeniedWrites(host, guest, outsider.Page, document, 403);
            await ReadAssets(host, outsider.Page, document, 403);
            var forbidden = await outsider.Page.GotoAsync(host.Urls["web"] + document.Editor); Assert.Equal(403, forbidden!.Status);
            Assert.DoesNotContain("ACCESS_START", await outsider.Page.ContentAsync());
            var wrongScope = document.Collection.Replace(office.ToString(), independent.ToString());
            await Api(host, "documents", guest, HttpMethod.Get, wrongScope, expected: 404);
            await Api(host, "documents", guest, HttpMethod.Post, wrongScope + "/" + document.Id + "/draft-revisions", Draft(5, "MUST_NOT_SAVE"), 404);
            evidence.Add(new { scenario = "independent-and-cross-tenant", independentWritable = true, foreignReadsAndWritesDenied = true, passed = true });

            var latestDraft = await Current(host, owner, document); var number = latestDraft.GetProperty("revisionNumber").GetInt32();
            var reviewPdf = await Api(host, "documents", owner, HttpMethod.Post, document.Path + $"/draft-revisions/{number}/pdfs", expected: 201);
            var review = await Api(host, "documents", translator, HttpMethod.Post, document.Path + "/reviews", new { expectedRevision = number, expectedRound = 0, pdfVersionId = reviewPdf.GetProperty("id").GetGuid() }, 201);
            var reviewPath = document.Path + $"/reviews/{review.GetProperty("id").GetGuid()}/decision";
            var reviewSnapshot = await Snapshot(host, document);
            foreach (var user in new[] { translator, reviewer }) await Api(host, "documents", user, HttpMethod.Post, reviewPath, new { action = "Approve" }, 403);
            await Api(host, "documents", owner, HttpMethod.Post, document.Path + "/draft-revisions", Draft(number, "LOCKED_REVIEW"), 409);
            Assert.Equal(reviewSnapshot, await Snapshot(host, document));
            await Open(host, sessions["Owner"].Page, document, false);
            await Api(host, "documents", admin, HttpMethod.Post, reviewPath, new { action = "Approve" });
            await Open(host, sessions["Owner"].Page, document, false);
            await Api(host, "documents", owner, HttpMethod.Post, reviewPath, new { action = "Reopen", note = "Synthetic correction" });
            evidence.Add(new { scenario = "review-permissions-and-locks", translatorSubmits = true, onlyManagersApprove = true, lockedDraftUnchanged = true, passed = true });

            var translatorId = (await Api(host, "identity-access", translator, HttpMethod.Get, $"/api/v1/tenants/{office}/access")).GetProperty("userId").GetGuid();
            await Open(host, sessions["Translator"].Page, document, true);
            await TypeProbe(sessions["Translator"].Page, " ACCESS_UNSAVED_ROLE_CHANGE");
            var beforeRoleChange = await Snapshot(host, document);
            await TeamChange(host, owner, office, translatorId, "role", newRole: "Reviewer");
            await LiveDenial(sessions["Translator"].Page, 403); Assert.Equal(beforeRoleChange, await Snapshot(host, document));
            await ReadAssets(host, sessions["Translator"].Page, document, 200);
            await Open(host, sessions["Translator"].Page, document, false);
            await DeniedWrites(host, translator, sessions["Translator"].Page, document, 403);
            await TeamChange(host, owner, office, translatorId, "status");
            await ReadAssets(host, sessions["Translator"].Page, document, 403);
            await Api(host, "documents", translator, HttpMethod.Get, document.Collection, expected: 403);
            forbidden = await sessions["Translator"].Page.GotoAsync(host.Urls["web"] + document.Editor); Assert.Equal(403, forbidden!.Status);
            evidence.Add(new { scenario = "live-role-change-and-deactivation", staleCookieCannotBypass = true, unsavedTextRetainedAfterRoleChange = true, passed = true });

            var ownerPage = sessions["Owner"].Page;
            await Open(host, ownerPage, document, true); await TypeProbe(ownerPage, " ACCESS_UNSAVED_EXPIRED");
            var beforeExpiry = await Snapshot(host, document);
            await SubscriptionFixture(host, office, "Expired");
            await LiveDenial(ownerPage, 402); Assert.Equal(beforeExpiry, await Snapshot(host, document));
            foreach (var (role, user) in new[] { ("Owner", owner), ("Administrator", admin) })
            {
                var page = sessions[role].Page; await Open(host, page, document, false);
                await Assertions.Expect(page.Locator("[data-subscription-readonly=true]")).ToBeVisibleAsync();
                await DeniedWrites(host, user, page, document, 402); await ReadAssets(host, page, document, 200);
            }
            // Another tenant's still-valid trial cannot unlock the expired office.
            await Open(host, outsider.Page, personal, true);
            foreach (var state in new[] { "PastDue", "Canceled", "Pending" })
            {
                await SubscriptionFixture(host, office, state); await Open(host, ownerPage, document, false);
                await DeniedWrites(host, owner, ownerPage, document, state == "Pending" ? 503 : 402);
                await ReadAssets(host, ownerPage, document, 200);
                evidence.Add(new { scenario = "subscription-" + state, readsPreserved = true, mutationsDenied = true, passed = true });
            }
            await SubscriptionFixture(host, office, "Active"); await Open(host, ownerPage, document, true);
            await TypeProbe(ownerPage, " ACCESS_PAID_RESTORED");
            await ownerPage.Locator("#draft-form button[type=submit]").ClickAsync();
            await Assertions.Expect(ownerPage.Locator("#save-state")).ToHaveTextAsync("Otomatik kayıt yok");
            Assert.Contains("ACCESS_PAID_RESTORED", (await Current(host, owner, document)).GetProperty("editorContentJson").GetString());
            await ReadAssets(host, ownerPage, document, 200);
            evidence.Add(new { scenario = "expired-then-active", liveExpiryLocked = true, paidFixtureRestoresWrite = true, archivedPdfBytesUnchanged = true, passed = true });

            var adminPage = sessions["Administrator"].Page;
            await Open(host, adminPage, document, true); await TypeProbe(adminPage, " ACCESS_UNSAVED_DEACTIVATED");
            var adminId = (await Api(host, "identity-access", admin, HttpMethod.Get, $"/api/v1/tenants/{office}/access")).GetProperty("userId").GetGuid();
            var beforeDeactivation = await Snapshot(host, document); await TeamChange(host, owner, office, adminId, "status");
            await LiveDenial(adminPage, 403); await DeniedWrites(host, admin, adminPage, document, 403);
            await ReadAssets(host, adminPage, document, 403); Assert.Equal(beforeDeactivation, await Snapshot(host, document));
            evidence.Add(new { scenario = "live-writer-deactivation", existingSessionDenied = true, unsavedTextRetained = true, storedContentUnchanged = true, passed = true });

            await Api(host, "documents", null, HttpMethod.Get, document.Collection, expected: 401);
            await Api(host, "documents", null, HttpMethod.Post, document.Path + "/draft-revisions", Draft(0, "NO_AUTH"), 401);
            await using var anonymous = await browser.NewContextAsync();
            var anon = await anonymous.APIRequest.GetAsync(host.Urls["web"] + document.Editor, new() { MaxRedirects = 0 });
            Assert.Equal(302, anon.Status);
            var loginLocation = new Uri(new Uri(host.Urls["web"]), anon.Headers["location"]);
            Assert.Equal(host.Urls["web"], loginLocation.GetLeftPart(UriPartial.Authority)); Assert.Equal("/Account/Login", loginLocation.AbsolutePath);
            evidence.Add(new { scenario = "anonymous", apiDenied = true, browserLoginRequired = true, passed = true });
            await ownerPage.GotoAsync(host.Urls["web"] + document.Editor); await ownerPage.Locator("#editor-pages-toggle").ClickAsync();
            await ownerPage.ScreenshotAsync(new() { Path = System.IO.Path.Combine(host.Output, "active-editor.png"), FullPage = false });
            foreach (var session in sessions.Values) await session.Context.DisposeAsync(); await outsider.Context.DisposeAsync();
            Assert.Empty(errors); Assert.Empty(external); Assert.Empty(csp);
            var latest = System.IO.Path.Combine(host.Root, "tmp/editor-access"); Directory.CreateDirectory(latest);
            await File.WriteAllTextAsync(System.IO.Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally
        {
            http.Dispose();
            await File.WriteAllTextAsync(System.IO.Path.Combine(host.Output, "access-report.json"), JsonSerializer.Serialize(new
            { runId = host.RunId, scenarios = evidence, errors, external, csp, customerDataUsed = false, subscriptionStatesAreSynthetic = true }, Json));
        }
    }

    private async Task<Account> Register(IsolatedSqlProductHost host, string label)
    {
        var email = $"access-{label}-{host.RunId}@etranslate.local"; var password = $"Qa-{Guid.NewGuid():N}!";
        await Api(host, "identity-access", null, HttpMethod.Post, "/api/v1/auth/register", new { email, password });
        var login = await Api(host, "identity-access", null, HttpMethod.Post, "/api/v1/auth/login", new { email, password });
        return new(email, password, login.GetProperty("accessToken").GetString()!);
    }
    private async Task<Guid> Tenant(IsolatedSqlProductHost host, Account owner, string type)
    {
        var value = await Api(host, "identity-access", owner, HttpMethod.Post, "/api/v1/tenants", new { name = "Synthetic access QA " + type, slug = "access-" + type.ToLowerInvariant() + "-" + host.RunId, type }, 201);
        var id = value.GetProperty("id").GetGuid();
        for (var n = 0; n < 100; n++) { if ((await Api(host, "billing", owner, HttpMethod.Get, $"/api/v1/tenants/{id}/subscription/write-access")).GetProperty("canWrite").GetBoolean()) return id; await Task.Delay(200); }
        throw new TimeoutException("Trial provisioning did not complete.");
    }
    private async Task Join(IsolatedSqlProductHost host, Account owner, Account user, Guid tenant, string role)
    {
        var teamPath = $"/api/v1/tenants/{tenant}/team"; var team = await Api(host, "identity-access", owner, HttpMethod.Get, teamPath);
        var invite = await Api(host, "identity-access", owner, HttpMethod.Post, teamPath + "/invitations", new { email = user.Email, role, expectedTeamVersion = team.GetProperty("teamVersion").GetInt64() }, 201);
        await Api(host, "identity-access", user, HttpMethod.Post, $"/api/v1/team-invitations/{invite.GetProperty("id").GetGuid()}/accept", new { token = invite.GetProperty("token").GetString() });
    }
    private async Task TeamChange(IsolatedSqlProductHost host, Account owner, Guid tenant, Guid user, string kind, string? newRole = null)
    {
        var path = $"/api/v1/tenants/{tenant}/team"; var team = await Api(host, "identity-access", owner, HttpMethod.Get, path);
        await Api(host, "identity-access", owner, HttpMethod.Put, path + $"/members/{user}/{kind}", new { role = newRole, isActive = false, expectedTeamVersion = team.GetProperty("teamVersion").GetInt64() });
    }
    private static object Draft(int revision, string text) => new { expectedCurrentRevision = revision, editorContentJson = Body(text), contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 };
    private static string Body(string text, Guid? image = null) => JsonSerializer.Serialize(new { type = "doc", content = new object[] {
        new { type = "paragraph", attrs = new { dir = "rtl" }, content = new[] { new { type = "text", text = "ACCESS_START ترجمهٔ آزمایشی İstanbul " + text } } },
        image.HasValue ? (object)new { type = "image", attrs = new { assetId = image.Value, assetScope = "document", widthPercent = 25, align = "center" } } : new { type = "paragraph" },
        new { type = "paragraph", attrs = new { pageBreakBefore = true }, content = new[] { new { type = "text", text = "ACCESS_PAGE_TWO " + text } } }
    } });
    private async Task<Document> Seed(IsolatedSqlProductHost host, Account owner, Guid tenant)
    {
        var root = $"/api/v1/tenants/{tenant}/translation-jobs";
        var job = (await Api(host, "translation-workflow", owner, HttpMethod.Post, root, new { title = "Synthetic editor access", sourceLanguageCode = "fa", targetLanguageCode = "tr", notaryRequirement = "NotRequired" }, 201)).GetProperty("id").GetGuid();
        var collection = root + $"/{job}/documents"; var id = (await Api(host, "documents", owner, HttpMethod.Post, collection, expected: 201)).GetProperty("id").GetGuid();
        var path = collection + "/" + id;
        var image = (await Upload(host, owner, path + "/images?expectedRevision=0", 201)).GetProperty("id").GetGuid();
        var source = (await Upload(host, owner, path + "/source-files", 201)).GetProperty("id").GetGuid();
        foreach (var revision in new[] { 0, 1 }) await Api(host, "documents", owner, HttpMethod.Post, path + "/draft-revisions", new { expectedCurrentRevision = revision, editorContentJson = Body("SAVED_" + revision, image), contentFormat = DocumentContentDescriptor.RichFormat, contentSchemaVersion = 10 }, 201);
        var pdf = await Api(host, "documents", owner, HttpMethod.Post, path + "/draft-revisions/2/pdfs", expected: 201);
        var pdfId = pdf.GetProperty("id").GetGuid();
        using var request = Request(host, "documents", owner, HttpMethod.Get, path + "/pdfs/" + pdfId);
        using var response = await http.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(); Assert.Equal(pdf.GetProperty("sha256").GetString(), Hash(bytes).ToLowerInvariant());
        await File.WriteAllBytesAsync(System.IO.Path.Combine(host.Output, owner.Email.Contains("owner") ? "office.pdf" : "independent.pdf"), bytes);
        return new(tenant, job, id, image, source, pdfId, bytes);
    }
    private async Task<(IBrowserContext Context, IPage Page)> Login(IsolatedSqlProductHost host, IBrowser browser, Account account)
    {
        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1100 }, ServiceWorkers = ServiceWorkerPolicy.Block });
        await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('ACCESS_CSP:'+e.effectiveDirective));");
        await context.RouteAsync("**/*", async route => { if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != host.Urls["web"]) { external.Add(route.Request.Url); await route.AbortAsync(); } else await route.ContinueAsync(); });
        var page = await context.NewPageAsync(); page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, msg) => { if (msg.Text.StartsWith("ACCESS_CSP:")) csp.Add(msg.Text); };
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.GotoAsync(host.Urls["web"] + "/Account/Login"); await page.Locator("#Email").FillAsync(account.Email); await page.Locator("#Password").FillAsync(account.Password);
        await page.Locator(".auth-card button[type=submit]").ClickAsync(); await page.WaitForURLAsync("**/Workspace"); return (context, page);
    }
    private static async Task Open(IsolatedSqlProductHost host, IPage page, Document document, bool editable)
    {
        var response = await page.GotoAsync(host.Urls["web"] + document.Editor); Assert.Equal(200, response!.Status);
        await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", editable ? "true" : "false");
        await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", editable ? "true" : "false");
        if (!editable) await Locked(page);
        var original = await page.Locator("#document-json").InputValueAsync();
        await page.Locator("#editor-pages-toggle").ClickAsync(); await Assertions.Expect(page.Locator("#editor-viewport")).ToHaveAttributeAsync("data-pagination", "paged");
        await page.Locator("#editor-page-index").SelectOptionAsync("2"); Assert.Equal(original, await page.Locator("#document-json").InputValueAsync());
        await page.Locator("#editor-pages-toggle").ClickAsync();
    }
    private static async Task Locked(IPage page)
    {
        var textbox = page.Locator("#rich-text [role=textbox]"); await Assertions.Expect(textbox).ToHaveAttributeAsync("contenteditable", "false");
        await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
        Assert.True(await page.Locator(".editor-toolbar button").EvaluateAllAsync<bool>("buttons=>buttons.every(b=>b.disabled)"));
        var original = await page.Locator("#document-json").InputValueAsync();
        await textbox.EvaluateAsync("el=>{const data=new DataTransfer();data.setData('text/plain','MUST_NOT_PASTE');el.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));}");
        Assert.Equal(original, await page.Locator("#document-json").InputValueAsync());
    }
    private static async Task TypeProbe(IPage page, string text)
    { await page.Locator("#rich-text [role=textbox]").PressAsync("Control+End"); await page.Keyboard.InsertTextAsync(text); Assert.Contains(text.Trim(), await page.Locator("#document-json").InputValueAsync()); }
    private static async Task LiveDenial(IPage page, int status)
    {
        var original = await page.Locator("#document-json").InputValueAsync();
        var denied = page.WaitForResponseAsync(r => new Uri(r.Url).AbsolutePath == "/Workspace/SaveDraft" && r.Request.Method == "POST");
        await page.Locator("#draft-form button[type=submit]").ClickAsync(); Assert.Equal(status, (await denied).Status);
        await Locked(page); Assert.Equal(original, await page.Locator("#document-json").InputValueAsync());
        await Assertions.Expect(page.Locator("#save-state")).ToContainTextAsync("Metniniz");
    }
    private async Task DeniedWrites(IsolatedSqlProductHost host, Account user, IPage page, Document document, int status)
    {
        var snapshot = await Snapshot(host, document); var current = await Current(host, null, document);
        var revision = current.GetProperty("revisionNumber").GetInt32();
        var error = await Api(host, "documents", user, HttpMethod.Post, document.Path + "/draft-revisions", Draft(revision, "MUST_NOT_SAVE"), status);
        if (status is 402 or 503) Assert.Equal(status == 402 ? "subscription_required" : "subscription_pending", error.GetProperty("code").GetString());
        await Upload(host, user, document.Path + $"/images?expectedRevision={revision}", status);
        await Api(host, "documents", user, HttpMethod.Post, document.Path + $"/draft-revisions/{revision}/pdfs", expected: status);
        var form = page.APIRequest.CreateFormData(); form.Set("tenantId", document.Tenant.ToString()); form.Set("jobId", document.Job.ToString());
        form.Set("expectedRevision", revision.ToString()); form.Set("mode", "rich"); form.Set("editorContentJson", Body("MUST_NOT_SAVE"));
        form.Set("__RequestVerificationToken", await page.Locator("input[name=__RequestVerificationToken]").First.InputValueAsync());
        var web = await page.APIRequest.PostAsync(host.Urls["web"] + "/Workspace/SaveDraft", new() { Form = form, MaxRedirects = 0 }); Assert.Equal(status, web.Status);
        Assert.Equal(snapshot, await Snapshot(host, document));
    }
    private static async Task<string> Snapshot(IsolatedSqlProductHost host, Document document)
    {
        await using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
        var item = await db.TranslationDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        var revisions = await db.DraftRevisions.AsNoTracking().Where(r => r.DocumentId == document.Id).OrderBy(r => r.RevisionNumber).Select(r => r.EditorContentJson).ToArrayAsync();
        var blobs = Directory.GetFiles(host.Storage, "*", SearchOption.AllDirectories).Order().Select(p => new { path = System.IO.Path.GetRelativePath(host.Storage, p), hash = Hash(File.ReadAllBytes(p)) }).ToArray();
        return JsonSerializer.Serialize(new { item.CurrentDraftRevision, item.ReviewStatus, revisions, images = await db.DocumentImages.CountAsync(), sources = await db.SourceFiles.CountAsync(), pdfs = await db.PdfVersions.CountAsync(), reviews = await db.Reviews.CountAsync(), blobs });
    }
    private async Task<JsonElement> Current(IsolatedSqlProductHost host, Account? account, Document document)
    {
        if (account is not null)
        {
            var item = await Api(host, "documents", account, HttpMethod.Get, document.Collection);
            return await Api(host, "documents", account, HttpMethod.Get, document.Path + "/draft-revisions/" + item.GetProperty("currentDraftRevision").GetInt32());
        }
        await using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
        var draft = await db.DraftRevisions.AsNoTracking().Where(r => r.DocumentId == document.Id).OrderByDescending(r => r.RevisionNumber).FirstAsync();
        return JsonSerializer.SerializeToElement(new { revisionNumber = draft.RevisionNumber });
    }
    private static async Task SubscriptionFixture(IsolatedSqlProductHost host, Guid tenant, string state)
    {
        await using var db = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(host.Connection("billingdb")).Options);
        var now = DateTimeOffset.UtcNow;
        var item = await db.Subscriptions.SingleOrDefaultAsync(s => s.TenantId == tenant);
        if (state == "Active")
        {
            if (item is null) { item = Subscription.StartTrial(tenant, now.AddDays(-31), "TranslationOffice"); db.Subscriptions.Add(item); }
            item.AllocatePaidPeriod(new BillingPlan("office-monthly", "Synthetic paid access fixture", "TranslationOffice"), now);
            await db.SaveChangesAsync(); return;
        }
        Assert.NotNull(item);
        if (state == "Pending") { db.Subscriptions.Remove(item); await db.SaveChangesAsync(); return; }
        var affected = await db.Subscriptions.Where(s => s.TenantId == tenant).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.Status, state == "Expired" ? SubscriptionStatus.Trialing : state == "PastDue" ? SubscriptionStatus.PastDue : SubscriptionStatus.Canceled)
            .SetProperty(s => s.TrialStartedAtUtc, now.AddDays(-31)).SetProperty(s => s.TrialEndsAtUtc, now.AddDays(-1)).SetProperty(s => s.Version, s => s.Version + 1));
        Assert.Equal(1, affected);
    }
    private static async Task ReadAssets(IsolatedSqlProductHost host, IPage page, Document d, int status)
    {
        foreach (var (path, bytes) in new[] { ($"/Workspace/BodyImage?tenantId={d.Tenant}&jobId={d.Job}&assetId={d.Image}", BodyImageFixture.Png), ($"/Workspace/Source?tenantId={d.Tenant}&jobId={d.Job}&sourceFileId={d.Source}&download=true", BodyImageFixture.Png), ($"/Workspace/DraftPdf?tenantId={d.Tenant}&jobId={d.Job}&pdfId={d.Pdf}", d.PdfBytes) })
        {
            var response = await page.APIRequest.GetAsync(host.Urls["web"] + path, new() { MaxRedirects = 0 }); Assert.Equal(status, response.Status);
            if (status == 200) { Assert.Equal(Hash(bytes), Hash(await response.BodyAsync())); Assert.Contains("no-store", response.Headers["cache-control"]); }
        }
    }
    private async Task<JsonElement> Upload(IsolatedSqlProductHost host, Account account, string path, int expected)
    {
        using var request = Request(host, "documents", account, HttpMethod.Post, path); using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(BodyImageFixture.Png); image.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(image, "file", "access-fixture.png"); request.Content = form;
        using var response = await http.SendAsync(request); Assert.Equal(expected, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { });
        using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static HttpRequestMessage Request(IsolatedSqlProductHost host, string service, Account? account, HttpMethod method, string path)
    { var request = new HttpRequestMessage(method, host.Urls[service] + path); if (account is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token); return request; }
    private async Task<JsonElement> Api(IsolatedSqlProductHost host, string service, Account? account, HttpMethod method, string path, object? body = null, int expected = 200)
    {
        using var request = Request(host, service, account, method, path); if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{service} {method} {path}: expected {expected}, received {(int)response.StatusCode}. {text[..Math.Min(200, text.Length)]}");
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { });
        using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone();
    }
}
