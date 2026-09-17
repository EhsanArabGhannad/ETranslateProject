using System.Net;
using System.Net.Http.Json;

namespace ETranslate.Documents.Api.Authorization;

public sealed class TranslationJobClient(HttpClient httpClient)
{
    public async Task<TranslationJobLookupResult> GetAsync(
        Guid tenantId,
        Guid translationJobId,
        string authorizationHeader,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}");
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return TranslationJobLookupResult.Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TranslationJobLookupResult.Unauthorized();
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return TranslationJobLookupResult.NotFound();
            }

            if (!response.IsSuccessStatusCode)
            {
                return TranslationJobLookupResult.Unavailable();
            }

            var job = await response.Content.ReadFromJsonAsync<TranslationJobDto>(cancellationToken);
            return job is null
                ? TranslationJobLookupResult.Unavailable()
                : TranslationJobLookupResult.Found(job);
        }
    }
}

public sealed record TranslationJobDto(Guid Id, Guid TenantId, string Status, string Title);

public sealed record TranslationJobLookupResult(
    TranslationJobLookupStatus Status,
    TranslationJobDto? Job)
{
    public static TranslationJobLookupResult Found(TranslationJobDto job) =>
        new(TranslationJobLookupStatus.Found, job);

    public static TranslationJobLookupResult NotFound() =>
        new(TranslationJobLookupStatus.NotFound, null);

    public static TranslationJobLookupResult Unauthorized() =>
        new(TranslationJobLookupStatus.Unauthorized, null);

    public static TranslationJobLookupResult Unavailable() =>
        new(TranslationJobLookupStatus.WorkflowServiceUnavailable, null);
}

public enum TranslationJobLookupStatus
{
    Found = 1,
    NotFound = 2,
    Unauthorized = 3,
    WorkflowServiceUnavailable = 4
}
