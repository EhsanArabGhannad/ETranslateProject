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
    private static async Task<IResult> ChangeTemplateAsync(Guid tenantId, Guid translationJobId, Guid documentId,
        ChangeDocumentTemplateRequest request, HttpContext httpContext, TenantAccessClient tenantAccessClient,
        DocumentsDbContext database, IPublishEndpoint publishEndpoint, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await FindDocumentAsync(database, tenantId, translationJobId, documentId, cancellationToken);
        if (document is null) return Results.NotFound();
        if (document.IsReviewLocked) return Results.Conflict(new { error = "document_review_locked" });
        if (request.ExpectedCurrentRevision < 1 || request.ExpectedCurrentRevision != document.CurrentDraftRevision)
            return RevisionConflict(request.ExpectedCurrentRevision, document.CurrentDraftRevision);
        var current = await database.DraftRevisions.AsNoTracking().SingleOrDefaultAsync(d => d.DocumentId == documentId && d.RevisionNumber == document.CurrentDraftRevision, cancellationToken);
        if (current is null) return Results.Conflict(new { error = "current_draft_unavailable" });
        var target = await (from template in database.DocumentTemplates
            join revision in database.TemplateRevisions on template.Id equals revision.TemplateId
            where template.TenantId == tenantId && template.Id == request.TemplateId && revision.RevisionNumber == request.RevisionNumber
            select new { Template = template, Revision = revision }).SingleOrDefaultAsync(cancellationToken);
        if (target is null) return Results.NotFound();
        if (!current.ContentDescriptor.IsSupported || !target.Revision.ContentDescriptor.IsSupported)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ContentFormat"] = ["Unsupported content format/version; no template change was made."] });
        IReadOnlyCollection<Guid> assets;
        try { assets = TemplateAssetReferences.Parse(current.EditorContentJson); }
        catch (DocumentTemplateValidationException error) { return Results.ValidationProblem(error.Errors.ToDictionary()); }
        if (assets.Count > 0 && await database.TemplateAssets.CountAsync(a => a.TemplateId == target.Template.Id && assets.Contains(a.Id), cancellationToken) != assets.Count)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["assetId"] = ["Saved body images must belong to the target template; no image or text was removed."] });
        var now = timeProvider.GetUtcNow();
        var previousTemplate = document.TemplateRevisionId;
        DocumentDraftRevision draft;
        try { draft = document.ChangeTemplate(request.ExpectedCurrentRevision, target.Template, target.Revision, current, access.Actor!.UserId, now); }
        catch (DocumentValidationException error) { return Results.ValidationProblem(error.Errors.ToDictionary()); }
        catch (DocumentRevisionConflictException) { return RevisionConflict(request.ExpectedCurrentRevision, document.CurrentDraftRevision); }
        catch (DocumentReviewConflictException) { return Results.Conflict(new { error = "document_review_locked" }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = "document_template_cannot_be_changed", detail = error.Message }); }
        database.DraftRevisions.Add(draft);
        await publishEndpoint.Publish(new DocumentTemplateChangedV1(Guid.NewGuid(), tenantId, translationJobId, documentId,
            previousTemplate, target.Revision.Id, draft.RevisionNumber, access.Actor!.UserId, now), cancellationToken);
        await publishEndpoint.Publish(new DocumentDraftRevisionCreatedV1(Guid.NewGuid(), documentId, translationJobId, tenantId,
            draft.RevisionNumber, access.Actor.UserId, now), cancellationToken);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "document_revision_conflict" }); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 }) { return Results.Conflict(new { error = "document_revision_conflict" }); }
        return Results.Ok(ToResponse(document));
    }
}
public sealed record ChangeDocumentTemplateRequest(int ExpectedCurrentRevision, Guid TemplateId, int RevisionNumber);
