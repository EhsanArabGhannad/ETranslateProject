using System.Text.Json;
using ETranslate.Contracts.Trust;
using ETranslate.Trust.Api.Dependencies;
using ETranslate.Trust.Api.Persistence;
using ETranslate.Trust.Api.Preparations;
using ETranslate.Trust.Api.Providers;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Trust.Api.Endpoints;

public static class SigningPreparationEndpoints
{
    public static IEndpointRouteBuilder MapSigningPreparationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/translation-jobs/{translationJobId:guid}/documents/{documentId:guid}/signing-preparations")
            .WithTags("Signing Preparation");
        group.MapGet("/", GetAsync); group.MapPost("/", CreateAsync);
        group.MapPost("/{preparationId:guid}/cancel", CancelAsync);
        group.MapPost("/{preparationId:guid}/dispatch", DispatchDisabledAsync);
        return endpoints;
    }
    private static string ArtifactPath(Guid tenant, Guid job, Guid document) => $"/api/v1/tenants/{tenant}/translation-jobs/{job}/documents/{document}/signing-artifact";
    private static async Task<DependencyResult<TrustActor>> ActorAsync(Guid tenantId, HttpContext context, TrustDependencyClient dependencies, CancellationToken ct)
    {
        var actor = await dependencies.GetAsync<TrustActor>("identity-access", $"/api/v1/tenants/{tenantId}/access", context.Request.Headers.Authorization.ToString(), ct);
        if (actor.Status == 404) return new(403, null);
        return actor.Status == 200 && actor.Value?.IsValid(tenantId) != true ? new(503, null) : actor;
    }
    private static IResult Error(int status) => status == 503 ? Results.Problem(statusCode: 503, title: "A required service is unavailable.") : Results.StatusCode(status);
    private static IResult Conflict() => Results.Conflict(new { error = "signing_preparation_conflict", detail = "Approval or preparation changed. Refresh; cancel an existing preparation before reissuing." });
    private static IResult Invalid(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["preparation"] = [message] });

    private static async Task<IResult> GetAsync(Guid tenantId, Guid translationJobId, Guid documentId, HttpContext context,
        TrustDependencyClient dependencies, TrustDbContext db, ISignatureProviderCapabilities provider, CancellationToken ct)
    {
        var access = await ActorAsync(tenantId, context, dependencies, ct); if (access.Status != 200) return Error(access.Status);
        var snapshot = await dependencies.GetAsync<DocumentSigningSnapshot>("documents", ArtifactPath(tenantId, translationJobId, documentId), context.Request.Headers.Authorization.ToString(), ct);
        if (snapshot.Status is 401 or 403 or 404) return Error(snapshot.Status);
        if (snapshot.Status == 200 && snapshot.Value?.IsScoped(tenantId, translationJobId, documentId) != true) return Error(503);
        var requests = await db.Preparations.AsNoTracking().Include(item => item.Stages)
            .Where(item => item.TenantId == tenantId && item.TranslationJobId == translationJobId && item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedAtUtc).Take(20).ToListAsync(ct);
        var artifact = snapshot.Value?.ReviewStatus == "Approved" ? snapshot.Value.Artifact : null;
        return Results.Ok(new SigningPreparationStateResponse(access.Value!.UserId, access.Value.CanManage, access.Value.SignaturePolicy,
            snapshot.Status == 200, snapshot.Value?.ReviewStatus, artifact, provider.Current,
            requests.Select(item => Response(item, artifact)).ToArray()));
    }
    private static async Task<IResult> CreateAsync(Guid tenantId, Guid translationJobId, Guid documentId, PrepareSigningRequest request,
        HttpContext context, TrustDependencyClient dependencies, TrustDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var access = await ActorAsync(tenantId, context, dependencies, ct); if (access.Status != 200) return Error(access.Status);
        var actor = access.Value!; if (!actor.CanManage) return Results.StatusCode(403);
        if (request.TranslatorUserId == Guid.Empty || request.ExpectedReviewId == Guid.Empty || request.ExpectedPdfVersionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.TranslatorStatement) || request.TranslatorStatement.Length > 4000 || request.OfficeStatement?.Length > 4000)
            return Invalid("Current review/PDF, translator and proposed statement are required (maximum 4000 characters).");
        var auth = context.Request.Headers.Authorization.ToString();
        var job = await dependencies.GetAsync<TrustJob>("translation-workflow", $"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}", auth, ct);
        if (job.Status != 200) return Error(job.Status);
        if (job.Value?.Id != translationJobId || job.Value.TenantId != tenantId || job.Value.SignaturePolicy != actor.SignaturePolicy) return Error(503);
        var snapshot = await dependencies.GetAsync<DocumentSigningSnapshot>("documents", ArtifactPath(tenantId, translationJobId, documentId), auth, ct);
        if (snapshot.Status != 200) return Error(snapshot.Status);
        if (snapshot.Value?.IsScoped(tenantId, translationJobId, documentId) != true) return Error(503);
        var artifact = snapshot.Value.ReviewStatus == "Approved" ? snapshot.Value.Artifact : null;
        if (artifact is null || artifact.ReviewId != request.ExpectedReviewId || artifact.PdfVersionId != request.ExpectedPdfVersionId) return Conflict();
        var translator = await dependencies.GetAsync<EligibleSigner>("identity-access", $"/api/v1/tenants/{tenantId}/translation-assignees/{request.TranslatorUserId}", auth, ct);
        if (translator.Status == 404) return Invalid("Translator must be an active writing member of this tenant.");
        if (translator.Status != 200) return Error(translator.Status);
        if (translator.Value?.UserId != request.TranslatorUserId || translator.Value.Role is not ("Owner" or "Administrator" or "Translator")) return Invalid("Invalid translator.");
        if (actor.SignaturePolicy == "TranslatorAndTranslationOffice")
        {
            if (request.OfficeSignerUserId is null || request.OfficeSignerUserId == Guid.Empty) return Invalid("An office manager signer is required.");
            var office = await dependencies.GetAsync<EligibleSigner>("identity-access", $"/api/v1/tenants/{tenantId}/translation-assignees/{request.OfficeSignerUserId}", auth, ct);
            if (office.Status == 404) return Invalid("Office signer must be an active manager of this tenant.");
            if (office.Status != 200) return Error(office.Status);
            if (office.Value?.UserId != request.OfficeSignerUserId || office.Value.Role is not ("Owner" or "Administrator")) return Invalid("Only a manager may be planned for the office stage.");
        }
        SigningPreparation preparation;
        try
        {
            var pinned = new PreparationArtifact(tenantId, translationJobId, documentId, artifact.ReviewId, artifact.ReviewRound,
                artifact.PdfVersionId, artifact.DraftRevisionId, artifact.RevisionNumber, artifact.TemplateRevisionId, artifact.Sha256,
                artifact.SizeBytes, artifact.Kind, artifact.ApprovedByUserId, artifact.ApprovedAtUtc, JsonSerializer.Serialize(artifact.SourceFiles));
            preparation = SigningPreparation.Create(pinned, actor.SignaturePolicy, actor.UserId, clock.GetUtcNow(), request.TranslatorUserId,
                request.TranslatorStatement, request.TranslatorMethod, request.OfficeSignerUserId, request.OfficeStatement, request.OfficeMethod);
        }
        catch (ArgumentException exception) { return Invalid(exception.Message); }
        // Recheck after member lookups; this is still not a distributed lock or a dispatch authorization.
        var latest = await dependencies.GetAsync<DocumentSigningSnapshot>("documents", ArtifactPath(tenantId, translationJobId, documentId), auth, ct);
        if (latest.Status != 200) return Error(latest.Status);
        if (latest.Value?.IsScoped(tenantId, translationJobId, documentId) != true) return Error(503);
        if (latest.Value.ReviewStatus != "Approved" || latest.Value.Artifact is not { } current ||
            !preparation.MatchesCurrentArtifact(current.ReviewId, current.PdfVersionId, current.Sha256)) return Conflict();
        db.Preparations.Add(preparation);
        await PublishAsync(preparation, actor.UserId, preparation.CreatedAtUtc, publish, ct);
        if (!await SaveAsync(db, ct)) return Conflict();
        return Results.Created($"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/signing-preparations", Response(preparation, artifact));
    }
    private static async Task<IResult> CancelAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid preparationId,
        CancelPreparationRequest request, HttpContext context, TrustDependencyClient dependencies, TrustDbContext db,
        IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var access = await ActorAsync(tenantId, context, dependencies, ct); if (access.Status != 200) return Error(access.Status);
        if (!access.Value!.CanManage) return Results.StatusCode(403);
        var item = await db.Preparations.Include(item => item.Stages).SingleOrDefaultAsync(item => item.Id == preparationId &&
            item.TenantId == tenantId && item.TranslationJobId == translationJobId && item.DocumentId == documentId, ct);
        if (item is null) return Results.NotFound();
        var now = clock.GetUtcNow();
        try { item.Cancel(request.ExpectedVersion, access.Value.UserId, request.Reason, now); }
        catch (ArgumentException exception) { return Invalid(exception.Message); }
        catch (InvalidOperationException) { return Conflict(); }
        await PublishAsync(item, access.Value.UserId, now, publish, ct);
        return await SaveAsync(db, ct) ? Results.Ok(Response(item, null)) : Conflict();
    }
    private static async Task<IResult> DispatchDisabledAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid preparationId,
        HttpContext context, TrustDependencyClient dependencies, TrustDbContext db, CancellationToken ct)
    {
        var access = await ActorAsync(tenantId, context, dependencies, ct); if (access.Status != 200) return Error(access.Status);
        if (!access.Value!.CanManage) return Results.StatusCode(403);
        var item = await db.Preparations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == preparationId && item.TenantId == tenantId &&
            item.TranslationJobId == translationJobId && item.DocumentId == documentId, ct);
        if (item is null) return Results.NotFound();
        if (item.Status != SigningPreparationStatus.Prepared) return Conflict();
        return Results.Problem(statusCode: 501, title: "Real signing is not implemented.", detail: "No provider or final signable PDF is configured. This preparation cannot be dispatched or marked signed.");
    }
    private static SigningPreparationResponse Response(SigningPreparation item, ApprovedArtifactSnapshot? current) => new(item.Id,
        item.Status.ToString(), item.Version, item.ReviewId, item.ReviewRound, item.PdfVersionId, item.RevisionNumber, item.PdfSha256,
        item.ArtifactKind, item.SignaturePolicy, item.CreatedByUserId, item.CreatedAtUtc, item.CancelledByUserId, item.CancelledAtUtc, item.CancellationReason,
        current is not null && item.MatchesCurrentArtifact(current.ReviewId, current.PdfVersionId, current.Sha256),
        item.Stages.OrderBy(stage => stage.Order).Select(stage => new PlannedStageResponse(stage.Order, stage.Role.ToString(), stage.SignerUserId,
            stage.ProposedStatement, stage.RequestedMethod.ToString())).ToArray());
    private static Task PublishAsync(SigningPreparation item, Guid actor, DateTimeOffset now, IPublishEndpoint publish, CancellationToken ct) =>
        publish.Publish(new SigningPreparationChangedV1(Guid.NewGuid(), item.Id, item.TenantId, item.TranslationJobId, item.DocumentId,
            item.ReviewId, item.PdfVersionId, item.PdfSha256, item.Status.ToString(), item.Version, actor, now), ct);
    private static async Task<bool> SaveAsync(TrustDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 }) { return false; }
    }
}

