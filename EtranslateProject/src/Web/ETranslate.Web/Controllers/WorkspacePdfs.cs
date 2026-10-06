using System.Net;
using System.Security.Cryptography;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    private const long MaximumPdfBytes = 25 * 1024 * 1024;
    [HttpPost] public async Task<IActionResult> GenerateDraftPdf(Guid tenantId, Guid jobId, int revisionNumber)
    {
        var json = Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
        try
        {
            if (!ModelState.IsValid || revisionNumber < 1) throw new BackendException(HttpStatusCode.BadRequest);
            var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
            var pdf = await api.PostAsync<PdfVersionView>("documents-pdf",
                $"{DocumentsPath(tenantId, jobId)}/{document.Id}/draft-revisions/{revisionNumber}/pdfs");
            if (json) return Ok(new { pdf });
            TempData["Notice"] = $"PDF پیش‌نویس نسخه‌ی {pdf.RevisionNumber} آماده شد؛ این فایل امضای معتبر ندارد.";
        }
        catch (BackendException error)
        {
            var message = error.Status switch
            {
                HttpStatusCode.UnprocessableEntity => "ساختار، چیدمان یا اندازه‌ی سند برای PDF پشتیبانی نمی‌شود؛ هیچ بخشی حذف یا ساده نشده است.",
                HttpStatusCode.TooManyRequests => "موتور PDF در حال کار است؛ کمی بعد دوباره امتحان کنید.",
                HttpStatusCode.ServiceUnavailable => "موتور PDF موقتاً در دسترس نیست؛ نصب موتور چاپ و وضعیت سرویس Documents را بررسی کنید.",
                _ => error.UserMessage
            };
            if (json) return StatusCode((int)error.Status, new { message });
            if (error.Status == HttpStatusCode.Unauthorized) throw;
            TempData["Notice"] = message;
        }
        return RedirectToAction(nameof(Editor), new { tenantId, jobId, revisionNumber });
    }
    [HttpGet] public async Task<IActionResult> DraftPdf(Guid tenantId, Guid jobId, Guid pdfId)
    {
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        var path = $"{DocumentsPath(tenantId, jobId)}/{document.Id}/pdfs";
        var versions = await api.ReadAsync<List<PdfVersionView>>("documents", path);
        var version = versions.SingleOrDefault(item => item.Id == pdfId);
        if (version is null) return NotFound();
        if (version.Kind != "DraftUnsigned" || version.SizeBytes is <= 0 or > MaximumPdfBytes) return BadRequest();
        using var response = await api.SendAsync("documents", HttpMethod.Get, $"{path}/{pdfId}", completion: HttpCompletionOption.ResponseHeadersRead);
        BackendApi.EnsureSuccess(response);
        if (response.Content.Headers.ContentType?.MediaType != "application/pdf") return BadRequest();
        await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        using var buffer = new MemoryStream(); var chunk = new byte[65536]; int count;
        while ((count = await stream.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
        {
            if (buffer.Length + count > MaximumPdfBytes) throw new BackendException(HttpStatusCode.BadGateway);
            await buffer.WriteAsync(chunk.AsMemory(0, count), HttpContext.RequestAborted);
        }
        var bytes = buffer.ToArray();
        if (bytes.LongLength != version.SizeBytes || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != version.Sha256)
            throw new BackendException(HttpStatusCode.BadGateway);
        Response.Headers["X-Content-SHA256"] = version.Sha256;
        Response.Headers["X-Document-Revision"] = version.RevisionNumber.ToString();
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; frame-ancestors 'none'";
        return File(bytes, "application/pdf", $"translation-draft-r{version.RevisionNumber}-{pdfId:N}.pdf", enableRangeProcessing: true);
    }
}
