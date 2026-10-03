using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    private static async Task<IResult> ApplyTemplateAsync(
        Guid tenantId, Guid translationJobId, Guid documentId, ApplyDocumentTemplateRequest request,
        HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database,
        IPublishEndpoint publishEndpoint, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await database.TranslationDocuments.Include(item => item.SourceFiles)
            .SingleOrDefaultAsync(item => item.Id == documentId && item.TenantId == tenantId &&
                item.TranslationJobId == translationJobId, cancellationToken);
        if (document is null) return Results.NotFound();
        var result = await (
            from template in database.DocumentTemplates
            join revision in database.TemplateRevisions on template.Id equals revision.TemplateId
            where template.TenantId == tenantId && template.Id == request.TemplateId &&
                  revision.RevisionNumber == request.RevisionNumber
            select new { Template = template, Revision = revision }).SingleOrDefaultAsync(cancellationToken);
        if (result is null) return Results.NotFound();

        var now = timeProvider.GetUtcNow();
        DocumentDraftRevision draft;
        try
        {
            draft = document.ApplyTemplate(result.Template, result.Revision, access.Actor!.UserId, now);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = "document_template_cannot_be_applied", detail = exception.Message });
        }

        database.DraftRevisions.Add(draft);
        await publishEndpoint.Publish(new DocumentTemplateAppliedV1(
            Guid.NewGuid(), document.Id, translationJobId, tenantId, result.Revision.Id, access.Actor!.UserId, now),
            cancellationToken);
        await publishEndpoint.Publish(new DocumentDraftRevisionCreatedV1(
            Guid.NewGuid(), document.Id, translationJobId, tenantId, draft.RevisionNumber, access.Actor.UserId, now),
            cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "document_revision_conflict" });
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Results.Conflict(new { error = "document_revision_conflict" });
        }
        return Results.Ok(ToResponse(document));
    }

    private static async Task<IResult> GetAppliedTemplateAsync(
        Guid tenantId, Guid translationJobId, Guid documentId, HttpContext httpContext,
        TenantAccessClient tenantAccessClient, DocumentsDbContext database, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        var revision = await (
            from document in database.TranslationDocuments.AsNoTracking()
            join candidate in database.TemplateRevisions.AsNoTracking() on document.TemplateRevisionId equals candidate.Id
            where document.Id == documentId && document.TenantId == tenantId && document.TranslationJobId == translationJobId
            select candidate).SingleOrDefaultAsync(cancellationToken);
        return revision is null ? Results.NotFound() : Results.Ok(new DocumentTemplateRevisionResponse(
            revision.Id, revision.TemplateId, revision.RevisionNumber, revision.EditorContentJson,
            revision.HeaderContentJson, revision.FooterContentJson, revision.PageLayoutJson,
            revision.WatermarkJson, revision.CreatedByUserId, revision.CreatedAtUtc));
    }

    private static async Task<IResult?> ValidateDraftAssetsAsync(
        TranslationDocument document, string content, DocumentsDbContext database, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> ids;
        try { ids = TemplateAssetReferences.Parse(content); }
        catch (DocumentTemplateValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }
        if (ids.Count == 0) return null;
        var templateId = await database.TemplateRevisions.AsNoTracking()
            .Where(revision => revision.Id == document.TemplateRevisionId)
            .Select(revision => (Guid?)revision.TemplateId).SingleOrDefaultAsync(cancellationToken);
        var count = templateId is null ? 0 : await database.TemplateAssets.CountAsync(
            asset => asset.TemplateId == templateId && ids.Contains(asset.Id), cancellationToken);
        return count == ids.Count ? null : Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["assetId"] = ["Draft assets must belong to the document's pinned template."]
        });
    }
}

public sealed record ApplyDocumentTemplateRequest(Guid TemplateId, int RevisionNumber);
