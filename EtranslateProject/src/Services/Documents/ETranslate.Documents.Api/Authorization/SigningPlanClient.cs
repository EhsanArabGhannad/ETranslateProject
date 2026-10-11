using System.Net;
using System.Text.Json;
using ETranslate.Contracts.Trust;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.Documents.Api.Authorization;

public sealed class SigningPlanClient(HttpClient http)
{
    public async Task<SigningPlanResult> ReadAsync(Guid tenant, Guid job, Guid document, Guid preparation, string? authorization, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return new(401, null);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/tenants/{tenant}/translation-jobs/{job}/documents/{document}/signing-preparations/{preparation}/preview-snapshot");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? (int)response.StatusCode : 503, null);
            var value = await response.Content.ReadFromJsonAsync<SigningPreviewSnapshotV1>(ct);
            return value is null ? new(503, null) : new(200, value);
        }
        catch (HttpRequestException) { return new(503, null); }
        catch (JsonException) { return new(503, null); }
        catch (TimeoutRejectedException) { return new(503, null); }
        catch (BrokenCircuitException) { return new(503, null); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(503, null); }
    }
}
public sealed record SigningPlanResult(int Status, SigningPreviewSnapshotV1? Value);
