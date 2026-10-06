using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    private static async Task<IResult> GetReviewsAsync(Guid tenantId, Guid translationJobId, Guid documentId,
        HttpContext httpContext, TenantAccessClient tenantAccessClient, DocumentsDbContext database, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, false, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await FindDocumentAsync(database, tenantId, translationJobId, documentId, cancellationToken);
        if (document is null) return Results.NotFound();
        var reviews = await database.Reviews.AsNoTracking().Where(review => review.DocumentId == document.Id)
            .OrderByDescending(review => review.Round).ToListAsync(cancellationToken);
        return Results.Ok(new DocumentReviewStateResponse(document.ReviewStatus, document.ReviewRound,
            access.Actor!.UserId, access.Actor.CanManageDocuments, access.Actor.CanReviewDocuments,
            reviews.Select(ReviewResponse).ToArray()));
    }

    private static async Task<IResult> SubmitReviewAsync(Guid tenantId, Guid translationJobId, Guid documentId,
        SubmitDocumentReviewRequest request, HttpContext httpContext, TenantAccessClient tenantAccessClient,
        DocumentsDbContext database, IDocumentBlobStore blobStore, IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await database.TranslationDocuments.Include(item => item.SourceFiles)
            .SingleOrDefaultAsync(item => item.Id == documentId && item.TenantId == tenantId && item.TranslationJobId == translationJobId, cancellationToken);
        if (document is null) return Results.NotFound();
        if (document.IsReviewLocked || request.ExpectedRevision != document.CurrentDraftRevision || request.ExpectedRound != document.ReviewRound)
            return ReviewConflict();
        var pdf = await database.PdfVersions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == request.PdfVersionId && item.DocumentId == document.Id && item.RevisionNumber == request.ExpectedRevision, cancellationToken);
        if (pdf is null) return Results.NotFound();
        var previous = await database.Reviews.AsNoTracking().SingleOrDefaultAsync(
            review => review.DocumentId == document.Id && review.Round == document.ReviewRound, cancellationToken);
        bool verified;
        try { verified = await VerifyReviewPdfAsync(pdf, blobStore, cancellationToken); }
        catch (IOException) { verified = false; }
        if (!verified)
            return Results.Problem(statusCode: 503, title: "The archived PDF is unavailable or failed integrity verification.");
        var sources = JsonSerializer.Serialize(document.SourceFiles.OrderBy(file => file.Id).Select(file => new ReviewSourceFile(
            file.Id, file.OriginalFileName, file.SizeBytes, file.Sha256)));
        var now = timeProvider.GetUtcNow();
        DocumentReview review;
        try { review = document.SubmitReview(request.ExpectedRevision, request.ExpectedRound, pdf, previous, sources, access.Actor!.UserId, now); }
        catch (DocumentReviewConflictException) { return ReviewConflict(); }
        database.Reviews.Add(review);
        await PublishReviewAsync(document, review, access.Actor!.UserId, now, publishEndpoint, cancellationToken);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return ReviewConflict(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 }) { return ReviewConflict(); }
        return Results.Created($"/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/reviews", ReviewResponse(review));
    }

    private static async Task<IResult> DecideReviewAsync(Guid tenantId, Guid translationJobId, Guid documentId, Guid reviewId,
        DecideDocumentReviewRequest request, HttpContext httpContext, TenantAccessClient tenantAccessClient,
        DocumentsDbContext database, IPublishEndpoint publishEndpoint, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(tenantId, httpContext, tenantAccessClient, true, cancellationToken);
        if (access.Error is not null) return access.Error;
        var document = await FindDocumentAsync(database, tenantId, translationJobId, documentId, cancellationToken);
        if (document is null) return Results.NotFound();
        var review = await database.Reviews.SingleOrDefaultAsync(item => item.Id == reviewId && item.DocumentId == document.Id, cancellationToken);
        if (review is null) return Results.NotFound();
        var actor = access.Actor!;
        if (request.Action is not ("Approve" or "RequestChanges" or "Withdraw" or "Reopen"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["Unknown review action."] });
        if (request.Action == "Withdraw" ? actor.UserId != review.SubmittedByUserId && !actor.CanReviewDocuments : !actor.CanReviewDocuments)
            return Results.StatusCode(403);
        var now = timeProvider.GetUtcNow();
        try
        {
            if (request.Action == "Reopen") document.ReopenReview(review, actor.UserId, request.Note, now);
            else document.DecideReview(review, request.Action switch
            {
                "Approve" => DocumentReviewStatus.Approved,
                "RequestChanges" => DocumentReviewStatus.ChangesRequested,
                _ => DocumentReviewStatus.Withdrawn
            }, actor.UserId, request.Note, now);
        }
        catch (DocumentReviewConflictException) { return ReviewConflict(); }
        catch (DocumentValidationException exception) { return Results.ValidationProblem(exception.Errors.ToDictionary()); }
        await PublishReviewAsync(document, review, actor.UserId, now, publishEndpoint, cancellationToken);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return ReviewConflict(); }
        return Results.Ok(ReviewResponse(review));
    }

    private static IResult ReviewConflict() => Results.Conflict(new { error = "document_review_conflict", detail = "Review status or revision changed. Refresh; returned corrections require a new saved draft." });

    private static async Task<bool> VerifyReviewPdfAsync(DocumentPdfVersion pdf, IDocumentBlobStore store, CancellationToken cancellationToken)
    {
        if (pdf.SizeBytes is <= 0 or > 25 * 1024 * 1024) return false;
        await using var stream = await store.OpenReadAsync(pdf.StorageKey, cancellationToken);
        if (stream is null) return false;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long total = 0; int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += count; if (total > pdf.SizeBytes) return false; hash.AppendData(buffer, 0, count);
        }
        return total == pdf.SizeBytes && Convert.ToHexString(hash.GetHashAndReset()).Equals(pdf.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static Task PublishReviewAsync(TranslationDocument document, DocumentReview review, Guid actor, DateTimeOffset now,
        IPublishEndpoint endpoint, CancellationToken cancellationToken) => endpoint.Publish(new DocumentReviewChangedV1(
            Guid.NewGuid(), document.TenantId, document.TranslationJobId, document.Id, review.Id, review.Round,
            review.RevisionNumber, review.PdfVersionId, review.PdfSha256, review.Status.ToString(), actor, now), cancellationToken);

    private static DocumentReviewResponse ReviewResponse(DocumentReview review) => new(review.Id, review.Round,
        review.Status, review.RevisionNumber, review.PdfVersionId, review.PdfSha256,
        JsonSerializer.Deserialize<ReviewSourceFile[]>(review.SourceFilesJson)!, review.SubmittedByUserId, review.SubmittedAtUtc,
        review.DecidedByUserId, review.DecidedAtUtc, review.DecisionNote, review.ReopenedByUserId, review.ReopenedAtUtc, review.ReopenNote);
}

public sealed record SubmitDocumentReviewRequest(int ExpectedRevision, int ExpectedRound, Guid PdfVersionId);
public sealed record DecideDocumentReviewRequest(string Action, string? Note);
public sealed record ReviewSourceFile(Guid Id, string OriginalFileName, long SizeBytes, string Sha256);
public sealed record DocumentReviewStateResponse(DocumentReviewStatus Status, int Round, Guid UserId, bool CanManage, bool CanReview, IReadOnlyList<DocumentReviewResponse> Reviews);
public sealed record DocumentReviewResponse(Guid Id, int Round, DocumentReviewStatus Status, int RevisionNumber,
    Guid PdfVersionId, string PdfSha256, IReadOnlyList<ReviewSourceFile> SourceFiles, Guid SubmittedByUserId, DateTimeOffset SubmittedAtUtc,
    Guid? DecidedByUserId, DateTimeOffset? DecidedAtUtc, string? DecisionNote,
    Guid? ReopenedByUserId, DateTimeOffset? ReopenedAtUtc, string? ReopenNote);
