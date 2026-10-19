using System.Net;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    private async Task<(SigningPageView Page, string Path)> LoadSigningPreparation(Guid tenantId, Guid jobId)
    {
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        var path = $"{DocumentsPath(tenantId, jobId)}/{document.Id}/signing-preparations";
        var state = await api.ReadAsync<SigningStateView>("trust", path);
        var job = await api.ReadAsync<JobView>("translation-workflow", $"{JobsPath(tenantId)}/{jobId}");
        return (new SigningPageView
        {
            TenantId = tenantId, Job = job, State = state,
            WriteStatus = await WriteStatusAsync(tenantId),
            Previews = await api.ReadAsync<List<SigningPreviewView>>("documents", $"{DocumentsPath(tenantId, jobId)}/{document.Id}/signing-previews"),
            Candidates = state.CanManage ? await api.ReadAsync<List<AssigneeView>>("identity-access", $"{TenantPath(tenantId)}/translation-assignees") : [],
            Form = new()
            {
                ExpectedReviewId = state.Artifact?.ReviewId ?? Guid.Empty, ExpectedPdfVersionId = state.Artifact?.PdfVersionId ?? Guid.Empty,
                TranslatorUserId = job.AssignedTranslatorUserId ?? Guid.Empty,
                OfficeMethod = state.SignaturePolicy == "TranslatorAndTranslationOffice" ? "EImza" : null
            }
        }, path);
    }
    [HttpGet] public async Task<IActionResult> SigningPreparation(Guid tenantId, Guid jobId)
        => View((await LoadSigningPreparation(tenantId, jobId)).Page);

    [HttpPost, RequestSizeLimit(200_000)]
    public async Task<IActionResult> PrepareSigning(Guid tenantId, Guid jobId, [Bind(Prefix = "Form")] SigningProposalForm form)
    {
        var (page, path) = await LoadSigningPreparation(tenantId, jobId);
        if (!page.State.CanManage) throw new BackendException(HttpStatusCode.Forbidden);
        try
        {
            if (!ModelState.IsValid) throw new BackendException(HttpStatusCode.BadRequest);
            using var response = await api.SendAsync("trust", HttpMethod.Post, path, JsonContent.Create(form));
            BackendApi.EnsureSuccess(response);
            TempData["Notice"] = UiText.Get("TalepPlaniKaydedildiImzaVeyaImzalayaninOnayi");
            return RedirectToAction(nameof(SigningPreparation), new { tenantId, jobId });
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.PaymentRequired || error.IsSubscriptionError)
        {
            page.Form = form;
            page.Error = error.IsSubscriptionError ? error.UserMessage : error.Status == HttpStatusCode.Conflict
                ? UiText.Get("OnayliSurumVeyaEtkinTalepDegistiGirilen")
                : UiText.Get("ImzalayanBuCalismaAlanininEtkinVeYetkili");
            Response.StatusCode = (int)error.Status;
            return View(nameof(SigningPreparation), page);
        }
    }
    [HttpPost] public async Task<IActionResult> CancelSigningPreparation(Guid tenantId, Guid jobId, Guid preparationId, long expectedVersion, string reason)
    {
        var (page, path) = await LoadSigningPreparation(tenantId, jobId);
        if (!page.State.CanManage) throw new BackendException(HttpStatusCode.Forbidden);
        try
        {
            if (!ModelState.IsValid) throw new BackendException(HttpStatusCode.BadRequest);
            using var response = await api.SendAsync("trust", HttpMethod.Post, $"{path}/{preparationId}/cancel", JsonContent.Create(new { expectedVersion, reason }));
            BackendApi.EnsureSuccess(response);
            TempData["Notice"] = UiText.Get("HazirlikIptalEdildiPDFVeOnerilenBeyanlarin");
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        { TempData["Notice"] = error.Status == HttpStatusCode.Conflict ? UiText.Get("TalepDahaOnceDegistiGuncelDurumuKontrol") : UiText.Get("IptalGerekcesiZorunludurVeEnFazla2000"); }
        return RedirectToAction(nameof(SigningPreparation), new { tenantId, jobId });
    }
}
