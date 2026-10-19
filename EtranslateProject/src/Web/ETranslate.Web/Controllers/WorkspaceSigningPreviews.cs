using System.Net;
using System.Security.Cryptography;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    [HttpPost] public async Task<IActionResult> GenerateSigningPreview(Guid tenantId, Guid jobId, Guid preparationId)
    {
        try
        {
            if (!ModelState.IsValid || preparationId == Guid.Empty) throw new BackendException(HttpStatusCode.BadRequest);
            var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
            await api.PostAsync<SigningPreviewView>("documents-pdf",
                $"{DocumentsPath(tenantId, jobId)}/{document.Id}/signing-preparations/{preparationId}/preview-pdfs");
            TempData["Notice"] = UiText.Get("OnizlemePDFHazirReferansCeviriSayfalariVe");
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or
            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.PaymentRequired)
        {
            TempData["Notice"] = error.IsSubscriptionError ? error.UserMessage : error.Status switch
            {
                HttpStatusCode.Conflict => UiText.Get("TalepIptalEdildiVeyaOnayliPDFDegisti"),
                HttpStatusCode.UnprocessableEntity => UiText.Get("OnizlemePDFSayfaBoyutVeyaYapiSinirini"),
                HttpStatusCode.TooManyRequests => UiText.Get("PDFMotoruMesgulBirazSonraTekrarDeneyin"),
                _ => UiText.Get("OnizlemeOlusturmaSuAnKullanilamiyorHizmetlerinDurumunu")
            };
        }
        return RedirectToAction(nameof(SigningPreparation), new { tenantId, jobId });
    }
    [HttpGet] public async Task<IActionResult> SigningPreviewPdf(Guid tenantId, Guid jobId, Guid previewId)
    {
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        var path = $"{DocumentsPath(tenantId, jobId)}/{document.Id}/signing-previews";
        var versions = await api.ReadAsync<List<SigningPreviewView>>("documents", path);
        var preview = versions.SingleOrDefault(item => item.Id == previewId);
        if (preview is null) return NotFound();
        if (preview.Kind != "SigningPreviewUnsigned" || preview.SizeBytes is <= 0 or > MaximumPdfBytes) return BadRequest();
        using var response = await api.SendAsync("documents", HttpMethod.Get, $"{path}/{previewId}/file", completion: HttpCompletionOption.ResponseHeadersRead);
        BackendApi.EnsureSuccess(response);
        if (response.Content.Headers.ContentType?.MediaType != "application/pdf") throw new BackendException(HttpStatusCode.BadGateway);
        await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        using var buffer = new MemoryStream(); var chunk = new byte[65536]; int count;
        while ((count = await stream.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
        {
            if (buffer.Length + count > MaximumPdfBytes) throw new BackendException(HttpStatusCode.BadGateway);
            await buffer.WriteAsync(chunk.AsMemory(0, count), HttpContext.RequestAborted);
        }
        var bytes = buffer.ToArray();
        if (bytes.LongLength != preview.SizeBytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(preview.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new BackendException(HttpStatusCode.BadGateway);
        Response.Headers["X-Content-SHA256"] = preview.Sha256;
        Response.Headers["X-Source-PDF-SHA256"] = preview.SourcePdfSha256;
        Response.Headers["X-Artifact-Kind"] = preview.Kind;
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; frame-ancestors 'none'";
        return File(bytes, "application/pdf", $"signing-preview-UNSIGNED-{preview.Id:N}.pdf", enableRangeProcessing: true);
    }
}
