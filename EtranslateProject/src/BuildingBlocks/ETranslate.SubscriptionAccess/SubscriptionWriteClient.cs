using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ETranslate.Contracts.Billing;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.SubscriptionAccess;

public enum SubscriptionWriteStatus { Allowed, Required, Pending, Unavailable, Unauthorized, Forbidden }

public sealed class SubscriptionWriteClient(HttpClient http, TimeProvider clock)
{
    public async Task<SubscriptionWriteStatus> CheckAsync(Guid tenantId, string? authorization, CancellationToken ct)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
            return SubscriptionWriteStatus.Unauthorized;
        if (tenantId == Guid.Empty) return SubscriptionWriteStatus.Unavailable;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/subscription/write-access");
        request.Headers.Authorization = header;
        request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return SubscriptionWriteStatus.Unauthorized;
            if (response.StatusCode == HttpStatusCode.Forbidden) return SubscriptionWriteStatus.Forbidden;
            if (response.StatusCode != HttpStatusCode.OK) return SubscriptionWriteStatus.Unavailable;
            var decision = await response.Content.ReadFromJsonAsync<SubscriptionWriteAccessV1>(ct);
            return Validate(tenantId, decision, clock.GetUtcNow());
        }
        catch (HttpRequestException) { return SubscriptionWriteStatus.Unavailable; }
        catch (JsonException) { return SubscriptionWriteStatus.Unavailable; }
        catch (NotSupportedException) { return SubscriptionWriteStatus.Unavailable; }
        catch (TimeoutRejectedException) { return SubscriptionWriteStatus.Unavailable; }
        catch (BrokenCircuitException) { return SubscriptionWriteStatus.Unavailable; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return SubscriptionWriteStatus.Unavailable; }
    }

    public static SubscriptionWriteStatus Validate(Guid tenantId, SubscriptionWriteAccessV1? decision, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || decision is null || decision.TenantId != tenantId || decision.ObservedAtUtc == default ||
            now - decision.ObservedAtUtc > TimeSpan.FromSeconds(30) || decision.ObservedAtUtc - now > TimeSpan.FromSeconds(5))
            return SubscriptionWriteStatus.Unavailable;
        if (decision.CanWrite)
        {
            if (decision.Reason is not null) return SubscriptionWriteStatus.Unavailable;
            if (decision.Status == "Active") return SubscriptionWriteStatus.Allowed;
            if (decision.Status != "Trialing" || decision.TrialEndsAtUtc is not { } end || end <= decision.ObservedAtUtc)
                return SubscriptionWriteStatus.Unavailable;
            return end > now ? SubscriptionWriteStatus.Allowed : SubscriptionWriteStatus.Required;
        }
        if (decision.Status == "Pending" && decision.Reason == "subscription_pending" && decision.TrialEndsAtUtc is null)
            return SubscriptionWriteStatus.Pending;
        if (decision.Status is "Expired" or "PastDue" or "Canceled" && decision.Reason == "subscription_required")
        {
            if (decision.Status == "Expired" && (decision.TrialEndsAtUtc is null || decision.TrialEndsAtUtc > decision.ObservedAtUtc))
                return SubscriptionWriteStatus.Unavailable;
            return SubscriptionWriteStatus.Required;
        }
        return SubscriptionWriteStatus.Unavailable;
    }
}
