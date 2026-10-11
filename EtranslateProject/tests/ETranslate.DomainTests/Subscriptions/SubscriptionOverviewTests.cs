using System.Net;
using System.Net.Http.Json;
using ETranslate.Billing.Api.Authorization;
using ETranslate.Billing.Api.Subscriptions;
using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Subscriptions;

public sealed class SubscriptionOverviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(0, 30, "Trialing")] [InlineData(0.5, 30, "Trialing")] [InlineData(1, 29, "Trialing")]
    [InlineData(29.5, 1, "Trialing")] [InlineData(30, 0, "Expired")] [InlineData(31, 0, "Expired")]
    public void DaysAreRoundedUpAndExpireAtTheExactBoundary(double elapsed, int days, string status)
    {
        var tenant = Guid.NewGuid(); var subscription = Subscription.StartTrial(tenant, Start);
        var snapshot = SubscriptionSnapshot.Observe(tenant, subscription, Start.AddDays(elapsed));
        Assert.Equal(days, snapshot.RemainingTrialDays); Assert.Equal(status, snapshot.Status);
        Assert.Equal(Start.AddDays(30), snapshot.TrialEndsAtUtc);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status); // Reading does not persist expiration.
        Assert.Equal(Start, subscription.UpdatedAtUtc);
        Assert.True(View(snapshot).IsValidFor(tenant));
    }
    [Fact] public void LastTickStillHasOneRemainingDay()
    {
        var tenant = Guid.NewGuid(); var subscription = Subscription.StartTrial(tenant, Start);
        var snapshot = SubscriptionSnapshot.Observe(tenant, subscription, Start.AddDays(30).AddTicks(-1));
        Assert.Equal(1, snapshot.RemainingTrialDays); Assert.Equal("Trialing", snapshot.Status);
    }
    [Fact] public void MissingRowIsPendingNotExpiredOrFreeTrial()
    {
        var tenant = Guid.NewGuid(); var snapshot = SubscriptionSnapshot.Observe(tenant, null, Start);
        Assert.Equal("Pending", snapshot.Status); Assert.Null(snapshot.PlanCode); Assert.Null(snapshot.RemainingTrialDays);
        Assert.Null(snapshot.TrialStartedAtUtc); Assert.Null(snapshot.TrialEndsAtUtc); Assert.True(View(snapshot).IsValidFor(tenant));
    }
    [Fact] public void SnapshotCannotMixTenants()
    {
        Assert.Throws<ArgumentException>(() => SubscriptionSnapshot.Observe(Guid.NewGuid(), Subscription.StartTrial(Guid.NewGuid(), Start), Start));
        Assert.Throws<ArgumentException>(() => SubscriptionSnapshot.Observe(Guid.Empty, null, Start));
    }
    [Theory] [InlineData("Owner")] [InlineData("Administrator")] [InlineData("Translator")] [InlineData("Reviewer")]
    public void AllSupportedActiveMembershipRolesCanRead(string role)
    {
        var tenant = Guid.NewGuid(); var actor = new SubscriptionActor(tenant, Guid.NewGuid(), "TranslationOffice", role);
        Assert.True(actor.IsValid(tenant)); Assert.False(actor.IsValid(Guid.NewGuid()));
        Assert.False((actor with { UserId = Guid.Empty }).IsValid(tenant));
        Assert.False((actor with { Role = "PlatformAdmin" }).IsValid(tenant));
        Assert.False((actor with { TenantType = "Unknown" }).IsValid(tenant));
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("Bearer")] [InlineData("Basic secret")]
    public async Task InvalidAuthorizationNeverCallsIdentity(string? authorization)
    {
        using var http = Client(_ => throw new InvalidOperationException("Must not call Identity"));
        Assert.Equal(401, await new SubscriptionAccessClient(http).CheckAsync(Guid.NewGuid(), authorization, default));
    }
    [Theory] [InlineData(401, 401)] [InlineData(403, 403)] [InlineData(404, 403)] [InlineData(429, 503)] [InlineData(500, 503)]
    public async Task IdentityErrorsAreFailClosed(int upstream, int expected)
    {
        using var http = Client(_ => new((HttpStatusCode)upstream));
        Assert.Equal(expected, await new SubscriptionAccessClient(http).CheckAsync(Guid.NewGuid(), "Bearer fixture", default));
    }
    [Fact] public async Task ValidCheckForwardsBearerAndVerifiesIdentityScope()
    {
        var tenant = Guid.NewGuid(); var actor = new SubscriptionActor(tenant, Guid.NewGuid(), "IndependentTranslator", "Owner");
        var requestedTenant = tenant;
        using var http = Client(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal($"/api/v1/tenants/{requestedTenant}/access", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer fixture", request.Headers.Authorization!.ToString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(actor) };
        });
        Assert.Equal(200, await new SubscriptionAccessClient(http).CheckAsync(tenant, "Bearer fixture", default));
        requestedTenant = Guid.NewGuid();
        Assert.Equal(503, await new SubscriptionAccessClient(http).CheckAsync(requestedTenant, "Bearer fixture", default));
    }
    [Theory] [InlineData("null")] [InlineData("bad-json")] [InlineData("{}")]
    public async Task MalformedIdentityCannotAuthorize(string body)
    {
        using var http = Client(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) });
        Assert.Equal(503, await new SubscriptionAccessClient(http).CheckAsync(Guid.NewGuid(), "Bearer fixture", default));
    }
    [Fact] public async Task NetworkFailureIsUnavailableNotExpired()
    {
        using var http = Client(_ => throw new HttpRequestException());
        Assert.Equal(503, await new SubscriptionAccessClient(http).CheckAsync(Guid.NewGuid(), "Bearer fixture", default));
    }
    [Fact] public async Task UpstreamTimeoutIsUnavailable()
    {
        using var http = Client(_ => throw new TaskCanceledException());
        Assert.Equal(503, await new SubscriptionAccessClient(http).CheckAsync(Guid.NewGuid(), "Bearer fixture", default));
    }
    [Fact] public async Task CallerCancellationIsPropagated()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        using var http = Client(_ => throw new TaskCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SubscriptionAccessClient(http).CheckAsync(Guid.NewGuid(), "Bearer fixture", cts.Token));
    }
    [Fact] public void BffRejectsMismatchedOrMisleadingSnapshots()
    {
        var tenant = Guid.NewGuid(); var view = View(SubscriptionSnapshot.Observe(tenant, Subscription.StartTrial(tenant, Start), Start));
        Assert.True(view.IsValidFor(tenant)); Assert.False(view.IsValidFor(Guid.NewGuid()));
        Assert.False((view with { RemainingTrialDays = 31 }).IsValidFor(tenant));
        Assert.False((view with { TrialEndsAtUtc = Start.AddDays(31) }).IsValidFor(tenant));
        Assert.False((view with { Status = "Paid" }).IsValidFor(tenant));
        Assert.False((view with { ObservedAtUtc = Start.AddDays(30) }).IsValidFor(tenant));
        Assert.False((view with { Status = "Pending" }).IsValidFor(tenant));
    }
    [Fact] public void IstanbulDisplayIsIndependentOfHostTimezone()
        => Assert.Equal("2026-10-11 15:00:00", SubscriptionView.IstanbulTime(Start));
    private static SubscriptionView View(SubscriptionSnapshot s) => new(s.TenantId, s.PlanCode, s.Status, s.TrialStartedAtUtc, s.TrialEndsAtUtc, s.ObservedAtUtc, s.RemainingTrialDays);
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond)) { BaseAddress = new Uri("http://identity-access") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
}
