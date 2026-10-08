using ETranslate.Contracts.TranslationJobs;
using ETranslate.TranslationWorkflow.Api.Authorization;
using ETranslate.TranslationWorkflow.Api.Persistence;
using ETranslate.TranslationWorkflow.Api.TranslationJobs;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.TranslationWorkflow.Api.Endpoints;

public static class TranslationAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapTranslationAssignmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/translation-jobs").WithTags("Translation Assignments");
        group.MapGet("/{translationJobId:guid}/assignment", GetAsync);
        group.MapPut("/{translationJobId:guid}/assignment", AssignAsync);
        endpoints.MapGet("/api/v1/tenants/{tenantId:guid}/translation-assignees", CandidatesAsync).WithTags("Translation Assignments");
        return endpoints;
    }

    private static async Task<IResult> CandidatesAsync(Guid tenantId, HttpContext context, TenantAccessClient identity, CancellationToken ct)
    {
        var access = await identity.CheckAccessAsync(tenantId, context.Request.Headers.Authorization.ToString(), ct);
        var error = AccessError(access.Status); if (error is not null) return error;
        if (!access.Actor!.CanAssignTranslators) return Results.StatusCode(403);
        var result = await identity.GetAssigneesAsync(tenantId, null, context.Request.Headers.Authorization.ToString(), ct);
        return AccessError(result.Status) ?? Results.Ok(result.Members);
    }

    private static async Task<IResult> GetAsync(Guid tenantId, Guid translationJobId, HttpContext context,
        TenantAccessClient identity, TranslationWorkflowDbContext db, CancellationToken ct)
    {
        var access = await identity.CheckAccessAsync(tenantId, context.Request.Headers.Authorization.ToString(), ct);
        var error = AccessError(access.Status); if (error is not null) return error;
        var job = await db.TranslationJobs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == translationJobId && item.TenantId == tenantId, ct);
        if (job is null) return Results.NotFound();
        var history = await db.AssignmentChanges.AsNoTracking().Where(item => item.TranslationJobId == translationJobId && item.TenantId == tenantId)
            .OrderByDescending(item => item.Version).Take(50).Select(item => new AssignmentChangeResponse(item.Id, item.Version, item.ActorUserId,
                item.PreviousTranslatorUserId, item.TranslatorUserId, item.Note, item.OccurredAtUtc)).ToListAsync(ct);
        return Results.Ok(new AssignmentStateResponse(job.Id, job.AssignedTranslatorUserId, job.AssignmentVersion,
            job.AssignmentChangedByUserId, job.AssignmentChangedAtUtc, access.Actor!.UserId, access.Actor.CanAssignTranslators, history));
    }

    private static async Task<IResult> AssignAsync(Guid tenantId, Guid translationJobId, AssignTranslatorRequest request,
        HttpContext context, TenantAccessClient identity, TranslationWorkflowDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var access = await identity.CheckAccessAsync(tenantId, context.Request.Headers.Authorization.ToString(), ct);
        var error = AccessError(access.Status); if (error is not null) return error;
        if (!access.Actor!.CanAssignTranslators) return Results.StatusCode(403);
        if (request.ExpectedAssignmentVersion < 0 || request.TranslatorUserId == Guid.Empty || request.Note?.Length > 500)
            return Invalid("Invalid assignment version, user ID or note.");
        var job = await db.TranslationJobs.SingleOrDefaultAsync(item => item.Id == translationJobId && item.TenantId == tenantId, ct);
        if (job is null) return Results.NotFound();
        if (job.AssignmentVersion != request.ExpectedAssignmentVersion) return Conflict();
        if (request.TranslatorUserId.HasValue)
        {
            var target = await identity.GetAssigneesAsync(tenantId, request.TranslatorUserId, context.Request.Headers.Authorization.ToString(), ct);
            error = AccessError(target.Status); if (error is not null) return error;
            if (target.Members.Count != 1 || target.Members[0].UserId != request.TranslatorUserId)
                return Invalid("Translator must be an active writing member of the same tenant.");
        }
        TranslationJobAssignmentChange change;
        try
        {
            change = job.AssignTranslator(request.TranslatorUserId, access.Actor.UserId, request.ExpectedAssignmentVersion, request.Note, clock.GetUtcNow());
        }
        catch (ArgumentException exception) { return Invalid(exception.Message); }
        catch (InvalidOperationException) { return Conflict(); }
        db.AssignmentChanges.Add(change);
        await publish.Publish(new TranslationJobAssignedV1(change.Id, tenantId, job.Id, change.Version, change.ActorUserId,
            change.PreviousTranslatorUserId, change.TranslatorUserId, change.OccurredAtUtc), ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 }) { return Conflict(); }
        return Results.Ok(new { job.Id, job.AssignedTranslatorUserId, job.AssignmentVersion, job.AssignmentChangedByUserId, job.AssignmentChangedAtUtc });
    }

    private static IResult Invalid(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["assignment"] = [message] });
    private static IResult Conflict() => Results.Conflict(new { error = "translation_assignment_conflict", detail = "Refresh assignment state before changing it." });
    private static IResult? AccessError(TenantAccessStatus status) => status switch
    {
        TenantAccessStatus.Authorized => null, TenantAccessStatus.Unauthorized => Results.Unauthorized(),
        TenantAccessStatus.Forbidden => Results.StatusCode(403),
        _ => Results.Problem(statusCode: 503, title: "Identity service is unavailable.")
    };
}

public sealed record AssignTranslatorRequest(Guid? TranslatorUserId, long ExpectedAssignmentVersion, string? Note);
public sealed record AssignmentChangeResponse(Guid Id, long Version, Guid ActorUserId, Guid? PreviousTranslatorUserId,
    Guid? TranslatorUserId, string? Note, DateTimeOffset OccurredAtUtc);
public sealed record AssignmentStateResponse(Guid TranslationJobId, Guid? AssignedTranslatorUserId, long AssignmentVersion,
    Guid? ChangedByUserId, DateTimeOffset? ChangedAtUtc, Guid UserId, bool CanManage, IReadOnlyList<AssignmentChangeResponse> History);
