using System.Net;
using System.Text;
using ETranslate.TranslationWorkflow.Api.Authorization;

namespace ETranslate.DomainTests.TranslationJobs;

public sealed class AssigneeAccessClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString(); Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TenantAccessStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, TenantAccessStatus.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TenantAccessStatus.IdentityServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, TenantAccessStatus.IdentityServiceUnavailable)]
    public async Task UpstreamFailureDoesNotGrantEligibility(HttpStatusCode code, TenantAccessStatus expected)
    {
        using var handler = new StubHandler(code, "{}"); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity.test") };
        var result = await new TenantAccessClient(http).GetAssigneesAsync(Guid.NewGuid(), Guid.NewGuid(), "Bearer test-token", default);
        Assert.Equal(expected, result.Status); Assert.Empty(result.Members);
    }
    [Fact] public async Task MissingTargetIsIneligibleButMissingListEndpointIsUnavailable()
    {
        using var handler = new StubHandler(HttpStatusCode.NotFound, "{}"); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity.test") };
        var client = new TenantAccessClient(http);
        var target = await client.GetAssigneesAsync(Guid.NewGuid(), Guid.NewGuid(), "Bearer test-token", default);
        Assert.Equal(TenantAccessStatus.Authorized, target.Status); Assert.Empty(target.Members);
        var list = await client.GetAssigneesAsync(Guid.NewGuid(), null, "Bearer test-token", default);
        Assert.Equal(TenantAccessStatus.IdentityServiceUnavailable, list.Status); Assert.Empty(list.Members);
    }
    [Theory] [InlineData("not-json")] [InlineData("null")]
    public async Task MalformedOrNullResponsesAreUnavailable(string body)
    {
        using var handler = new StubHandler(HttpStatusCode.OK, body); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity.test") };
        var client = new TenantAccessClient(http);
        foreach (var user in new Guid?[] { null, Guid.NewGuid() })
        {
            var result = await client.GetAssigneesAsync(Guid.NewGuid(), user, "Bearer test-token", default);
            Assert.Equal(TenantAccessStatus.IdentityServiceUnavailable, result.Status); Assert.Empty(result.Members);
        }
    }
    [Fact] public async Task CandidateRequestForwardsManagerAuthorizationAndTenantScope()
    {
        var tenantId = Guid.NewGuid(); var userId = Guid.NewGuid();
        using var handler = new StubHandler(HttpStatusCode.OK, $"[{{\"userId\":\"{userId}\",\"email\":\"translator@test.local\",\"role\":\"Translator\"}}]");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity.test") };
        var result = await new TenantAccessClient(http).GetAssigneesAsync(tenantId, null, "Bearer test-token", default);
        Assert.Equal(TenantAccessStatus.Authorized, result.Status); Assert.Equal(userId, Assert.Single(result.Members).UserId);
        Assert.Equal("Bearer test-token", handler.Authorization); Assert.Equal($"/api/v1/tenants/{tenantId}/translation-assignees", handler.Path);
    }
}
