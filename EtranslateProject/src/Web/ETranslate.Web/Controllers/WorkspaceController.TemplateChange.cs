using System.Net;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class WorkspaceController
{
    [HttpGet] public async Task<IActionResult> DocumentTemplate(Guid tenantId, Guid jobId)
    {
        if (!ModelState.IsValid || tenantId == Guid.Empty || jobId == Guid.Empty) return BadRequest();
        var model = await LoadEditor(tenantId, jobId);
        if (model.Document is null || model.ExpectedRevision < 1) return RedirectToAction(nameof(Editor), new { tenantId, jobId });
        return View(model);
    }
    [HttpPost] public async Task<IActionResult> ChangeDocumentTemplate(Guid tenantId, Guid jobId, Guid templateId, int revisionNumber, int expectedRevision)
    {
        if (!ModelState.IsValid || new[] { tenantId, jobId, templateId }.Any(id => id == Guid.Empty) || revisionNumber < 1 || expectedRevision < 1) return BadRequest();
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        try
        {
            await api.PostAsync<DocumentView>("documents", $"{DocumentsPath(tenantId, jobId)}/{document.Id}/template-change",
                new { templateId, revisionNumber, expectedCurrentRevision = expectedRevision });
            TempData["Notice"] = UiText.Get("SablonKayitliMetninYeniBirSurumuneBaglandi");
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            TempData["Notice"] = error.Status == HttpStatusCode.Conflict ? UiText.Get("SablonDegisikligiDogrulanamadiSurumVeyaIncelemeDurumu") :
                UiText.Get("SablonDegistirilmediBilgilerGecersizVeyaMetindekiGorseller");
        }
        return RedirectToAction(nameof(DocumentTemplate), new { tenantId, jobId });
    }
}
