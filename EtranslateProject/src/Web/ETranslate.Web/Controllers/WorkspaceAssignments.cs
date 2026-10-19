using System.Net;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    [HttpGet] public async Task<IActionResult> Assignment(Guid tenantId, Guid jobId)
    {
        var state = await api.ReadAsync<AssignmentStateView>("translation-workflow", $"{JobsPath(tenantId)}/{jobId}/assignment");
        return View(new AssignmentPageView
        {
            TenantId = tenantId, Assignment = state,
            WriteStatus = await WriteStatusAsync(tenantId),
            Job = await api.ReadAsync<JobView>("translation-workflow", $"{JobsPath(tenantId)}/{jobId}"),
            Candidates = state.CanManage ? await api.ReadAsync<List<AssigneeView>>("translation-workflow", $"{TenantPath(tenantId)}/translation-assignees") : []
        });
    }

    [HttpPost] public async Task<IActionResult> AssignTranslator(Guid tenantId, Guid jobId, Guid? translatorUserId,
        long expectedAssignmentVersion, string? note)
    {
        try
        {
            if (!ModelState.IsValid || translatorUserId == Guid.Empty || expectedAssignmentVersion < 0 || note?.Length > 500)
                throw new BackendException(HttpStatusCode.BadRequest);
            using var response = await api.SendAsync("translation-workflow", HttpMethod.Put, $"{JobsPath(tenantId)}/{jobId}/assignment",
                JsonContent.Create(new { translatorUserId, expectedAssignmentVersion, note }));
            BackendApi.EnsureSuccess(response);
            TempData["Notice"] = UiText.Get("CevirmenAtamasiKaydedildiCeviriSurumleriVeInceleme");
        }
        catch (BackendException error) when (error.Status != HttpStatusCode.Unauthorized)
        {
            TempData["Notice"] = error.Status switch
            {
                HttpStatusCode.Conflict => UiText.Get("AtamaDegistiVeyaSeciminizZatenKayitliGuncel"),
                HttpStatusCode.BadRequest => UiText.Get("UyeEtkinOlmaliYazmaYetkisiBulunmaliVe"),
                _ => error.UserMessage
            };
        }
        return RedirectToAction(nameof(Assignment), new { tenantId, jobId });
    }
}
