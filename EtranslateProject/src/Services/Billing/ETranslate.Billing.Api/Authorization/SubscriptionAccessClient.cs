using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.Billing.Api.Authorization;

public sealed class SubscriptionAccessClient(HttpClient http)
{
    public async Task<int> CheckAsync(Guid tenantId, string? authorization, CancellationToken ct)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter)) return 401;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/access");
        request.Headers.Authorization = header;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => 401,
                HttpStatusCode.Forbidden or HttpStatusCode.NotFound => 403,
                _ => 503
            };
            var actor = await response.Content.ReadFromJsonAsync<SubscriptionActor>(ct);
            return actor?.IsValid(tenantId) == true ? 200 : 503;
        }
        catch (HttpRequestException) { return 503; }
        catch (JsonException) { return 503; }
        catch (TimeoutRejectedException) { return 503; }
        catch (BrokenCircuitException) { return 503; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return 503; }
    }
}

public sealed record SubscriptionActor(Guid TenantId, Guid UserId, string TenantType, string Role)
{
    public bool IsValid(Guid tenantId) => tenantId != Guid.Empty && TenantId == tenantId && UserId != Guid.Empty &&
        TenantType is "IndependentTranslator" or "TranslationOffice" && Role is "Owner" or "Administrator" or "Translator" or "Reviewer";
}
