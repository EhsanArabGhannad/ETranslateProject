using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Documents;
using ETranslate.Trust.Api.Persistence;
using ETranslate.StorageAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalDocumentJourneyFactAttribute : FactAttribute
{
    public LocalDocumentJourneyFactAttribute() {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_DOCUMENT_JOURNEY_TESTS") != "1") Skip = "Run Verify-DocumentJourney.ps1; new QA catalogs only.";
        Timeout = 420000;
    }
}

public sealed class DocumentJourneyAcceptanceTests
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly List<object> cases = []; private readonly List<string> errors = [], external = [], csp = [];
    private sealed record Account(string Email, string Password, string Token, Guid Id);

    [LocalDocumentJourneyFact]
    public async Task Real_office_and_independent_journeys_preserve_approval_and_unsigned_preparation_history()
    {
        var root = Environment.GetEnvironmentVariable("ETRANSLATE_DOCUMENT_JOURNEY_ROOT") ?? throw new InvalidOperationException("Explicit root required.");
        var server = Environment.GetEnvironmentVariable("ETRANSLATE_DOCUMENT_JOURNEY_SERVER") ?? @".\ESIMSSQLSERVER";
        await using var host = new IsolatedSqlProductHost(server, root, "document-journey");
        try {
            await host.StartAsync(); using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            var owner = await Register("owner"); var translator = await Register("translator");
            foreach (var office in new[] { true, false }) {
                var tenant = (await Api(owner, "identity-access", HttpMethod.Post, "/api/v1/tenants", new { name = office ? "Synthetic office journey" : "Synthetic solo journey", slug = (office ? "office-" : "solo-") + host.RunId, type = office ? "TranslationOffice" : "IndependentTranslator" }, 201)).GetProperty("id").GetGuid();
                var canWrite = false; for(var n=0;n<100;n++){canWrite=(await Api(owner,"billing",HttpMethod.Get,$"/api/v1/tenants/{tenant}/subscription/write-access")).GetProperty("canWrite").GetBoolean();if(canWrite)break;await Task.Delay(200);} Assert.True(canWrite);
                owner = owner with { Id = (await Api(owner,"identity-access",HttpMethod.Get,$"/api/v1/tenants/{tenant}/access")).GetProperty("userId").GetGuid() };
                if (office) {
                    var team = $"/api/v1/tenants/{tenant}/team"; var teamState = await Api(owner,"identity-access",HttpMethod.Get,team);
                    var invite = await Api(owner,"identity-access",HttpMethod.Post,team+"/invitations",new {email=translator.Email,role="Translator",expectedTeamVersion=teamState.GetProperty("teamVersion").GetInt64()},201);
                    await Api(translator,"identity-access",HttpMethod.Post,$"/api/v1/team-invitations/{invite.GetProperty("id").GetGuid()}/accept",new {token=invite.GetProperty("token").GetString()});
                    translator = translator with { Id = (await Api(translator,"identity-access",HttpMethod.Get,$"/api/v1/tenants/{tenant}/access")).GetProperty("userId").GetGuid() };
                }
                var writer = office ? translator : owner;
                var jobs = $"/api/v1/tenants/{tenant}/translation-jobs";
                var jobResult = await Api(owner,"translation-workflow",HttpMethod.Post,jobs,new {title="Synthetic full journey",sourceLanguageCode="fa",targetLanguageCode="tr",notaryRequirement=office?"NotRequired":"Required",notaryProcessingMode=office?(string?)null:"Physical",acceptanceProfile=office?(string?)null:"Other",acceptanceProfileOther=office?null:"Synthetic recipient"},201);
                var job=jobResult.GetProperty("id").GetGuid();
                Assert.Equal(office?"TranslatorAndTranslationOffice":"TranslatorOnly",jobResult.GetProperty("signaturePolicy").GetString());
                var collection=jobs+$"/{job}/documents"; var doc=(await Api(owner,"documents",HttpMethod.Post,collection,expected:201)).GetProperty("id").GetGuid(); var path=collection+"/"+doc; var plans=path+"/signing-preparations";
                var template=(await Api(owner,"documents",HttpMethod.Post,$"/api/v1/tenants/{tenant}/document-templates",new {name="Journey letterhead",editorContentJson=Body("JOURNEY_BODY ترجمه İstanbul"),headerContentJson=Body("JOURNEY_HEADER"),footerContentJson=Body("JOURNEY_FOOTER"),pageLayoutJson=DocumentPageLayout.Default.ToJson(),contentFormat=DocumentContentDescriptor.RichFormat,contentSchemaVersion=1},201)).GetProperty("id").GetGuid();
                await Api(owner,"documents",HttpMethod.Post,path+"/template",new {templateId=template,revisionNumber=1});
                using(var form=new MultipartFormDataContent()) {
                    var content=new ByteArrayContent(BodyImageFixture.Png); content.Headers.ContentType=new("image/png");form.Add(content,"file","synthetic.png");
                    using var request=new HttpRequestMessage(HttpMethod.Post,host.Urls["documents"]+path+"/source-files?uploadId="+Guid.NewGuid()){Content=form};request.Headers.Authorization=new("Bearer",writer.Token);
                    using var response=await http.SendAsync(request);Assert.Equal(201,(int)response.StatusCode);
                }
                await using var editing = await Context(browser); var editor=await Login(editing,writer);
                await editor.GotoAsync(host.Urls["web"]+$"/Workspace/Editor?tenantId={tenant}&jobId={job}");
                var text=editor.Locator("#rich-text [role=textbox]"); await Assertions.Expect(text).ToHaveAttributeAsync("contenteditable","true");
                await text.PressAsync("Control+End"); await editor.Keyboard.InsertTextAsync(" JOURNEY_BROWSER_EDIT");
                await editor.Locator("#draft-form button[type=submit]").ClickAsync();
                await Assertions.Expect(editor.Locator("#draft-form input[name=expectedRevision]")).ToHaveValueAsync("2");
                await Assertions.Expect(text).ToContainTextAsync("JOURNEY_BROWSER_EDIT");
                var draft=await Api(writer,"documents",HttpMethod.Get,path+"/draft-revisions/2"); var saved=draft.GetProperty("editorContentJson").GetString()!;
                Assert.Contains("JOURNEY_BROWSER_EDIT",saved);
                var pdf=await Api(writer,"documents",HttpMethod.Post,path+"/draft-revisions/2/pdfs",expected:201); var pdfId=pdf.GetProperty("id").GetGuid();var hash=pdf.GetProperty("sha256").GetString()!;
                var original=await Download(owner,path+"/pdfs/"+pdfId,hash,"draft-"+(office?"office":"solo")+".pdf");
                var proposal=new {expectedReviewId=Guid.NewGuid(),expectedPdfVersionId=pdfId,translatorUserId=writer.Id,translatorStatement="PROPOSED ترجمه İstanbul",translatorMethod="MobilImza",officeSignerUserId=office?(Guid?)owner.Id:null,officeStatement=office?"PROPOSED OFFICE":null,officeMethod=office?"EImza":null};
                await Api(owner,"trust",HttpMethod.Post,plans,proposal,409);
                var review=await Api(writer,"documents",HttpMethod.Post,path+"/reviews",new {expectedRevision=2,expectedRound=0,pdfVersionId=pdfId},201);var reviewId=review.GetProperty("id").GetGuid();
                if(office) await Api(writer,"documents",HttpMethod.Post,path+$"/reviews/{reviewId}/decision",new {action="Approve"},403);
                await Api(owner,"trust",HttpMethod.Post,plans,proposal,409);
                await Api(owner,"documents",HttpMethod.Post,path+$"/reviews/{reviewId}/decision",new {action="Approve"});
                await editor.ReloadAsync();await Assertions.Expect(editor.Locator("[data-editable]").First).ToHaveAttributeAsync("data-editable","false");
                await using var management=await Context(browser);var manager=await Login(management,owner);
                await manager.GotoAsync(host.Urls["web"]+$"/Workspace/SigningPreparation?tenantId={tenant}&jobId={job}");
                await manager.Locator("[name='Form.TranslatorUserId']").SelectOptionAsync(writer.Id.ToString());
                await manager.Locator("[name='Form.TranslatorMethod']").SelectOptionAsync("MobilImza");
                await manager.Locator("[name='Form.TranslatorStatement']").FillAsync("PROPOSED ترجمه İstanbul <script>JOURNEY_XSS</script>");
                if(office){await manager.Locator("[name='Form.OfficeSignerUserId']").SelectOptionAsync(owner.Id.ToString());await manager.Locator("[name='Form.OfficeStatement']").FillAsync("PROPOSED OFFICE");}
                else await Assertions.Expect(manager.Locator("[name='Form.OfficeStatement']")).ToHaveCountAsync(0);
                await manager.Locator("form[action='/Workspace/PrepareSigning'] button").ClickAsync();
                await Assertions.Expect(manager.Locator("article.team-member")).ToHaveCountAsync(1);
                Assert.DoesNotContain("<script>JOURNEY_XSS</script>",await manager.ContentAsync());
                var state=await Api(owner,"trust",HttpMethod.Get,plans);var plan=state.GetProperty("preparations")[0];var planId=plan.GetProperty("id").GetGuid();
                Assert.Equal(office?2:1,plan.GetProperty("stages").GetArrayLength()); Assert.Equal(hash,plan.GetProperty("pdfSha256").GetString());
                await manager.Locator("form[action='/Workspace/GenerateSigningPreview'] button").ClickAsync();
                await Assertions.Expect(manager.Locator("a[href*='/Workspace/SigningPreviewPdf']")).ToHaveCountAsync(1);
                var preview=(await Api(owner,"documents",HttpMethod.Get,path+"/signing-previews"))[0]; var previewId=preview.GetProperty("id").GetGuid();
                var privateLink=await manager.Locator("a[href*='/Workspace/SigningPreviewPdf']").GetAttributeAsync("href");
                var privateResponse=await management.APIRequest.GetAsync(host.Urls["web"]+privateLink);
                Assert.Equal(200,privateResponse.Status);
                Assert.Equal(preview.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(await privateResponse.BodyAsync())).ToLowerInvariant());
                await privateResponse.DisposeAsync();
                await Download(writer,path+$"/signing-previews/{previewId}/file",preview.GetProperty("sha256").GetString()!,"preview-"+(office?"office":"solo")+".pdf");
                Assert.Equal(previewId,(await Api(owner,"documents",HttpMethod.Post,path+$"/signing-preparations/{planId}/preview-pdfs")).GetProperty("id").GetGuid());
                await Api(owner,"trust",HttpMethod.Post,plans+$"/{planId}/dispatch",expected:501);
                if(office) await Api(writer,"trust",HttpMethod.Post,plans,proposal,403);
                var otherTenant=(await Api(owner,"identity-access",HttpMethod.Post,"/api/v1/tenants",new {name="Synthetic isolation",slug="other-"+Guid.NewGuid().ToString("N"),type="IndependentTranslator"},201)).GetProperty("id").GetGuid();
                await Api(owner,"documents",HttpMethod.Get,path.Replace(tenant.ToString(),otherTenant.ToString())+$"/signing-previews/{previewId}/file",expected:404);
                await manager.ScreenshotAsync(new(){Path=Path.Combine(host.Output,office?"office.png":"solo.png"),FullPage=true});
                await Api(owner,"documents",HttpMethod.Post,path+$"/reviews/{reviewId}/decision",new {action="Reopen",note="Synthetic correction"});
                state=await Api(owner,"trust",HttpMethod.Get,plans);Assert.False(state.GetProperty("preparations")[0].GetProperty("isCurrentArtifact").GetBoolean());
                await Api(owner,"documents",HttpMethod.Post,path+$"/signing-preparations/{planId}/preview-pdfs",expected:409);
                await Api(owner,"trust",HttpMethod.Post,plans+$"/{planId}/cancel",new {expectedVersion=0,reason="Synthetic correction"});
                await Api(owner,"trust",HttpMethod.Post,plans+$"/{planId}/cancel",new {expectedVersion=0,reason="Duplicate"},409);
                Assert.Equal(original,await Download(owner,path+"/pdfs/"+pdfId,hash,null));
                Assert.Equal(saved,(await Api(writer,"documents",HttpMethod.Get,path+"/draft-revisions/2")).GetProperty("editorContentJson").GetString());
                await using(var db=Db(host)){var record=await db.TranslationDocuments.AsNoTracking().SingleAsync(x=>x.Id==doc);Assert.Equal(2,record.CurrentDraftRevision);Assert.Single(await db.SigningPreviews.Where(x=>x.DocumentId==doc).ToArrayAsync());Assert.Equal(writer.Id,(await db.DraftRevisions.SingleAsync(x=>x.DocumentId==doc&&x.RevisionNumber==2)).CreatedByUserId);Assert.Contains((await db.SourceFiles.SingleAsync(x=>x.DocumentId==doc)).Id.ToString(),(await db.Reviews.SingleAsync(x=>x.Id==reviewId)).SourceFilesJson);}
                await using(var trust=new TrustDbContext(new DbContextOptionsBuilder<TrustDbContext>().UseSqlServer(host.Connection("trustdb")).Options)){var record=await trust.Preparations.Include(x=>x.Stages).SingleAsync(x=>x.Id==planId);Assert.Equal("Cancelled",record.Status.ToString());Assert.Equal(1,record.Version);Assert.Equal(writer.Id,record.Stages.Single(x=>x.Order==1).SignerUserId);}
                cases.Add(new {office,job,doc,planId,previewId,revision=2,stages=office?2:1,passed=true});
            }
            await using(var db=Db(host)){var inventory=await new BlobInventory(TimeProvider.System).ScanAsync(host.Storage,await SqlBlobReferences.ReadAsync(db,100,default),new(),default);Assert.True(inventory.Complete);Assert.False(inventory.HasFindings);Assert.Equal(6,inventory.Counts["Verified"]);}
            Assert.Empty(errors);Assert.Empty(external);Assert.Empty(csp);
            var latest=Path.Combine(root,"tmp/document-journey");Directory.CreateDirectory(latest);await File.WriteAllTextAsync(Path.Combine(latest,"latest.json"),JsonSerializer.Serialize(new {runId=host.RunId}));

            async Task<Account> Register(string role){var email=$"journey-{role}-{host.RunId}@etranslate.local";var password="Qa-"+Guid.NewGuid().ToString("N")+"!";await Api(null,"identity-access",HttpMethod.Post,"/api/v1/auth/register",new {email,password});var login=await Api(null,"identity-access",HttpMethod.Post,"/api/v1/auth/login",new {email,password});return new(email,password,login.GetProperty("accessToken").GetString()!,Guid.Empty);}
            async Task<IBrowserContext> Context(IBrowser b){var context=await b.NewContextAsync(new(){ViewportSize=new(){Width=1440,Height=1050},ServiceWorkers=ServiceWorkerPolicy.Block});await context.AddInitScriptAsync("document.addEventListener('securitypolicyviolation',e=>console.log('JOURNEY_CSP:'+e.effectiveDirective));");await context.RouteAsync("**/*",async route=>{if(new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)!=host.Urls["web"]){external.Add(route.Request.Url);await route.AbortAsync();}else await route.ContinueAsync();});return context;}
            async Task<IPage> Login(IBrowserContext context,Account account){var page=await context.NewPageAsync();page.PageError+=(_,e)=>errors.Add(e);page.Console+=(_,m)=>{if(m.Text.StartsWith("JOURNEY_CSP:"))csp.Add(m.Text);};await page.GotoAsync(host.Urls["web"]+"/Account/Login");await page.Locator("#Email").FillAsync(account.Email);await page.Locator("#Password").FillAsync(account.Password);await page.Locator(".auth-card button[type=submit]").ClickAsync();await page.WaitForURLAsync("**/Workspace");return page;}
            async Task<byte[]> Download(Account account,string path,string hash,string? name)
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,host.Urls["documents"]+path);request.Headers.Authorization=new("Bearer",account.Token);
                using var response=await http.SendAsync(request);Assert.Equal(200,(int)response.StatusCode);
                var bytes=await response.Content.ReadAsByteArrayAsync();Assert.Equal(hash,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                using var pdf=PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                if(name is not null){Assert.Equal(name.StartsWith("preview",StringComparison.Ordinal)?(name.Contains("office",StringComparison.Ordinal)?4:3):1,pdf.PageCount);await File.WriteAllBytesAsync(Path.Combine(host.Output,name),bytes);}
                return bytes;
            }
            async Task<JsonElement> Api(Account? account,string service,HttpMethod method,string path,object? body=null,int expected=200){using var request=new HttpRequestMessage(method,host.Urls[service]+path);if(account is not null)request.Headers.Authorization=new("Bearer",account.Token);if(body is not null)request.Content=JsonContent.Create(body);using var response=await http.SendAsync(request);var content=await response.Content.ReadAsStringAsync();Assert.True((int)response.StatusCode==expected,$"{service} {method} {path}: expected {expected}, got {(int)response.StatusCode}: {content[..Math.Min(300,content.Length)]}");if(string.IsNullOrWhiteSpace(content)||response.Content.Headers.ContentType?.MediaType!="application/json")return JsonSerializer.SerializeToElement(new {});using var parsed=JsonDocument.Parse(content);return parsed.RootElement.Clone();}
        }
        finally{http.Dispose();await File.WriteAllTextAsync(Path.Combine(host.Output,"journey-report.json"),JsonSerializer.Serialize(new {runId=host.RunId,cases,errors,external,csp,existingDatabasesUsed=false},new JsonSerializerOptions{WriteIndented=true}));}
    }
    private static DocumentsDbContext Db(IsolatedSqlProductHost host)=>new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(host.Connection("documentsdb")).Options);
    private static string Body(string text)=>JsonSerializer.Serialize(new {type="doc",content=new[]{new {type="paragraph",content=new[]{new {type="text",text}}}}});
}
