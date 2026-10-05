using System.Net;
using System.Net.Http.Headers;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    [HttpPost, RequestSizeLimit(27 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 27 * 1024 * 1024)]
    public async Task<IActionResult> UploadSource(Guid tenantId, Guid jobId, IFormFile? file)
    {
        var wantsJson = Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
        string? message = null;
        var status = 400;
        if (!ModelState.IsValid || file is null || !SourceFileRules.IsAllowed(file.ContentType, file.Length))
            message = "فایل باید PDF، PNG، JPEG یا TIFF و حداکثر ۲۵ مگابایت باشد.";
        else
        {
            try
            {
                var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
                using var form = new MultipartFormDataContent();
                await using var stream = file.OpenReadStream();
                var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                form.Add(content, "file", Path.GetFileName(file.FileName));
                using var response = await api.SendAsync("documents", HttpMethod.Post,
                    $"{DocumentsPath(tenantId, jobId)}/{document.Id}/source-files", form);
                BackendApi.EnsureSuccess(response);
                var source = (await response.Content.ReadFromJsonAsync<SourceFileView>(HttpContext.RequestAborted))!;
                if (wantsJson) return Ok(new { sourceFile = source });
                TempData["Notice"] = "مدرک مبدأ بارگذاری شد؛ متن ترجمه تغییر نکرده است.";
                return RedirectToAction(nameof(Editor), new { tenantId, jobId });
            }
            catch (BackendException error)
            {
                message = error.Status == HttpStatusCode.BadRequest
                    ? "فایل پذیرفته نشد؛ نوع واقعی فایل، اندازه و فرمت آن را بررسی کنید." : error.UserMessage;
                status = (int)error.Status;
            }
        }
        if (wantsJson) return StatusCode(status, new { message });
        TempData["Notice"] = message;
        return RedirectToAction(nameof(Editor), new { tenantId, jobId });
    }

    [HttpGet] public async Task<IActionResult> Source(Guid tenantId, Guid jobId, Guid sourceFileId, bool download = false)
    {
        // Derive document ID and filename from tenant-scoped metadata, never from untrusted query paths.
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        var source = document.SourceFiles?.SingleOrDefault(item => item.Id == sourceFileId);
        if (source is null) return NotFound();
        if (!SourceFileRules.IsAllowed(source.ContentType, source.SizeBytes)) return BadRequest();
        using var response = await api.SendAsync("documents", HttpMethod.Get,
            $"{DocumentsPath(tenantId, jobId)}/{document.Id}/source-files/{sourceFileId}",
            completion: HttpCompletionOption.ResponseHeadersRead);
        BackendApi.EnsureSuccess(response);
        if (response.Content.Headers.ContentType?.MediaType != source.ContentType) return BadRequest();
        var buffer = new MemoryStream();
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            var chunk = new byte[64 * 1024];
            int count;
            while ((count = await input.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
            {
                if (buffer.Length + count > SourceFileRules.MaximumBytes)
                    throw new BackendException(HttpStatusCode.BadGateway);
                await buffer.WriteAsync(chunk.AsMemory(0, count), HttpContext.RequestAborted);
            }
            if (buffer.Length != source.SizeBytes) throw new BackendException(HttpStatusCode.BadGateway);
            buffer.Position = 0;
            Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; frame-ancestors 'none'";
            return File(buffer, source.ContentType, download ? Path.GetFileName(source.OriginalFileName) : null,
                enableRangeProcessing: true);
        }
        catch { await buffer.DisposeAsync(); throw; }
    }
}
