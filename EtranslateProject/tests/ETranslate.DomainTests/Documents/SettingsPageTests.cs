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

namespace ETranslate.DomainTests.Documents;

public sealed class SettingsPageTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    [Theory] [InlineData("Owner", true)] [InlineData("Administrator", true)] [InlineData("Translator", true)] [InlineData("Reviewer", false)]
    public async Task Template_page_uses_live_membership_and_existing_roles(string role, bool canWrite)
    {
        var (controller, context, requests) = Create(role);
        var page = Assert.IsType<SettingsPageView>(Assert.IsType<ViewResult>(await controller.Templates(Tenant)).Model);
        Assert.Equal(Tenant, page.Tenant!.Id); Assert.Single(page.Templates); Assert.Equal(canWrite, page.CanWrite);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
        Assert.Equal(3, requests.Count); Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }
    [Fact] public async Task Settings_landing_does_not_depend_on_documents_or_billing()
    {
        var (controller, _, requests) = Create();
        Assert.IsType<ViewResult>(await controller.Index(Tenant)); Assert.Single(requests);
        Assert.Equal("/api/v1/tenants", requests[0].Path);
    }
    [Fact] public async Task Foreign_tenant_never_falls_back_to_first_membership()
    {
        var (controller, _, requests) = Create();
        Assert.IsType<NotFoundResult>(await controller.Templates(Guid.NewGuid())); Assert.Single(requests);
    }
    [Fact] public async Task Empty_or_malformed_scope_is_rejected_without_backend_calls()
    {
        var (controller, _, requests) = Create();
        Assert.IsType<BadRequestResult>(await controller.Templates(Guid.Empty)); Assert.Empty(requests);
        controller.ModelState.AddModelError("tenantId", "invalid");
        Assert.IsType<BadRequestResult>(await controller.Index(null)); Assert.Empty(requests);
    }
    [Fact] public async Task No_memberships_gives_an_empty_page_without_provisioning_anything()
    {
        var (controller, _, requests) = Create(empty: true);
        var page = Assert.IsType<SettingsPageView>(Assert.IsType<ViewResult>(await controller.Templates(null)).Model);
        Assert.Null(page.Tenant); Assert.Empty(page.Templates); Assert.False(page.CanWrite); Assert.Single(requests);
    }
    [Theory] [InlineData("Expired", "subscription_required", SubscriptionWriteStatus.Required)]
    [InlineData("Pending", "subscription_pending", SubscriptionWriteStatus.Pending)]
    public async Task Denied_subscription_keeps_the_template_list_readable(string status, string reason, SubscriptionWriteStatus expected)
    {
        var (controller, _, _) = Create(lease: (status, reason));
        var page = Assert.IsType<SettingsPageView>(Assert.IsType<ViewResult>(await controller.Templates(Tenant)).Model);
        Assert.Equal(expected, page.WriteStatus); Assert.Single(page.Templates); Assert.False(page.CanWrite);
    }
    [Theory] [InlineData(401)] [InlineData(403)]
    public async Task Lost_authorization_never_reads_templates(int status)
    {
        var (controller, _, requests) = Create(billingStatus: status);
        Assert.Equal((HttpStatusCode)status, (await Assert.ThrowsAsync<BackendException>(() => controller.Templates(Tenant))).Status);
        Assert.Equal(2, requests.Count);
    }

    private static (SettingsController, HttpContext, List<(HttpMethod Method, string Path)> Requests) Create(
        string role = "Owner", bool empty = false, (string Status, string Reason)? lease = null, int billingStatus = 200)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService, Auth>().BuildServiceProvider() };
        var requests = new List<(HttpMethod, string)>();
        var http = new HttpClient(new Handler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            Assert.Equal("Bearer fixture", request.Headers.Authorization?.ToString());
            if (request.RequestUri.AbsolutePath == "/api/v1/tenants") return new(HttpStatusCode.OK)
            { Content = JsonContent.Create(empty ? Array.Empty<TenantView>() : [new TenantView(Tenant, "Office", "TranslationOffice", role)]) };
            if (request.RequestUri.AbsolutePath.EndsWith("/write-access")) return new((HttpStatusCode)billingStatus)
            { Content = JsonContent.Create(new SubscriptionWriteAccessV1(Tenant, lease?.Status ?? "Trialing", lease is null,
                lease?.Reason, DateTimeOffset.UtcNow, lease?.Status == "Pending" ? null : DateTimeOffset.UtcNow.AddDays(lease is null ? 20 : -1))) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new TemplateView(Guid.NewGuid(), "Template", true, 2) }) };
        })) { BaseAddress = new("http://fixture.invalid") };
        return (new SettingsController(new BackendApi(new Factory(http), new HttpContextAccessor { HttpContext = context }), new SubscriptionWriteClient(http, TimeProvider.System))
        { ControllerContext = new() { HttpContext = context }, TempData = new TempDataDictionary(context, new Temp()) }, context, requests);
    }
    private sealed class Temp : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
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
