using ETranslate.Contracts.TranslationJobs;
using ETranslate.TranslationWorkflow.Api.Authorization;
using ETranslate.TranslationWorkflow.Api.Persistence;
using ETranslate.TranslationWorkflow.Api.TranslationJobs;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.TranslationWorkflow.Api.Endpoints;

public static class TranslationJobEndpoints
{
    public static IEndpointRouteBuilder MapTranslationJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/translation-jobs")
            .WithTags("Translation Jobs");

        group.MapPost("/", CreateTranslationJobAsync);
        group.MapGet("/", GetTranslationJobsAsync);
        group.MapGet("/{translationJobId:guid}", GetTranslationJobAsync);
        group.MapPut("/{translationJobId:guid}", UpdateTranslationJobAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTranslationJobAsync(
        Guid tenantId,
        CreateTranslationJobRequest request,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        TranslationWorkflowDbContext database,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await tenantAccessClient.CheckAccessAsync(
            tenantId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        var accessError = ToAccessError(access.Status);
        if (accessError is not null)
        {
            return accessError;
        }

        var actor = access.Actor!;
        if (!actor.CanManageTranslationJobs)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var now = timeProvider.GetUtcNow();
        TranslationJob job;

        try
        {
            job = TranslationJob.Create(
                tenantId,
                actor.UserId,
                actor.ProviderType,
                request.Title,
                request.SourceLanguageCode,
                request.TargetLanguageCode,
                request.NotaryRequirement,
                request.NotaryProcessingMode,
                request.AcceptanceProfile,
                request.AcceptanceProfileOther,
                now);
        }
        catch (TranslationJobValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }

        database.TranslationJobs.Add(job);
        await publishEndpoint.Publish(
            new TranslationJobCreatedV1(
                Guid.NewGuid(),
                job.Id,
                job.TenantId,
                job.CreatedByUserId,
                job.SignaturePolicy.ToString(),
                job.NotaryRequirement.ToString(),
                now),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/v1/tenants/{tenantId}/translation-jobs/{job.Id}",
            ToResponse(job));
    }

    private static async Task<IResult> GetTranslationJobsAsync(
        Guid tenantId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        TranslationWorkflowDbContext database,
        CancellationToken cancellationToken,
        int skip = 0,
        int take = 50)
    {
        var access = await tenantAccessClient.CheckAccessAsync(
            tenantId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        var accessError = ToAccessError(access.Status);
        if (accessError is not null)
        {
            return accessError;
        }

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var jobs = await database.TranslationJobs
            .AsNoTracking()
            .Where(job => job.TenantId == tenantId)
            .OrderByDescending(job => job.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(job => ToResponse(job))
            .ToListAsync(cancellationToken);

        return Results.Ok(jobs);
    }

    private static async Task<IResult> GetTranslationJobAsync(
        Guid tenantId,
        Guid translationJobId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        TranslationWorkflowDbContext database,
        CancellationToken cancellationToken)
    {
        var access = await tenantAccessClient.CheckAccessAsync(
            tenantId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        var accessError = ToAccessError(access.Status);
        if (accessError is not null)
        {
            return accessError;
        }

        var job = await database.TranslationJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == translationJobId && item.TenantId == tenantId,
                cancellationToken);

        return job is null ? Results.NotFound() : Results.Ok(ToResponse(job));
    }

    private static async Task<IResult> UpdateTranslationJobAsync(
        Guid tenantId,
        Guid translationJobId,
        UpdateTranslationJobRequest request,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        TranslationWorkflowDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var access = await tenantAccessClient.CheckAccessAsync(
            tenantId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        var accessError = ToAccessError(access.Status);
        if (accessError is not null)
        {
            return accessError;
        }

        if (!access.Actor!.CanManageTranslationJobs)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var job = await database.TranslationJobs.SingleOrDefaultAsync(
            item => item.Id == translationJobId && item.TenantId == tenantId,
            cancellationToken);
        if (job is null)
        {
            return Results.NotFound();
        }

        try
        {
            job.UpdateDraft(
                request.Title,
                request.SourceLanguageCode,
                request.TargetLanguageCode,
                request.NotaryRequirement,
                request.NotaryProcessingMode,
                request.AcceptanceProfile,
                request.AcceptanceProfileOther,
                timeProvider.GetUtcNow());
        }
        catch (TranslationJobValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = "translation_job_is_not_editable", detail = exception.Message });
        }

        await database.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(job));
    }

    private static IResult? ToAccessError(TenantAccessStatus status) => status switch
    {
        TenantAccessStatus.Authorized => null,
        TenantAccessStatus.Unauthorized => Results.Unauthorized(),
        TenantAccessStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        TenantAccessStatus.IdentityServiceUnavailable => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Identity service is unavailable."),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static TranslationJobResponse ToResponse(TranslationJob job) =>
        new(
            job.Id,
            job.TenantId,
            job.CreatedByUserId,
            job.ProviderType,
            job.SignaturePolicy,
            job.Status,
            job.Title,
            job.SourceLanguageCode,
            job.TargetLanguageCode,
            job.NotaryRequirement,
            job.NotaryProcessingMode,
            job.AcceptanceProfile,
            job.AcceptanceProfileOther,
            job.CreatedAtUtc,
            job.UpdatedAtUtc);
}

public sealed record CreateTranslationJobRequest(
    string Title,
    string SourceLanguageCode,
    string TargetLanguageCode,
    NotaryRequirement NotaryRequirement,
    NotaryProcessingMode? NotaryProcessingMode,
    AcceptanceProfile? AcceptanceProfile,
    string? AcceptanceProfileOther);

public sealed record UpdateTranslationJobRequest(
    string Title,
    string SourceLanguageCode,
    string TargetLanguageCode,
    NotaryRequirement NotaryRequirement,
    NotaryProcessingMode? NotaryProcessingMode,
    AcceptanceProfile? AcceptanceProfile,
    string? AcceptanceProfileOther);

public sealed record TranslationJobResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    TenantProviderType ProviderType,
    SignaturePolicy SignaturePolicy,
    TranslationJobStatus Status,
    string Title,
    string SourceLanguageCode,
    string TargetLanguageCode,
    NotaryRequirement NotaryRequirement,
    NotaryProcessingMode? NotaryProcessingMode,
    AcceptanceProfile? AcceptanceProfile,
    string? AcceptanceProfileOther,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
