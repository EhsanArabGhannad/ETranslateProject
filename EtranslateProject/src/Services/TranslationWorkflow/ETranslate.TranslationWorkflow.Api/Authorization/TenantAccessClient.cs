using System.Net;
using System.Net.Http.Json;
using ETranslate.TranslationWorkflow.Api.TranslationJobs;

namespace ETranslate.TranslationWorkflow.Api.Authorization;

public sealed class TenantAccessClient(HttpClient httpClient)
{
    public async Task<TenantAccessResult> CheckAccessAsync(
        Guid tenantId,
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return TenantAccessResult.Unauthorized();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenants/{tenantId}/access");
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return TenantAccessResult.Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TenantAccessResult.Unauthorized();
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return TenantAccessResult.Forbidden();
            }

            if (!response.IsSuccessStatusCode)
            {
                return TenantAccessResult.Unavailable();
            }

            var access = await response.Content.ReadFromJsonAsync<TenantAccessDto>(cancellationToken);
            if (access is null ||
                !Enum.TryParse<TenantProviderType>(access.TenantType, out var providerType))
            {
                return TenantAccessResult.Unavailable();
            }

            return TenantAccessResult.Authorized(new TenantActor(
                access.TenantId,
                access.UserId,
                providerType,
                access.Role));
        }
    }

    private sealed record TenantAccessDto(
        Guid TenantId,
        Guid UserId,
        string TenantType,
        string Role);
}

public sealed record TenantActor(
    Guid TenantId,
    Guid UserId,
    TenantProviderType ProviderType,
    string Role)
{
    public bool CanManageTranslationJobs =>
        Role.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
        Role.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
        Role.Equals("Translator", StringComparison.OrdinalIgnoreCase);
}

public sealed record TenantAccessResult(
    TenantAccessStatus Status,
    TenantActor? Actor)
{
    public static TenantAccessResult Authorized(TenantActor actor) =>
        new(TenantAccessStatus.Authorized, actor);

    public static TenantAccessResult Unauthorized() =>
        new(TenantAccessStatus.Unauthorized, null);

    public static TenantAccessResult Forbidden() =>
        new(TenantAccessStatus.Forbidden, null);

    public static TenantAccessResult Unavailable() =>
        new(TenantAccessStatus.IdentityServiceUnavailable, null);
}

public enum TenantAccessStatus
{
    Authorized = 1,
    Unauthorized = 2,
    Forbidden = 3,
    IdentityServiceUnavailable = 4
}
