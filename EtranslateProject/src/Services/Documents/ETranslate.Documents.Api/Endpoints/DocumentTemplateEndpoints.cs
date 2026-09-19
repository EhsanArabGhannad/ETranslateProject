using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static class DocumentTemplateEndpoints
{
    public static IEndpointRouteBuilder MapDocumentTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/document-templates")
            .WithTags("Document Templates");

        group.MapPost("/", CreateTemplateAsync);
        group.MapGet("/", GetTemplatesAsync);
        group.MapGet("/{templateId:guid}", GetTemplateAsync);
        group.MapPost("/{templateId:guid}/revisions", CreateRevisionAsync);
        group.MapGet("/{templateId:guid}/revisions", GetRevisionsAsync);
        group.MapGet("/{templateId:guid}/revisions/{revisionNumber:int}", GetRevisionAsync);
        group.MapPut("/{templateId:guid}/status", SetTemplateStatusAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTemplateAsync(
        Guid tenantId,
        CreateDocumentTemplateRequest request,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: true,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var now = timeProvider.GetUtcNow();
        DocumentTemplate template;
        try
        {
            template = DocumentTemplate.Create(
                tenantId,
                request.Name,
                request.Description,
                request.EditorContentJson,
                request.HeaderContentJson,
                request.FooterContentJson,
                request.PageLayoutJson,
                request.WatermarkJson,
                authorization.Actor!.UserId,
                now);
        }
        catch (DocumentTemplateValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }

        if (await database.DocumentTemplates.AnyAsync(
            candidate => candidate.TenantId == tenantId && candidate.Name == template.Name,
            cancellationToken))
        {
            return TemplateNameConflict();
        }

        database.DocumentTemplates.Add(template);
        await publishEndpoint.Publish(
            new DocumentTemplateCreatedV1(
                Guid.NewGuid(),
                template.Id,
                template.TenantId,
                template.CreatedByUserId,
                template.CurrentRevision,
                now),
            cancellationToken);
        var initialRevision = template.Revisions.Single();
        await publishEndpoint.Publish(
            new DocumentTemplateRevisionCreatedV1(
                Guid.NewGuid(),
                template.Id,
                template.TenantId,
                initialRevision.RevisionNumber,
                initialRevision.CreatedByUserId,
                now),
            cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return TemplateNameConflict();
        }

        return Results.Created(
            $"/api/v1/tenants/{tenantId}/document-templates/{template.Id}",
            ToDetailResponse(template, initialRevision));
    }

    private static async Task<IResult> GetTemplatesAsync(
        Guid tenantId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var templates = await database.DocumentTemplates
            .AsNoTracking()
            .Where(template => template.TenantId == tenantId)
            .OrderBy(template => template.Name)
            .Select(template => new DocumentTemplateSummaryResponse(
                template.Id,
                template.Name,
                template.Description,
                template.IsActive,
                template.CurrentRevision,
                template.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(templates);
    }

    private static async Task<IResult> GetTemplateAsync(
        Guid tenantId,
        Guid templateId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var result = await (
                from template in database.DocumentTemplates.AsNoTracking()
                join revision in database.TemplateRevisions.AsNoTracking()
                    on new { TemplateId = template.Id, RevisionNumber = template.CurrentRevision }
                    equals new { revision.TemplateId, revision.RevisionNumber }
                where template.Id == templateId && template.TenantId == tenantId
                select new { Template = template, Revision = revision })
            .SingleOrDefaultAsync(cancellationToken);

        return result is null
            ? Results.NotFound()
            : Results.Ok(ToDetailResponse(result.Template, result.Revision));
    }

    private static async Task<IResult> CreateRevisionAsync(
        Guid tenantId,
        Guid templateId,
        CreateDocumentTemplateRevisionRequest request,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: true,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var template = await database.DocumentTemplates.SingleOrDefaultAsync(
            candidate => candidate.Id == templateId && candidate.TenantId == tenantId,
            cancellationToken);
        if (template is null)
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        DocumentTemplateRevision revision;
        try
        {
            revision = template.AddRevision(
                request.ExpectedCurrentRevision,
                request.EditorContentJson,
                request.HeaderContentJson,
                request.FooterContentJson,
                request.PageLayoutJson,
                request.WatermarkJson,
                authorization.Actor!.UserId,
                now);
        }
        catch (DocumentTemplateValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }
        catch (DocumentTemplateRevisionConflictException exception)
        {
            return RevisionConflict(exception.ExpectedRevision, exception.ActualRevision);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = "document_template_is_archived", detail = exception.Message });
        }

        database.TemplateRevisions.Add(revision);
        await publishEndpoint.Publish(
            new DocumentTemplateRevisionCreatedV1(
                Guid.NewGuid(),
                template.Id,
                template.TenantId,
                revision.RevisionNumber,
                revision.CreatedByUserId,
                now),
            cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Results.Conflict(new
            {
                error = "document_template_revision_conflict",
                expectedRevision = request.ExpectedCurrentRevision
            });
        }

        return Results.Created(
            $"/api/v1/tenants/{tenantId}/document-templates/{templateId}/revisions/{revision.RevisionNumber}",
            ToRevisionResponse(revision));
    }

    private static async Task<IResult> GetRevisionsAsync(
        Guid tenantId,
        Guid templateId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var exists = await database.DocumentTemplates.AsNoTracking().AnyAsync(
            template => template.Id == templateId && template.TenantId == tenantId,
            cancellationToken);
        if (!exists)
        {
            return Results.NotFound();
        }

        var revisions = await database.TemplateRevisions
            .AsNoTracking()
            .Where(revision => revision.TemplateId == templateId)
            .OrderByDescending(revision => revision.RevisionNumber)
            .Select(revision => new DocumentTemplateRevisionSummaryResponse(
                revision.Id,
                revision.RevisionNumber,
                revision.CreatedByUserId,
                revision.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(revisions);
    }

    private static async Task<IResult> GetRevisionAsync(
        Guid tenantId,
        Guid templateId,
        int revisionNumber,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: false,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var revision = await (
                from candidate in database.TemplateRevisions.AsNoTracking()
                join template in database.DocumentTemplates.AsNoTracking()
                    on candidate.TemplateId equals template.Id
                where template.Id == templateId &&
                      template.TenantId == tenantId &&
                      candidate.RevisionNumber == revisionNumber
                select candidate)
            .SingleOrDefaultAsync(cancellationToken);

        return revision is null
            ? Results.NotFound()
            : Results.Ok(ToRevisionResponse(revision));
    }

    private static async Task<IResult> SetTemplateStatusAsync(
        Guid tenantId,
        Guid templateId,
        SetDocumentTemplateStatusRequest request,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        DocumentsDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            tenantId,
            httpContext,
            tenantAccessClient,
            requiresWriteAccess: true,
            cancellationToken);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var template = await database.DocumentTemplates.SingleOrDefaultAsync(
            candidate => candidate.Id == templateId && candidate.TenantId == tenantId,
            cancellationToken);
        if (template is null)
        {
            return Results.NotFound();
        }

        template.SetActive(request.IsActive, timeProvider.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(new DocumentTemplateSummaryResponse(
            template.Id,
            template.Name,
            template.Description,
            template.IsActive,
            template.CurrentRevision,
            template.UpdatedAtUtc));
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        HttpContext httpContext,
        TenantAccessClient tenantAccessClient,
        bool requiresWriteAccess,
        CancellationToken cancellationToken)
    {
        var access = await tenantAccessClient.CheckAccessAsync(
            tenantId,
            httpContext.Request.Headers.Authorization.ToString(),
            cancellationToken);
        IResult? error = access.Status switch
        {
            TenantAccessStatus.Authorized => null,
            TenantAccessStatus.Unauthorized => Results.Unauthorized(),
            TenantAccessStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            TenantAccessStatus.IdentityServiceUnavailable => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Identity service is unavailable."),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
        if (error is not null)
        {
            return new AuthorizationResult(null, error);
        }

        if (requiresWriteAccess && !access.Actor!.CanManageDocuments)
        {
            return new AuthorizationResult(
                null,
                Results.StatusCode(StatusCodes.Status403Forbidden));
        }

        return new AuthorizationResult(access.Actor, null);
    }

    private static IResult TemplateNameConflict() =>
        Results.Conflict(new { error = "document_template_name_already_exists" });

    private static IResult RevisionConflict(int expectedRevision, int actualRevision) =>
        Results.Conflict(new
        {
            error = "document_template_revision_conflict",
            expectedRevision,
            actualRevision
        });

    private static DocumentTemplateDetailResponse ToDetailResponse(
        DocumentTemplate template,
        DocumentTemplateRevision revision) =>
        new(
            template.Id,
            template.TenantId,
            template.Name,
            template.Description,
            template.IsActive,
            template.CurrentRevision,
            ToRevisionResponse(revision),
            template.CreatedByUserId,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);

    private static DocumentTemplateRevisionResponse ToRevisionResponse(DocumentTemplateRevision revision) =>
        new(
            revision.Id,
            revision.TemplateId,
            revision.RevisionNumber,
            revision.EditorContentJson,
            revision.HeaderContentJson,
            revision.FooterContentJson,
            revision.PageLayoutJson,
            revision.WatermarkJson,
            revision.CreatedByUserId,
            revision.CreatedAtUtc);

    private sealed record AuthorizationResult(TenantActor? Actor, IResult? Error);
}

public sealed record CreateDocumentTemplateRequest(
    string Name,
    string? Description,
    string EditorContentJson,
    string? HeaderContentJson,
    string? FooterContentJson,
    string PageLayoutJson,
    string? WatermarkJson);

public sealed record CreateDocumentTemplateRevisionRequest(
    int ExpectedCurrentRevision,
    string EditorContentJson,
    string? HeaderContentJson,
    string? FooterContentJson,
    string PageLayoutJson,
    string? WatermarkJson);

public sealed record SetDocumentTemplateStatusRequest(bool IsActive);

public sealed record DocumentTemplateSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int CurrentRevision,
    DateTimeOffset UpdatedAtUtc);

public sealed record DocumentTemplateDetailResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    bool IsActive,
    int CurrentRevision,
    DocumentTemplateRevisionResponse Revision,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DocumentTemplateRevisionSummaryResponse(
    Guid Id,
    int RevisionNumber,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record DocumentTemplateRevisionResponse(
    Guid Id,
    Guid TemplateId,
    int RevisionNumber,
    string EditorContentJson,
    string? HeaderContentJson,
    string? FooterContentJson,
    string PageLayoutJson,
    string? WatermarkJson,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);
