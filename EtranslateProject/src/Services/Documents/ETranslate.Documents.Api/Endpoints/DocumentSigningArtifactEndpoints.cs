using System.Text.Json;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Endpoints;

public static partial class DocumentEndpoints
{
    private static async Task<IResult> GetSigningArtifactAsync(Guid tenantId, Guid translationJobId, Guid documentId,
        HttpContext context, TenantAccessClient identity, DocumentsDbContext db, IDocumentBlobStore store, CancellationToken ct)
    {
        var access = await AuthorizeAsync(tenantId, context, identity, false, ct);
        if (access.Error is not null) return access.Error;
        // One SQL snapshot joins the current approved round and its exact archived PDF.
        var item = await (from document in db.TranslationDocuments.AsNoTracking()
                          where document.Id == documentId && document.TenantId == tenantId && document.TranslationJobId == translationJobId
                          join review in db.Reviews on new { DocumentId = document.Id, Round = document.ReviewRound } equals new { review.DocumentId, review.Round } into reviews
                          from review in reviews.DefaultIfEmpty()
                          join pdf in db.PdfVersions on review.PdfVersionId equals pdf.Id into pdfs
                          from pdf in pdfs.DefaultIfEmpty()
                          select new { Document = document, Review = review, Pdf = pdf }).SingleOrDefaultAsync(ct);
        if (item is null) return Results.NotFound();
        if (item.Document.ReviewStatus != DocumentReviewStatus.Approved)
            return Results.Ok(new SigningArtifactResponse(tenantId, translationJobId, documentId, item.Document.ReviewStatus.ToString(), null));
        var approvedReview = item.Review; var approvedPdf = item.Pdf;
        if (approvedReview is null || approvedPdf is null || approvedReview.Status != DocumentReviewStatus.Approved ||
            approvedPdf.DocumentId != documentId || approvedPdf.RevisionNumber != item.Document.CurrentDraftRevision ||
            approvedReview.RevisionNumber != approvedPdf.RevisionNumber || approvedPdf.DraftRevisionId != approvedReview.DraftRevisionId ||
            approvedPdf.TemplateRevisionId != item.Document.TemplateRevisionId || approvedPdf.Sha256 != approvedReview.PdfSha256 ||
            approvedReview.DecidedByUserId is null || approvedReview.DecidedAtUtc is null)
            return Results.Problem(statusCode: 503, title: "Approved document metadata is inconsistent.");
        bool verified;
        try { verified = await VerifyReviewPdfAsync(approvedPdf, store, ct); }
        catch (IOException) { verified = false; }
        catch (UnauthorizedAccessException) { verified = false; }
        if (!verified) return Results.Problem(statusCode: 503, title: "Archived PDF failed integrity verification.");
        if (!await db.TranslationDocuments.AsNoTracking().AnyAsync(document => document.Id == documentId &&
            document.ReviewStatus == DocumentReviewStatus.Approved && document.ReviewRound == approvedReview.Round &&
            document.CurrentDraftRevision == approvedPdf.RevisionNumber, ct)) return ReviewConflict();
        return Results.Ok(new SigningArtifactResponse(tenantId, translationJobId, documentId, "Approved",
            new ApprovedSigningArtifact(approvedReview.Id, approvedReview.Round, approvedPdf.Id, approvedPdf.DraftRevisionId, approvedPdf.RevisionNumber,
                approvedPdf.TemplateRevisionId, approvedPdf.Sha256, approvedPdf.SizeBytes, "DraftUnsigned", approvedReview.DecidedByUserId.Value,
                approvedReview.DecidedAtUtc.Value, JsonSerializer.Deserialize<ReviewSourceFile[]>(approvedReview.SourceFilesJson)!)));
    }
}

public sealed record SigningArtifactResponse(Guid TenantId, Guid TranslationJobId, Guid DocumentId, string ReviewStatus, ApprovedSigningArtifact? Artifact);
public sealed record ApprovedSigningArtifact(Guid ReviewId, int ReviewRound, Guid PdfVersionId, Guid DraftRevisionId,
    int RevisionNumber, Guid? TemplateRevisionId, string Sha256, long SizeBytes, string Kind,
    Guid ApprovedByUserId, DateTimeOffset ApprovedAtUtc, IReadOnlyList<ReviewSourceFile> SourceFiles);
