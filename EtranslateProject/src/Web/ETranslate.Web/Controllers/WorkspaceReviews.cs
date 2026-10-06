using System.Net;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    [HttpPost] public Task<IActionResult> SubmitReview(Guid tenantId, Guid jobId, int expectedRevision, int expectedRound, Guid pdfVersionId)
        => ChangeReview(tenantId, jobId, null, null, null, expectedRevision, expectedRound, pdfVersionId);

    [HttpPost] public Task<IActionResult> DecideReview(Guid tenantId, Guid jobId, Guid reviewId, string reviewAction, string? note)
        => ChangeReview(tenantId, jobId, reviewId, reviewAction, note, 0, 0, Guid.Empty);

    private async Task<IActionResult> ChangeReview(Guid tenantId, Guid jobId, Guid? reviewId, string? action, string? note,
        int expectedRevision, int expectedRound, Guid pdfId)
    {
        var json = Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
        try
        {
            if (!ModelState.IsValid || (reviewId.HasValue ? reviewId == Guid.Empty || action is not ("Approve" or "RequestChanges" or "Withdraw" or "Reopen") : expectedRevision < 1 || expectedRound < 0 || pdfId == Guid.Empty))
                throw new BackendException(HttpStatusCode.BadRequest);
            var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
            var path = $"{DocumentsPath(tenantId, jobId)}/{document.Id}/reviews";
            var review = reviewId.HasValue
                ? await api.PostAsync<ReviewView>("documents", $"{path}/{reviewId}/decision", new { action, note })
                : await api.PostAsync<ReviewView>("documents", path, new { expectedRevision, expectedRound, pdfVersionId = pdfId });
            var message = $"وضعیت بازبینی نسخه‌ی {review.RevisionNumber}: {ReviewStateView.Label(review.Status)}. این عملیات امضای الکترونیکی یا تأیید نوتر نیست.";
            if (json) return Ok(new { message });
            TempData["Notice"] = message;
        }
        catch (BackendException error)
        {
            var message = error.Status switch
            {
                HttpStatusCode.Conflict => "وضعیت بازبینی یا نسخه تغییر کرده است؛ آخرین وضعیت را بررسی کنید. پس از برگشت یا بازگشایی، یک نسخه‌ی جدید ذخیره کنید.",
                HttpStatusCode.BadRequest => "اطلاعات بازبینی معتبر نیست؛ دلیل برگشت، پس‌گرفتن یا بازگشایی الزامی و حداکثر ۲۰۰۰ نویسه است.",
                _ => error.UserMessage
            };
            if (json) return StatusCode((int)error.Status, new { message });
            if (error.Status == HttpStatusCode.Unauthorized) throw;
            TempData["Notice"] = message;
        }
        return RedirectToAction(nameof(Editor), new { tenantId, jobId });
    }
}
