using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ETranslate.Contracts.Billing;
using ETranslate.SubscriptionAccess;
using ETranslate.Web.Controllers;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class SubscriptionEnforcementWebTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Job = Guid.NewGuid(), Document = Guid.NewGuid();
    private const string Submitted = "این متن ذخیره نشده است؛ باید قابل کپی باقی بماند.";

    [Theory]
    [InlineData(402, "subscription_required", SubscriptionWriteStatus.Required)]
    [InlineData(503, "subscription_pending", SubscriptionWriteStatus.Pending)]
    [InlineData(503, "subscription_unavailable", SubscriptionWriteStatus.Unavailable)]
    public async Task Rejected_saves_preserve_text_and_status_without_fake_success(int status, string code, SubscriptionWriteStatus write)
    {
        var (controller, context) = Create(status, code, write);
        var result = Assert.IsType<ViewResult>(await controller.SaveDraft(Tenant, Job, 1, Submitted, null, "text"));
        var model = Assert.IsType<EditorView>(result.Model);
        Assert.Equal(Submitted, DocumentText.TryPlainText(model.EditorContentJson));
        Assert.Equal(status, context.Response.StatusCode);
        Assert.False(model.CanWrite); Assert.Equal(write, model.WriteStatus);
        Assert.Equal(new BackendException((HttpStatusCode)status, code).UserMessage, model.Message);
        Assert.Null(controller.TempData["Notice"]);
    }

    [Theory] [InlineData(SubscriptionWriteStatus.Required)] [InlineData(SubscriptionWriteStatus.Pending)] [InlineData(SubscriptionWriteStatus.Unavailable)]
    public async Task Workspace_remains_readable_without_a_write_grant(SubscriptionWriteStatus write)
    {
        var (controller, _) = Create(503, "subscription_unavailable", write);
        var result = Assert.IsType<ViewResult>(await controller.Index(Tenant));
        var model = Assert.IsType<WorkspaceView>(result.Model);
        Assert.Equal(Tenant, model.Tenant!.Id); Assert.False(model.CanWrite);
        Assert.Equal(write, model.WriteStatus); Assert.Single(model.Jobs);
    }

    [Theory] [InlineData(402, "unknown")] [InlineData(402, "subscription_pending")] [InlineData(503, "subscription_required")]
    public async Task Unknown_or_incoherent_upstream_codes_are_not_presented(int status, string code)
    {
        var (controller, _) = Create(status, code, SubscriptionWriteStatus.Allowed);
        var result = Assert.IsType<ViewResult>(await controller.SaveDraft(Tenant, Job, 1, Submitted, null, "text"));
        Assert.Equal(new BackendException((HttpStatusCode)status).UserMessage, Assert.IsType<EditorView>(result.Model).Message);
    }

    [Fact] public void Readonly_models_fail_closed_by_default()
    {
        Assert.False(new WorkspaceView().CanWrite); Assert.False(new EditorView().CanWrite);
        Assert.False(new AssignmentPageView().CanWrite); Assert.False(new SigningPageView().CanWrite);
    }

    [Theory]
    [InlineData(402, "subscription_required", SubscriptionWriteStatus.Required)]
    [InlineData(503, "subscription_unavailable", SubscriptionWriteStatus.Unavailable)]
    public async Task Enhanced_editor_receives_a_bounded_JSON_failure_without_reloading_the_editor(int status, string code, SubscriptionWriteStatus write)
    {
        var (controller, context) = Create(status, code, write); context.Request.Headers.Accept = "application/json";
        var result = Assert.IsType<ObjectResult>(await controller.SaveDraft(Tenant, Job, 1, Submitted, null, "text"));
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(new BackendException((HttpStatusCode)status, code).UserMessage,
            System.Text.Json.JsonSerializer.SerializeToElement(result.Value).GetProperty("message").GetString());
    }

    private static (WorkspaceController, HttpContext) Create(int status, string code, SubscriptionWriteStatus write)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService, Auth>().BuildServiceProvider() };
        var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("fixture", request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post) return new((HttpStatusCode)status) { Content = JsonContent.Create(new { code }) };
            object value = path switch
            {
                "/api/v1/tenants" => new[] { new TenantView(Tenant, "Fixture office", "TranslationOffice", "Owner") },
                var p when p == $"/api/v1/tenants/{Tenant}" => new TenantView(Tenant, "Fixture office", "TranslationOffice", "Owner"),
                var p when p.EndsWith("/subscription/write-access") => Decision(write),
                var p when p.EndsWith("/translation-jobs") => new[] { JobView() },
                var p when p.EndsWith($"/translation-jobs/{Job}") => JobView(),
                var p when p.EndsWith("/documents") => new DocumentView(Document, 1, null),
                var p when p.EndsWith("/document-templates") => Array.Empty<TemplateView>(),
                var p when p.EndsWith("/draft-revisions") => Array.Empty<DraftSummary>(),
                var p when p.EndsWith("/pdfs") => Array.Empty<PdfVersionView>(),
                var p when p.EndsWith("/reviews") => new ReviewStateView("Draft", 0, Guid.NewGuid(), true, true, []),
                var p when p.EndsWith("/draft-revisions/1") => new DraftView(1, DocumentText.FromPlainText("Original text"), "Original text"),
                _ => throw new Exception($"Unexpected fixture path {path}")
            };
            if (path.EndsWith("/subscription/write-access") && write == SubscriptionWriteStatus.Unavailable)
                return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
        })) { BaseAddress = new("http://fixture.invalid") };
        var api = new BackendApi(new Factory(http), new HttpContextAccessor { HttpContext = context });
        return (new WorkspaceController(api, new SubscriptionWriteClient(http, TimeProvider.System))
        { ControllerContext = new() { HttpContext = context }, TempData = new TempDataDictionary(context, new Temp()) }, context);
    }
    private static SubscriptionWriteAccessV1 Decision(SubscriptionWriteStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return status switch
        {
            SubscriptionWriteStatus.Allowed => new(Tenant, "Trialing", true, null, now, now.AddDays(1)),
            SubscriptionWriteStatus.Pending => new(Tenant, "Pending", false, "subscription_pending", now, null),
            _ => new(Tenant, "Expired", false, "subscription_required", now, now.AddDays(-1))
        };
    }
    private static JobView JobView() => new(Job, "Fixture", "fa", "tr", "NotRequired", null, null, null);
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request)); }
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
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("Fixture")), properties, "Fixture")));
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
