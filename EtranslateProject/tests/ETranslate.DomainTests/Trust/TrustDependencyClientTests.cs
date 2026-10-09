using System.Net;
using System.Text;
using ETranslate.Trust.Api.Dependencies;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.DomainTests.Trust;

public sealed class TrustDependencyClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization?.ToString(); Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class ThrowHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromException<HttpResponseMessage>(exception);
    }
    [Theory] [InlineData("network")] [InlineData("timeout")] [InlineData("circuit")] [InlineData("cancel")]
    public async Task DependencyTransportFailures_AreUnavailable(string failure)
    {
        Exception exception = failure switch { "network" => new HttpRequestException(), "timeout" => new TimeoutRejectedException(), "circuit" => new BrokenCircuitException(), _ => new OperationCanceledException() };
        using var handler = new ThrowHandler(exception); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://dependency.test") };
        var result = await new TrustDependencyClient(new Factory(http)).GetAsync<TrustActor>("identity-access", "/test", "Bearer test", default);
        Assert.Equal(503, result.Status); Assert.Null(result.Value);
    }
    [Theory] [InlineData(401, 401)] [InlineData(403, 403)] [InlineData(404, 404)] [InlineData(409, 409)]
    [InlineData(500, 503)] [InlineData(429, 503)] [InlineData(503, 503)]
    public async Task DependencyFailure_NeverGrantsAuthorization(int code, int expected)
    {
        using var handler = new StubHandler((HttpStatusCode)code, "{}"); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://dependency.test") };
        var result = await new TrustDependencyClient(new Factory(http)).GetAsync<TrustActor>("identity-access", "/test", "Bearer test", default);
        Assert.Equal(expected, result.Status); Assert.Null(result.Value);
    }
    [Theory] [InlineData("null")] [InlineData("not-json")]
    public async Task MalformedResponse_IsUnavailable(string body)
    {
        using var handler = new StubHandler(HttpStatusCode.OK, body); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://dependency.test") };
        var result = await new TrustDependencyClient(new Factory(http)).GetAsync<TrustActor>("identity-access", "/test", "Bearer test", default);
        Assert.Equal(503, result.Status); Assert.Null(result.Value);
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("Basic test")]
    public async Task MissingBearer_IsUnauthorizedWithoutRequest(string? auth)
    {
        using var handler = new StubHandler(HttpStatusCode.OK, "{}"); using var http = new HttpClient(handler) { BaseAddress = new Uri("http://dependency.test") };
        var result = await new TrustDependencyClient(new Factory(http)).GetAsync<TrustActor>("identity-access", "/test", auth, default);
        Assert.Equal(401, result.Status); Assert.Null(handler.Path);
    }
    [Fact] public async Task ScopedRequest_ForwardsAuthorization_AndDeserializesActualActor()
    {
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var path = $"/api/v1/tenants/{tenant}/access";
        using var handler = new StubHandler(HttpStatusCode.OK, $"{{\"tenantId\":\"{tenant}\",\"userId\":\"{user}\",\"tenantType\":\"IndependentTranslator\",\"role\":\"Owner\"}}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://dependency.test") };
        var result = await new TrustDependencyClient(new Factory(http)).GetAsync<TrustActor>("identity-access", path, "Bearer test", default);
        Assert.Equal(200, result.Status); Assert.True(result.Value!.IsValid(tenant)); Assert.Equal("TranslatorOnly", result.Value.SignaturePolicy);
        Assert.Equal(user, result.Value.UserId); Assert.Equal(path, handler.Path); Assert.Equal("Bearer test", handler.Authorization);
    }
}
