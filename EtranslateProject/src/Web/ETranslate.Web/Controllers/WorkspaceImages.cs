using System.Net;
using System.Net.Http.Headers;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    [HttpPost, RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadBodyImage(Guid tenantId, Guid jobId, int expectedRevision, IFormFile? file)
    {
        if (!ModelState.IsValid || file is null || file.ContentType is not ("image/png" or "image/jpeg") || file.Length is <= 0 or > 5 * 1024 * 1024 || expectedRevision < 0)
            return BadRequest(new { message = UiText.Get("EditorImageInvalidFile") });
        try
        {
            var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
            using var form = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            var content = new StreamContent(stream); content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(content, "file", Path.GetFileName(file.FileName));
            using var response = await api.SendAsync("documents", HttpMethod.Post, $"{DocumentsPath(tenantId, jobId)}/{document.Id}/images?expectedRevision={expectedRevision}", form);
            BackendApi.EnsureSuccess(response);
            var image = (await response.Content.ReadFromJsonAsync<AssetView>(HttpContext.RequestAborted))!;
            Response.Headers.CacheControl = "private, no-store";
            return Ok(new { image.Id });
        }
        catch (BackendException error) { return StatusCode((int)error.Status, new { message = error.Status == HttpStatusCode.BadRequest ? UiText.Get("EditorImageInvalidFile") : error.UserMessage }); }
    }

    [HttpGet]
    public async Task<IActionResult> BodyImage(Guid tenantId, Guid jobId, Guid assetId)
    {
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        using var response = await api.SendAsync("documents", HttpMethod.Get, $"{DocumentsPath(tenantId, jobId)}/{document.Id}/images/{assetId}", completion: HttpCompletionOption.ResponseHeadersRead);
        BackendApi.EnsureSuccess(response);
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/png" or "image/jpeg")) return BadRequest();
        await using var input = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        var buffer = new MemoryStream(); var chunk = new byte[64 * 1024];
        try
        {
            int count;
            while ((count = await input.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
            {
                if (buffer.Length + count > 5 * 1024 * 1024) throw new BackendException(HttpStatusCode.BadGateway);
                await buffer.WriteAsync(chunk.AsMemory(0, count), HttpContext.RequestAborted);
            }
            buffer.Position = 0;
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; frame-ancestors 'none'";
            return File(buffer, type);
        }
        catch { await buffer.DisposeAsync(); throw; }
    }
}
