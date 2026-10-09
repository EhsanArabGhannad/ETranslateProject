using System.Net;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ETranslate.Trust.Api.Dependencies;

public sealed class TrustDependencyClient(IHttpClientFactory clients)
{
    public async Task<DependencyResult<T>> GetAsync<T>(string service, string path, string? authorization, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return new(401, default);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        try
        {
            using var response = await clients.CreateClient(service).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                    ? (int)response.StatusCode : 503, default);
            var value = await response.Content.ReadFromJsonAsync<T>(ct);
            return value is null ? new(503, default) : new(200, value);
        }
        catch (HttpRequestException) { return new(503, default); }
        catch (TimeoutRejectedException) { return new(503, default); }
        catch (BrokenCircuitException) { return new(503, default); }
        catch (JsonException) { return new(503, default); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(503, default); }
    }
}

public sealed record DependencyResult<T>(int Status, T? Value);
public sealed record TrustActor(Guid TenantId, Guid UserId, string TenantType, string Role)
{
    public bool CanManage => Role is "Owner" or "Administrator";
    public bool IsValid(Guid tenantId) => TenantId == tenantId && UserId != Guid.Empty &&
        (TenantType is "IndependentTranslator" or "TranslationOffice") && (Role is "Owner" or "Administrator" or "Translator" or "Reviewer");
    public string SignaturePolicy => TenantType == "IndependentTranslator" ? "TranslatorOnly" : "TranslatorAndTranslationOffice";
}
public sealed record TrustJob(Guid Id, Guid TenantId, string SignaturePolicy);
public sealed record EligibleSigner(Guid UserId, string Role);
public sealed record SourceSnapshot(Guid Id, long SizeBytes, string Sha256);
public sealed record DocumentSigningSnapshot(Guid TenantId, Guid TranslationJobId, Guid DocumentId, string ReviewStatus, ApprovedArtifactSnapshot? Artifact)
{
    public bool IsScoped(Guid tenantId, Guid jobId, Guid documentId) => TenantId == tenantId && TranslationJobId == jobId && DocumentId == documentId;
}
public sealed record ApprovedArtifactSnapshot(Guid ReviewId, int ReviewRound, Guid PdfVersionId, Guid DraftRevisionId,
    int RevisionNumber, Guid? TemplateRevisionId, string Sha256, long SizeBytes, string Kind,
    Guid ApprovedByUserId, DateTimeOffset ApprovedAtUtc, IReadOnlyList<SourceSnapshot> SourceFiles);
