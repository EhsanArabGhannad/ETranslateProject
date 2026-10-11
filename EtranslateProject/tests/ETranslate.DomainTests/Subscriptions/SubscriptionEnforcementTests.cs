using System.Net;
using System.Net.Http.Json;
using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Contracts.Billing;
using ETranslate.SubscriptionAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class SubscriptionEnforcementTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    private static SubscriptionWriteAccessV1 Grant => new(Tenant, "Trialing", true, null, Now, Now.AddDays(1));

    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public void Trial_is_checked_at_exact_boundary_without_mutating_state(int ticks, bool allowed)
    {
        var subscription = Subscription.StartTrial(Tenant, Now.AddDays(-30));
        var result = SubscriptionWritePolicy.Observe(Tenant, subscription, Now.AddTicks(ticks));
        Assert.Equal(allowed, result.CanWrite);
        Assert.Equal(allowed ? null : "subscription_required", result.Reason);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Now.AddDays(-30), subscription.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active, true)] [InlineData(SubscriptionStatus.PastDue, false)]
    [InlineData(SubscriptionStatus.Canceled, false)] [InlineData(SubscriptionStatus.Expired, false)]
    public void Only_active_or_valid_trial_can_write(SubscriptionStatus status, bool allowed)
    {
        var subscription = Subscription.StartTrial(Tenant, Now.AddDays(-31));
        typeof(Subscription).GetProperty(nameof(Subscription.Status))!.SetValue(subscription, status);
        Assert.Equal(allowed, SubscriptionWritePolicy.Observe(Tenant, subscription, Now).CanWrite);
    }

    [Fact] public void Missing_subscription_is_pending_not_a_trial()
    {
        var decision = SubscriptionWritePolicy.Observe(Tenant, null, Now);
        Assert.False(decision.CanWrite); Assert.Equal("subscription_pending", decision.Reason);
        Assert.Equal(SubscriptionWriteStatus.Pending, SubscriptionWriteClient.Validate(Tenant, decision, Now));
    }

    [Theory]
    [InlineData(0, SubscriptionWriteStatus.Allowed)] [InlineData(30, SubscriptionWriteStatus.Allowed)]
    [InlineData(31, SubscriptionWriteStatus.Unavailable)] [InlineData(-5, SubscriptionWriteStatus.Allowed)]
    [InlineData(-6, SubscriptionWriteStatus.Unavailable)]
    public void Observations_must_be_fresh_with_bounded_clock_skew(int seconds, SubscriptionWriteStatus expected)
        => Assert.Equal(expected, SubscriptionWriteClient.Validate(Tenant, Grant with { ObservedAtUtc = Now.AddSeconds(-seconds) }, Now));

    [Fact] public void A_trial_that_expires_in_transit_is_denied()
        => Assert.Equal(SubscriptionWriteStatus.Required,
            SubscriptionWriteClient.Validate(Tenant, Grant with { ObservedAtUtc = Now.AddSeconds(-1), TrialEndsAtUtc = Now }, Now));

    [Fact] public void Incoherent_or_wrong_scope_responses_fail_closed()
    {
        SubscriptionWriteAccessV1?[] bad = [null, Grant with { TenantId = Guid.NewGuid() }, Grant with { ObservedAtUtc = default },
            Grant with { Status = "Unknown" }, Grant with { TrialEndsAtUtc = null }, Grant with { TrialEndsAtUtc = Now },
            Grant with { Reason = "subscription_required" }, Grant with { CanWrite = false },
            Grant with { CanWrite = false, Status = "Active", Reason = "subscription_required" },
            Grant with { CanWrite = false, Status = "Expired", Reason = "subscription_required" },
            Grant with { CanWrite = false, Status = "Pending", Reason = "subscription_pending" }];
        foreach (var value in bad) Assert.Equal(SubscriptionWriteStatus.Unavailable, SubscriptionWriteClient.Validate(Tenant, value, Now));
        Assert.Equal(SubscriptionWriteStatus.Unavailable, SubscriptionWriteClient.Validate(Guid.Empty, Grant, Now));
    }

    [Theory]
    [InlineData("Expired")] [InlineData("PastDue")] [InlineData("Canceled")]
    public void All_nonvalid_statuses_are_denied(string status)
        => Assert.Equal(SubscriptionWriteStatus.Required, SubscriptionWriteClient.Validate(Tenant,
            new(Tenant, status, false, "subscription_required", Now, Now.AddDays(-1)), Now));

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("Basic ignored")] [InlineData("Bearer")]
    public async Task Missing_bearer_never_contacts_billing(string? auth)
    {
        using var http = Client((_, _) => throw new Exception("Must not send"));
        Assert.Equal(SubscriptionWriteStatus.Unauthorized, await new SubscriptionWriteClient(http, new Clock()).CheckAsync(Tenant, auth, default));
    }

    [Theory]
    [InlineData(401, SubscriptionWriteStatus.Unauthorized)] [InlineData(403, SubscriptionWriteStatus.Forbidden)]
    [InlineData(404, SubscriptionWriteStatus.Unavailable)] [InlineData(202, SubscriptionWriteStatus.Unavailable)]
    [InlineData(429, SubscriptionWriteStatus.Unavailable)] [InlineData(500, SubscriptionWriteStatus.Unavailable)]
    [InlineData(503, SubscriptionWriteStatus.Unavailable)]
    public async Task Dependency_errors_never_become_grants(int code, SubscriptionWriteStatus expected)
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)));
        Assert.Equal(expected, await new SubscriptionWriteClient(http, new Clock()).CheckAsync(Tenant, "Bearer synthetic", default));
    }

    [Theory] [InlineData("null")] [InlineData("{}")] [InlineData("not-json")]
    public async Task Invalid_JSON_fails_closed(string json)
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }));
        Assert.Equal(SubscriptionWriteStatus.Unavailable, await new SubscriptionWriteClient(http, new Clock()).CheckAsync(Tenant, "Bearer synthetic", default));
    }

    [Fact] public async Task Every_write_sends_a_new_scoped_authenticated_GET()
    {
        var calls = 0;
        using var http = Client((request, _) =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/tenants/{Tenant}/subscription/write-access", request.RequestUri!.AbsolutePath);
            Assert.Equal("synthetic", request.Headers.Authorization!.Parameter);
            Assert.True(request.Headers.CacheControl!.NoStore);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Grant) });
        });
        var access = new SubscriptionWriteClient(http, new Clock());
        Assert.Equal(SubscriptionWriteStatus.Allowed, await access.CheckAsync(Tenant, "Bearer synthetic", default));
        Assert.Equal(SubscriptionWriteStatus.Allowed, await access.CheckAsync(Tenant, "Bearer synthetic", default));
        Assert.Equal(2, calls);
    }

    [Fact] public async Task Transport_failures_are_unknown_and_caller_cancellation_propagates()
    {
        Exception[] errors = [new HttpRequestException(), new TaskCanceledException(), new TimeoutRejectedException(), new BrokenCircuitException()];
        foreach (var error in errors)
        {
            using var http = Client((_, _) => Task.FromException<HttpResponseMessage>(error));
            Assert.Equal(SubscriptionWriteStatus.Unavailable, await new SubscriptionWriteClient(http, new Clock()).CheckAsync(Tenant, "Bearer synthetic", default));
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var client = Client((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SubscriptionWriteClient(client, new Clock()).CheckAsync(Tenant, "Bearer synthetic", canceled.Token));
    }

    [Theory] [InlineData("GET")] [InlineData("HEAD")] [InlineData("OPTIONS")]
    public async Task Reads_skip_billing_entirely(string method)
    {
        using var http = Client((_, _) => throw new Exception("Reads must never depend on Billing"));
        var context = new DefaultHttpContext(); context.Request.Method = method;
        var filter = new SubscriptionWriteFilter(new(http, new Clock()));
        Assert.Equal("readable", await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context), _ => ValueTask.FromResult<object?>("readable")));
    }

    [Theory] [InlineData("POST")] [InlineData("PUT")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Denied_mutations_do_not_execute_the_business_handler(string method)
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(new SubscriptionWriteAccessV1(Tenant, "Expired", false, "subscription_required", Now, Now.AddDays(-1))) }));
        var context = new DefaultHttpContext(); context.Request.Method = method;
        context.Request.RouteValues["tenantId"] = Tenant; context.Request.Headers.Authorization = "Bearer synthetic";
        var called = false;
        var filter = new SubscriptionWriteFilter(new(http, new Clock()));
        var result = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context), _ => { called = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(called); Assert.Equal(402, ((IStatusCodeHttpResult)result!).StatusCode);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
    }

    [Fact] public async Task Missing_route_scope_never_uses_a_spoofed_header()
    {
        using var http = Client((_, _) => throw new Exception("Must not send"));
        var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Headers["X-Tenant-Id"] = Tenant.ToString();
        var filter = new SubscriptionWriteFilter(new(http, new Clock()));
        var result = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(context), _ => throw new Exception("Must not mutate"));
        Assert.Equal(500, ((IStatusCodeHttpResult)result!).StatusCode);
    }

    [Theory]
    [InlineData(200, "pending", 503)] [InlineData(200, "malformed", 503)]
    [InlineData(503, "unavailable", 503)] [InlineData(401, "unauthorized", 401)] [InlineData(403, "forbidden", 403)]
    public async Task No_failed_check_can_execute_a_mutation(int upstreamStatus, string body, int status)
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)upstreamStatus)
        {
            Content = body == "pending" ? JsonContent.Create(new SubscriptionWriteAccessV1(Tenant, "Pending", false, "subscription_pending", Now, null))
                : new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        }));
        var context = new DefaultHttpContext(); context.Request.Method = "POST";
        context.Request.RouteValues["tenantId"] = Tenant; context.Request.Headers.Authorization = "Bearer synthetic";
        var result = await new SubscriptionWriteFilter(new(http, new Clock())).InvokeAsync(EndpointFilterInvocationContext.Create(context), _ => throw new Exception("Must not mutate"));
        Assert.Equal(status, ((IStatusCodeHttpResult)result!).StatusCode);
    }

    [Fact] public async Task A_grant_does_not_override_the_handlers_role_policy()
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Grant) }));
        var context = new DefaultHttpContext(); context.Request.Method = "POST";
        context.Request.RouteValues["tenantId"] = Tenant; context.Request.Headers.Authorization = "Bearer synthetic";
        var result = await new SubscriptionWriteFilter(new(http, new Clock())).InvokeAsync(EndpointFilterInvocationContext.Create(context), _ => ValueTask.FromResult<object?>(Results.StatusCode(403)));
        Assert.Equal(403, ((IStatusCodeHttpResult)result!).StatusCode);
    }

    [Theory]
    [InlineData(SubscriptionWriteStatus.Required, 402, "subscription_required")]
    [InlineData(SubscriptionWriteStatus.Pending, 503, "subscription_pending")]
    [InlineData(SubscriptionWriteStatus.Unavailable, 503, "subscription_unavailable")]
    public void Denials_expose_safe_distinct_problem_codes(SubscriptionWriteStatus status, int code, string reason)
    {
        var result = SubscriptionWriteFilter.Error(status);
        Assert.Equal(code, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(reason, ((Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult)result).ProblemDetails.Extensions["code"]);
    }

    [Fact] public void Client_has_a_short_bounded_budget()
    {
        var services = new ServiceCollection().AddSubscriptionWriteAccess().BuildServiceProvider();
        using (services) Assert.Equal(TimeSpan.FromSeconds(4), services.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(SubscriptionWriteClient).Name).Timeout);
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) => new(new Handler(action)) { BaseAddress = new("http://fixture.invalid") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
