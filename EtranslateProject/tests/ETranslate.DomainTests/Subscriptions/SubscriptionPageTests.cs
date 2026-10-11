using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ETranslate.Web.Controllers;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class SubscriptionPageTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static SubscriptionView Trial() => new(Tenant, "trial", "Trialing", Now.AddDays(-1), Now.AddDays(29), Now, 29);
    [Theory] [InlineData("Trialing", 29)] [InlineData("Expired", 0)]
    public async Task ValidStatusIsPresentedWithoutActivationOrMutations(string status, int days)
    {
        var view = Trial() with { Status = status, RemainingTrialDays = days };
        if (status == "Expired") view = view with { TrialStartedAtUtc = Now.AddDays(-31), TrialEndsAtUtc = Now.AddDays(-1) };
        var (controller, context) = Create(HttpStatusCode.OK, view);
        var result = Assert.IsType<ViewResult>(await controller.Index(Tenant));
        Assert.Equal(view, Assert.IsType<SubscriptionPageView>(result.Model).Subscription);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
    }
    [Fact] public async Task PendingDoesNotInventDatesOrStartATrial()
    {
        var pending = new SubscriptionView(Tenant, null, "Pending", null, null, Now, null);
        var (controller, context) = Create(HttpStatusCode.Accepted, pending);
        var page = Assert.IsType<SubscriptionPageView>(Assert.IsType<ViewResult>(await controller.Index(Tenant)).Model);
        Assert.Equal(pending, page.Subscription); Assert.Equal(202, context.Response.StatusCode); Assert.Null(page.Error);
    }
    [Theory] [InlineData(503)] [InlineData(502)]
    public async Task OutageDoesNotBecomeExpiredOrTrialing(int status)
    {
        var (controller, context) = Create((HttpStatusCode)status, null);
        var page = Assert.IsType<SubscriptionPageView>(Assert.IsType<ViewResult>(await controller.Index(Tenant)).Model);
        Assert.Null(page.Subscription); Assert.NotNull(page.Error); Assert.Equal(status, context.Response.StatusCode);
    }
    [Fact] public async Task MismatchedTenantIsNeverShown()
    {
        var (controller, context) = Create(HttpStatusCode.OK, Trial() with { TenantId = Guid.NewGuid() });
        var page = Assert.IsType<SubscriptionPageView>(Assert.IsType<ViewResult>(await controller.Index(Tenant)).Model);
        Assert.Null(page.Subscription); Assert.Equal(502, context.Response.StatusCode);
    }
    [Fact] public async Task PendingHttpStatusMustMatchBody()
    {
        var (controller, context) = Create(HttpStatusCode.Accepted, Trial());
        var page = Assert.IsType<SubscriptionPageView>(Assert.IsType<ViewResult>(await controller.Index(Tenant)).Model);
        Assert.Null(page.Subscription); Assert.Equal(502, context.Response.StatusCode);
    }
    [Fact] public async Task BrokenJsonFailsClosed()
    {
        var (controller, context) = Create(HttpStatusCode.OK, null, malformed: true);
        var page = Assert.IsType<SubscriptionPageView>(Assert.IsType<ViewResult>(await controller.Index(Tenant)).Model);
        Assert.Null(page.Subscription); Assert.Equal(502, context.Response.StatusCode);
    }
    [Theory] [InlineData(401)] [InlineData(403)]
    public async Task AuthorizationFailureIsNotMasked(int status)
    {
        var (controller, _) = Create((HttpStatusCode)status, null);
        var error = await Assert.ThrowsAsync<BackendException>(() => controller.Index(Tenant));
        Assert.Equal((HttpStatusCode)status, error.Status);
    }
    private static (SubscriptionController Controller, HttpContext Context) Create(HttpStatusCode status, SubscriptionView? body, bool malformed = false)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService, FixtureAuthentication>().BuildServiceProvider() };
        var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("Bearer fixture-token", request.Headers.Authorization!.ToString());
            return request.RequestUri!.AbsolutePath.EndsWith("/subscription") ? new(status)
            { Content = malformed ? new StringContent("broken-json") : JsonContent.Create(body) }
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(new TenantView(Tenant, "Fixture tenant", "TranslationOffice", "Owner")) };
        })) { BaseAddress = new Uri("http://fixtures") };
        var controller = new SubscriptionController(new BackendApi(new Factory(client), new HttpContextAccessor { HttpContext = context }))
        { ControllerContext = new ControllerContext { HttpContext = context }, TempData = new TempDataDictionary(context, new FixtureTempData()) };
        return (controller, context);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class FixtureTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
    private sealed class FixtureAuthentication : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var properties = new AuthenticationProperties(); properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "fixture-token" }]);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("Fixture")), properties, "Fixture")));
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
