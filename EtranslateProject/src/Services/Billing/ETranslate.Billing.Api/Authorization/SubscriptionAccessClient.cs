using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.Billing.Api.Authorization;

public sealed class SubscriptionAccessClient(HttpClient http)
{
    public async Task<int> CheckAsync(Guid tenantId, string? authorization, CancellationToken ct)
        => (await GetActorAsync(tenantId, authorization, ct)).Status;

    public async Task<SubscriptionActorResult> GetActorAsync(Guid tenantId, string? authorization, CancellationToken ct)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter)) return new(401, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/access");
        request.Headers.Authorization = header;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => 401,
                HttpStatusCode.Forbidden or HttpStatusCode.NotFound => 403,
                _ => 503
            }, null);
            var actor = await response.Content.ReadFromJsonAsync<SubscriptionActor>(ct);
            return actor?.IsValid(tenantId) == true ? new(200, actor) : new(503, null);
        }
        catch (HttpRequestException) { return new(503, null); }
        catch (JsonException) { return new(503, null); }
        catch (NotSupportedException) { return new(503, null); }
        catch (TimeoutRejectedException) { return new(503, null); }
        catch (BrokenCircuitException) { return new(503, null); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(503, null); }
    }
}

public sealed record SubscriptionActorResult(int Status, SubscriptionActor? Actor);

public sealed record SubscriptionActor(Guid TenantId, Guid UserId, string TenantType, string Role)
{
    public bool IsValid(Guid tenantId) => tenantId != Guid.Empty && TenantId == tenantId && UserId != Guid.Empty &&
        TenantType is "IndependentTranslator" or "TranslationOffice" && Role is "Owner" or "Administrator" or "Translator" or "Reviewer";
}