public sealed record PrepareSigningRequest(Guid ExpectedReviewId, Guid ExpectedPdfVersionId, Guid TranslatorUserId,
    string TranslatorStatement, RequestedSignatureMethod TranslatorMethod, Guid? OfficeSignerUserId,
    string? OfficeStatement, RequestedSignatureMethod? OfficeMethod);
public sealed record CancelPreparationRequest(long ExpectedVersion, string Reason);
public sealed record PlannedStageResponse(int Order, string Role, Guid SignerUserId, string ProposedStatement, string RequestedMethod);
public sealed record SigningPreparationResponse(Guid Id, string Status, long Version, Guid ReviewId, int ReviewRound,
    Guid PdfVersionId, int RevisionNumber, string PdfSha256, string ArtifactKind, string SignaturePolicy, Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc, Guid? CancelledByUserId, DateTimeOffset? CancelledAtUtc, string? CancellationReason,
    bool IsCurrentArtifact, IReadOnlyList<PlannedStageResponse> Stages);
public sealed record SigningPreparationStateResponse(Guid UserId, bool CanManage, string SignaturePolicy, bool ArtifactServiceAvailable,
    string? ReviewStatus, ApprovedArtifactSnapshot? Artifact, SignatureProviderCapabilities Provider, IReadOnlyList<SigningPreparationResponse> Preparations);
