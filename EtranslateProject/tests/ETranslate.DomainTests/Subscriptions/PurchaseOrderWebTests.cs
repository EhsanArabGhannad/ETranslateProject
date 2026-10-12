using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ETranslate.Billing.Api.Endpoints;
using ETranslate.Web.Controllers;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class PurchaseOrderWebTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Order = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static PurchaseOrderView Draft() => new(Order, Tenant, "office-monthly", "Office", 1, null, "TRY", "Purchase", "Draft", Now, Now.AddMinutes(30), null, 0, false, false, Now);

    [Fact] public async Task Manager_page_is_private_readonly_and_has_no_payment_claim()
    {
        var (controller, context) = Create("Owner", Draft());
        var view = Assert.IsType<ViewResult>(await controller.Index(Tenant));
        var page = Assert.IsType<PurchaseOrdersPageView>(view.Model);
        Assert.Single(page.Orders); Assert.False(page.Catalog.PurchaseEnabled); Assert.False(page.Catalog.PricingConfigured);
        Assert.Equal("TRY", page.Catalog.Currency); Assert.NotEqual(Guid.Empty, page.RequestId);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
    }

    [Theory] [InlineData("Translator")] [InlineData("Reviewer")]
    public async Task Nonmanagers_never_read_financial_data(string role)
    {
        var (controller, _) = Create(role, Draft(), rejectBilling: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<BackendException>(() => controller.Index(Tenant))).Status);
    }

    [Theory] [InlineData("wrong-tenant")] [InlineData("paid")] [InlineData("checkout")]
    public async Task Invalid_upstream_order_data_cannot_be_displayed(string mutation)
    {
        var draft = mutation switch { "wrong-tenant" => Draft() with { TenantId = Guid.NewGuid() }, "paid" => Draft() with { PaymentReceived = true }, _ => Draft() with { CheckoutEnabled = true } };
        var (controller, _) = Create("Owner", draft);
        Assert.Equal(HttpStatusCode.BadGateway, (await Assert.ThrowsAsync<BackendException>(() => controller.Index(Tenant))).Status);
    }

    [Fact] public async Task Create_sends_only_plan_and_nonce_with_server_bearer()
    {
        var nonce = Guid.NewGuid(); var seen = false;
        var (controller, _) = Create("Administrator", Draft(), post: request =>
        {
            seen = true; var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var data = JsonDocument.Parse(json); Assert.Equal(2, data.RootElement.EnumerateObject().Count());
            Assert.Equal(nonce, data.RootElement.GetProperty("requestId").GetGuid());
            Assert.Equal("office-monthly", data.RootElement.GetProperty("planCode").GetString());
        });
        Assert.IsType<RedirectToActionResult>(await controller.Create(Tenant, "office-monthly", nonce)); Assert.True(seen);
        Assert.Contains("هیچ پرداختی", (string)controller.TempData["Notice"]!);
    }

    [Fact] public async Task Cancellation_checks_returned_scope_and_terminal_state()
    {
        var (controller, _) = Create("Owner", Draft());
        Assert.Equal(HttpStatusCode.BadGateway, (await Assert.ThrowsAsync<BackendException>(() => controller.Cancel(Tenant, Order, 0))).Status);
    }

    [Theory] [InlineData("amountTry", "0")] [InlineData("status", "\"Paid\"")] [InlineData("currency", "\"USD\"")]
    public void Payment_fields_are_rejected_by_the_API_request_contract(string property, string value)
    {
        var json = $"{{\"PlanCode\":\"office-monthly\",\"RequestId\":\"{Guid.NewGuid()}\",\"{property}\":{value}}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreatePurchaseOrderRequest>(json));
    }

    private static (PurchaseOrdersController, HttpContext) Create(string role, PurchaseOrderView order, bool rejectBilling = false, Action<HttpRequestMessage>? post = null)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService, Auth>()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>().BuildServiceProvider() };
        var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("fixture", request.Headers.Authorization?.Parameter);
            object response;
            if (request.RequestUri!.AbsolutePath == $"/api/v1/tenants/{Tenant}") response = new TenantView(Tenant, "Office", "TranslationOffice", role);
            else
            {
                Assert.False(rejectBilling);
                if (request.Method == HttpMethod.Post) { post?.Invoke(request); response = order; }
                else if (request.RequestUri.AbsolutePath.EndsWith("/plans")) response = new PurchasePlanCatalogView(Tenant, "TranslationOffice", true, "TRY", "UTC", false, false, [new("office-monthly", "Office", "TranslationOffice", 1, true, null)]);
                else response = new[] { order };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response, response.GetType()) };
        })) { BaseAddress = new("http://fixture.invalid") };
        return (new PurchaseOrdersController(new BackendApi(new Factory(http), new HttpContextAccessor { HttpContext = context }))
        { ControllerContext = new() { HttpContext = context, RouteData = new Microsoft.AspNetCore.Routing.RouteData(), ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor() }, TempData = new TempDataDictionary(context, new Temp()) }, context);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request)); }
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
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("Fixture")), properties, "Fixture")));
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
