using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ETranslate.Contracts.Billing;
using ETranslate.Contracts.Documents;
using ETranslate.SubscriptionAccess;
using ETranslate.Web.Controllers;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ETranslate.DomainTests.Documents;

public sealed class TemplateSaveTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Template = Guid.NewGuid(), Asset = Guid.NewGuid();
    private static TemplateEditForm Form(bool existing = true) => new() { TenantId = Tenant, TemplateId = existing ? Template : null, ExpectedRevision = existing ? 2 : 0, Name = "Name", Header = "Edited", Footer = "Footer", StarterText = "Starter" };
    [Fact] public async Task Selected_template_loads_server_fields()
    {
        var f = new Fixture(); var view = Assert.IsType<ViewResult>(await f.Controller.Templates(Tenant, Template));
        var page = Assert.IsType<SettingsPageView>(view.Model); Assert.Equal(Template, page.Form.TemplateId); Assert.Equal(2, page.Form.ExpectedRevision); Assert.Equal("Header", page.Form.Header);
    }
    [Fact] public async Task Edit_saves_one_revision_without_metadata_or_old_content_mutation()
    {
        var f = new Fixture(); var form = Form(); form.Name = "Cannot rename through a content revision";
        Assert.IsType<RedirectToActionResult>(await f.Controller.SaveTemplate(form, null));
        Assert.Equal(["revision"], f.Writes); Assert.Equal(2, f.Payload!.RootElement.GetProperty("expectedCurrentRevision").GetInt32());
        Assert.DoesNotContain("Cannot rename", f.Payload.RootElement.GetRawText());
    }
    [Fact] public async Task New_with_logo_creates_then_uploads_then_attaches_and_redirects_to_same_identity()
    {
        var f = new Fixture(); var result = Assert.IsType<RedirectToActionResult>(await f.Controller.SaveTemplate(Form(false), File()));
        Assert.Equal(["create", "upload", "revision"], f.Writes); Assert.Equal(Template, result.RouteValues!["templateId"]);
        Assert.Contains(Asset.ToString(), f.Payload!.RootElement.GetProperty("headerContentJson").GetString());
    }
    [Fact] public async Task Failed_upload_after_create_retains_edit_identity_and_user_fields()
    {
        var f = new Fixture { FailUpload = true }; var view = Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(Form(false), File()));
        var page = Assert.IsType<SettingsPageView>(view.Model); Assert.Equal(Template, page.Form.TemplateId); Assert.Equal(1, page.Form.ExpectedRevision);
        Assert.Equal("Edited", page.Form.Header); Assert.Contains("Şablon oluşturuldu", page.Message); Assert.Equal(["create", "upload"], f.Writes);
    }
    [Fact] public async Task Failed_attach_preserves_uploaded_id_for_reuse_without_new_upload()
    {
        var f = new Fixture { FailRevision = true }; var view = Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(Form(), File()));
        var page = Assert.IsType<SettingsPageView>(view.Model); Assert.Equal(Asset, page.Form.PendingAssetId); Assert.True(page.Conflict);
        f.FailRevision = false; f.Writes.Clear();
        Assert.IsType<RedirectToActionResult>(await f.Controller.SaveTemplate(page.Form, null));
        Assert.Equal(["revision"], f.Writes);
    }
    [Fact] public async Task Stale_form_and_forged_recovery_id_never_write()
    {
        var f = new Fixture(); var stale = Form(); stale.ExpectedRevision = 1;
        var conflict = Assert.IsType<SettingsPageView>(Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(stale, null)).Model); Assert.True(conflict.Conflict); Assert.Empty(f.Writes);
        var forged = Form(); forged.PendingAssetId = Guid.NewGuid();
        Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(forged, null)); Assert.Equal(400, f.Context.Response.StatusCode); Assert.Empty(f.Writes);
    }
    [Theory] [InlineData("Reviewer", "Trialing", 403)] [InlineData("Owner", "Expired", 402)]
    public async Task Role_and_subscription_denials_do_not_upload_or_save(string role, string status, int expected)
    {
        var f = new Fixture { Role = role, Status = status };
        var result = await f.Controller.SaveTemplate(Form(), File());
        Assert.Equal(expected, result is StatusCodeResult code ? code.StatusCode : f.Context.Response.StatusCode); Assert.Empty(f.Writes);
    }
    [Fact] public async Task Invalid_file_or_page_layout_fails_before_creation()
    {
        var f = new Fixture(); var form = Form(false); form.MarginTop = 1;
        Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(form, File())); Assert.Empty(f.Writes);
    }
    [Theory] [InlineData("[\"justify\"]", null, null)] [InlineData("[]", "url(unsafe)", null)]
    [InlineData("[]", null, "full-bleed")]
    public async Task Unsupported_presentation_options_fail_before_upload(string lines, string? align, string? layout)
    {
        var f = new Fixture(); var form = Form(false); form.HeaderLineAlignmentsJson = lines; form.ImageAlignment = align; form.ImageLayout = layout;
        Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(form, File())); Assert.Equal(400, f.Context.Response.StatusCode); Assert.Empty(f.Writes);
    }
    [Theory] [InlineData("OddPages")] [InlineData("")]
    public async Task Invalid_footer_policy_never_creates_or_uploads(string policy)
    {
        var f = new Fixture(); var form = Form(false); form.FooterPlacement = policy;
        Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(form, File())); Assert.Equal(400, f.Context.Response.StatusCode); Assert.Empty(f.Writes);
    }

    [Fact] public async Task A_new_template_declares_rich_v1_and_the_logo_revision_inherits_that_declaration()
    {
        var f = new Fixture();
        Assert.IsType<RedirectToActionResult>(await f.Controller.SaveTemplate(Form(false), File()));
        Assert.Equal(DocumentContentDescriptor.RichFormat, f.Payload!.RootElement.GetProperty("contentFormat").GetString());
        Assert.Equal(1, f.Payload.RootElement.GetProperty("contentSchemaVersion").GetInt32());
    }

    [Fact] public async Task A_future_template_cannot_be_edited_or_receive_an_upload_through_a_forged_form()
    {
        var f = new Fixture(); f.DeclareContent(DocumentContentDescriptor.RichFormat, 10);
        var page = Assert.IsType<SettingsPageView>(Assert.IsType<ViewResult>(await f.Controller.SaveTemplate(Form(), File())).Model);
        Assert.Equal(422, f.Context.Response.StatusCode); Assert.Empty(f.Writes); Assert.False(page.CanSave);
    }

    private static IFormFile File() => new FormFile(new MemoryStream([137,80,78,71]), 0, 4, "logo", "logo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
    private sealed class Fixture
    {
        public string Role = "Owner", Status = "Trialing";
        public bool FailUpload, FailRevision;
        public List<string> Writes { get; } = [];
        public JsonDocument? Payload;
        public DefaultHttpContext Context { get; }
        public SettingsController Controller { get; }
        private TemplateDetail _detail = new(Template, "Name", null, true, 2, new(Guid.NewGuid(), Template, 2, DocumentText.FromPlainText("Starter"), DocumentText.FromPlainText("Header"), DocumentText.FromPlainText("Footer"), DocumentPageLayout.Default.ToJson(), null));
        public void DeclareContent(string format, int version) => _detail = _detail with { Revision = _detail.Revision with { ContentFormat = format, ContentSchemaVersion = version } };
        public Fixture()
        {
            Context = new() { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService, Auth>().AddSingleton<IUrlHelperFactory, UrlHelperFactory>().BuildServiceProvider() };
            var http = new HttpClient(new Handler(async request =>
            {
                Assert.Equal("fixture", request.Headers.Authorization?.Parameter); var path = request.RequestUri!.AbsolutePath;
                object value;
                if (request.Method == HttpMethod.Get)
                {
                    if (path == "/api/v1/tenants") value = new[] { new TenantView(Tenant, "Office", "TranslationOffice", Role) };
                    else if (path.EndsWith("/write-access")) value = new SubscriptionWriteAccessV1(Tenant, Status, Status == "Trialing", Status == "Trialing" ? null : "subscription_required", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(Status == "Trialing" ? 20 : -1));
                    else if (path.EndsWith("/assets")) value = new[] { new AssetView(Asset) };
                    else if (path.EndsWith(Template.ToString())) value = _detail;
                    else value = new[] { new TemplateView(Template, "Name", true, _detail.CurrentRevision) };
                }
                else if (path.EndsWith("/assets")) { Writes.Add("upload"); if (FailUpload) return new(HttpStatusCode.BadRequest); value = new AssetView(Asset); }
                else
                {
                    Payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    var data = Payload.RootElement;
                    if (path.EndsWith("/revisions"))
                    {
                        Writes.Add("revision"); if (FailRevision) return new(HttpStatusCode.Conflict);
                        value = _detail.Revision with { Id = Guid.NewGuid(), RevisionNumber = data.GetProperty("expectedCurrentRevision").GetInt32() + 1 };
                    }
                    else
                    {
                        Writes.Add("create"); _detail = new(Template, data.GetProperty("name").GetString()!, null, true, 1, new(Guid.NewGuid(), Template, 1,
                            data.GetProperty("editorContentJson").GetString()!, data.GetProperty("headerContentJson").GetString(), data.GetProperty("footerContentJson").GetString(), data.GetProperty("pageLayoutJson").GetString()!, data.GetProperty("watermarkJson").GetString(),
                            data.GetProperty("contentFormat").GetString(), data.GetProperty("contentSchemaVersion").GetInt32())); value = _detail;
                    }
                }
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
            })) { BaseAddress = new("http://fixture.invalid") };
            Controller = new(new BackendApi(new Factory(http), new HttpContextAccessor { HttpContext = Context }), new SubscriptionWriteClient(http, TimeProvider.System))
            { ControllerContext = new() { HttpContext = Context, RouteData = new RouteData(), ActionDescriptor = new ControllerActionDescriptor() }, TempData = new TempDataDictionary(Context, new Temp()) };
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request); }
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class Temp : ITempDataProvider { public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(); public void SaveTempData(HttpContext context, IDictionary<string, object> values) { } }
    private sealed class Auth : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) { var p = new AuthenticationProperties(); p.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "fixture" }]); return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("fixture")), p, "fixture"))); }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
