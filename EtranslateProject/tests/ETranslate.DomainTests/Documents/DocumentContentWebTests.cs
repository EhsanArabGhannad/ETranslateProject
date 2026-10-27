using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ETranslate.Contracts.Billing;
using ETranslate.Contracts.Documents;
using ETranslate.SubscriptionAccess;
using ETranslate.Web.Controllers;
using ETranslate.Web.Localization;
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

public sealed class DocumentContentWebTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Job = Guid.NewGuid(), Document = Guid.NewGuid();
    private static readonly string Body = DocumentText.FromPlainText("ترجمه İstanbul با نیم‌فاصله");

    [Fact] public async Task Rich_save_stamps_the_application_format_on_a_new_revision_not_the_old_source()
    {
        var fixture = new Fixture();
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.SaveDraft(Tenant, Job, 1, null, Body, "rich"));
        var payload = fixture.Writes.Single();
        Assert.Equal(DocumentContentDescriptor.RichFormat, payload.GetProperty("contentFormat").GetString());
        Assert.Equal(3, payload.GetProperty("contentSchemaVersion").GetInt32());
        Assert.Equal(Body, payload.GetProperty("editorContentJson").GetString());
        Assert.Equal("ترجمه İstanbul با نیم‌فاصله", payload.GetProperty("plainText").GetString());
        Assert.True(fixture.Original.ContentDescriptor.IsLegacy); Assert.Equal(Body, fixture.Original.EditorContentJson);
    }

    [Theory] [InlineData(null, null)] [InlineData("etranslate.rich-document", 1)]
    public async Task Raw_save_preserves_the_authoritative_descriptor_instead_of_guessing_from_JSON(string? format, int? version)
    {
        var fixture = new Fixture(format, version);
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.SaveDraft(Tenant, Job, 1, null, Body, "json"));
        var payload = fixture.Writes.Single();
        Assert.Equal(format, payload.GetProperty("contentFormat").GetString());
        Assert.Equal(version, payload.GetProperty("contentSchemaVersion").ValueKind == JsonValueKind.Null ? null : (int?)payload.GetProperty("contentSchemaVersion").GetInt32());
    }

    [Theory] [InlineData("rich")] [InlineData("text")] [InlineData("json")]
    public async Task A_forged_save_mode_cannot_downgrade_unknown_persisted_content(string mode)
    {
        var fixture = new Fixture(DocumentContentDescriptor.RichFormat, 4);
        var view = Assert.IsType<ViewResult>(await fixture.Controller.SaveDraft(Tenant, Job, 1, "Retain failed input", Body, mode));
        var model = Assert.IsType<EditorView>(view.Model);
        Assert.Empty(fixture.Writes); Assert.Equal(422, fixture.Context.Response.StatusCode);
        Assert.Equal(4, model.ContentSchemaVersion); Assert.False(model.ContentIsSupported);
        Assert.Equal(mode == "text" ? DocumentText.FromPlainText("Retain failed input") : Body, model.EditorContentJson);
        Assert.Equal(UiText.Get("BelgeIcerikSurumuDesteklenmiyor"), model.Message);
    }

    private sealed class Fixture
    {
        public List<JsonElement> Writes { get; } = [];
        public DraftView Original { get; }
        public DefaultHttpContext Context { get; }
        public WorkspaceController Controller { get; }
        public Fixture(string? format = null, int? version = null)
        {
            Original = new(1, Body, "Original projection", null, format, version);
            Context = new() { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService, Auth>()
                .AddSingleton<IUrlHelperFactory, UrlHelperFactory>().BuildServiceProvider() };
            var http = new HttpClient(new Handler(async request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                Assert.Equal("fixture", request.Headers.Authorization?.Parameter);
                object value;
                if (request.Method == HttpMethod.Post)
                {
                    Assert.EndsWith("/draft-revisions", path);
                    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); Writes.Add(payload.RootElement.Clone());
                    value = new DraftView(2, Body, null, null, format, version);
                }
                else value = path switch {
                    var p when p.EndsWith("/documents") => new DocumentView(Document, 1, null),
                    var p when p.EndsWith("/draft-revisions/1") => Original,
                    var p when p.EndsWith("/draft-revisions") => Array.Empty<DraftSummary>(),
                    var p when p.EndsWith("/pdfs") => Array.Empty<PdfVersionView>(),
                    var p when p.EndsWith("/reviews") => new ReviewStateView("Draft", 0, Guid.NewGuid(), true, true, []),
                    var p when p.EndsWith("/document-templates") => Array.Empty<TemplateView>(),
                    var p when p.EndsWith($"/translation-jobs/{Job}") => new JobView(Job, "Fixture", "fa", "tr", "NotRequired", null, null, null),
                    var p when p.EndsWith($"/tenants/{Tenant}") => new TenantView(Tenant, "Fixture", "TranslationOffice", "Owner"),
                    var p when p.EndsWith("/subscription/write-access") => new SubscriptionWriteAccessV1(Tenant, "Trialing", true, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)),
                    _ => throw new InvalidOperationException("Unexpected local fixture path: " + path)
                };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
            })) { BaseAddress = new("http://fixture.invalid") };
            Controller = new(new BackendApi(new Factory(http), new HttpContextAccessor { HttpContext = Context }), new SubscriptionWriteClient(http, TimeProvider.System))
            { ControllerContext = new() { HttpContext = Context, RouteData = new RouteData(), ActionDescriptor = new ControllerActionDescriptor() },
                TempData = new TempDataDictionary(Context, new Temp()) };
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request); }
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class Temp : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class Auth : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var properties = new AuthenticationProperties(); properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "fixture" }]);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("fixture")), properties, "fixture")));
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
