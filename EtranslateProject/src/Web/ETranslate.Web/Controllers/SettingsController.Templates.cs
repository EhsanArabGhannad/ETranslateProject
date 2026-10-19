using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETranslate.SubscriptionAccess;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

public sealed partial class SettingsController
{
    private static string TemplatePath(Guid tenantId, Guid? templateId = null)
        => $"/api/v1/tenants/{tenantId}/document-templates" + (templateId.HasValue ? $"/{templateId}" : "");
    private async Task<TemplateDetail> ReadTemplate(Guid tenantId, Guid templateId)
    {
        var detail = await api.ReadAsync<TemplateDetail>("documents", TemplatePath(tenantId, templateId));
        if (detail is null || detail.Id != templateId || detail.Revision is null || detail.Revision.TemplateId != templateId ||
            detail.CurrentRevision < 1 || detail.Revision.RevisionNumber != detail.CurrentRevision || detail.Revision.Id == Guid.Empty)
            throw new BackendException(HttpStatusCode.BadGateway);
        return detail;
    }

    [HttpPost, RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> SaveTemplate([Bind(Prefix = "Form")] TemplateEditForm form, IFormFile? logo)
    {
        if (form.TenantId == Guid.Empty || form.TemplateId == Guid.Empty || form.PendingAssetId == Guid.Empty) return BadRequest();
        var page = await LoadTemplatesAsync(form.TenantId, form.TemplateId);
        if (page?.Tenant is null) return NotFound();
        page.Form = form;
        if (page.Tenant.Role is not ("Owner" or "Administrator" or "Translator")) return StatusCode(403);
        if (!page.CanWrite)
        {
            Response.StatusCode = page.WriteStatus == SubscriptionWriteStatus.Required ? 402 : 503;
            page.Message = UiText.Get("SablonDegisikligiIcinGecerliAbonelikVeErisilebilir");
            return View("Templates", page);
        }
        if (page.Selected?.IsActive == false) return StatusCode(409);
        if (form.TemplateId.HasValue && (form.ExpectedRevision < 1 || form.ExpectedRevision != page.Selected!.CurrentRevision))
            return SaveFailure(page, 409, UiText.Get("SablonSurumuDegistiFormBilgilerinizKorunduSon"), conflict: true);
        if (!form.TemplateId.HasValue && (form.ExpectedRevision != 0 || form.PendingAssetId.HasValue)) return BadRequest();
        if (page.Projection.LayoutEditable && !form.Layout.IsValid) ModelState.AddModelError("Form.PageSize", UiText.Get("SayfaVeyaAltbilgiKonumuAyarlariGecersizKenar"));
        if (form.LogoMode is not ("Add" or "Replace")) ModelState.AddModelError("Form.LogoMode", UiText.Get("GorselEklemeYontemiGecersiz"));
        if (page.Projection.HeaderEditable && (!TemplateRegion.AlignmentsValid(form.HeaderLineAlignmentsJson) || form.ImageAlignment is not (null or "left" or "center" or "right") || form.ImageLayout is not (null or "logo" or "banner")))
            ModelState.AddModelError("Form.Header", UiText.Get("MetinHizalamasiVeyaUstbilgiGorselDuzeniGecersiz"));
        if (page.Projection.FooterEditable && !TemplateRegion.AlignmentsValid(form.FooterLineAlignmentsJson)) ModelState.AddModelError("Form.Footer", UiText.Get("AltbilgiHizalamasiGecersiz"));
        var hasLogo = logo is { Length: > 0 };
        var mime = logo?.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (logo is not null && (!hasLogo || logo.Length > 5 * 1024 * 1024 || mime is not ("image/png" or "image/jpeg")))
            ModelState.AddModelError("logo", UiText.Get("GorselPNGVeyaJPEGBicimindeVeEn"));
        if ((hasLogo || form.PendingAssetId.HasValue) && (!page.Projection.CanAppendLogo || form.LogoMode == "Replace" && !page.Projection.HeaderEditable))
            ModelState.AddModelError("logo", UiText.Get("KarmasikUstbilgiYapisiBuGorselDegisikliginiDesteklemiyor"));
        if (!ModelState.IsValid) return SaveFailure(page, 400, UiText.Get("FormBilgileriniKontrolEdinDosyaSectiysenizYeniden"));

        var created = false; var writeAttempted = false;
        try
        {
            if (!form.TemplateId.HasValue)
            {
                var content = page.Projection.Build(form, null, null);
                writeAttempted = true;
                var detail = await api.PostAsync<TemplateDetail>("documents", TemplatePath(form.TenantId), new
                { form.Name, description = (string?)null, content.EditorContentJson, content.HeaderContentJson, content.FooterContentJson, content.PageLayoutJson, content.WatermarkJson });
                if (detail is null || detail.Id == Guid.Empty || detail.Revision is null || detail.Revision.TemplateId != detail.Id || detail.CurrentRevision != 1 || detail.Revision.RevisionNumber != 1)
                    throw new BackendException(HttpStatusCode.BadGateway);
                created = true; page.Selected = detail; form.TemplateId = detail.Id; form.ExpectedRevision = 1; form.Name = detail.Name;
                page.Templates.Insert(0, new(detail.Id, detail.Name, true, 1));
                page.Projection = TemplateProjection.From(detail, form.TenantId, out _);
                // Subsequent failures render an EDIT form for this created ID, never another create.
                ModelState.Clear();
                if (!hasLogo) return Saved(form, UiText.Get("SablonOlusturulduSurum1Kaydedildi"));
            }

            Guid? imageId = form.PendingAssetId;
            if (hasLogo)
            {
                using var upload = new MultipartFormDataContent();
                await using var stream = logo!.OpenReadStream();
                var bytes = new StreamContent(stream); bytes.Headers.ContentType = new MediaTypeHeaderValue(mime!);
                upload.Add(bytes, "file", Path.GetFileName(logo.FileName));
                writeAttempted = true;
                using var response = await api.SendAsync("documents", HttpMethod.Post, $"{TemplatePath(form.TenantId, form.TemplateId)}/assets", upload);
                BackendApi.EnsureSuccess(response);
                var asset = await response.Content.ReadFromJsonAsync<AssetView>(HttpContext.RequestAborted);
                if (asset is null || asset.Id == Guid.Empty) throw new BackendException(HttpStatusCode.BadGateway);
                imageId = asset.Id; form.PendingAssetId = asset.Id;
                ModelState.Clear();
            }
            else if (imageId.HasValue)
            {
                var assets = await api.ReadAsync<List<AssetView>>("documents", $"{TemplatePath(form.TenantId, form.TemplateId)}/assets");
                if (assets is null || !assets.Any(a => a.Id == imageId)) return SaveFailure(page, 400, UiText.Get("KurtarilanGorselBuSablonaAitDegil"));
            }
            var payload = page.Projection.Build(form, page.Selected!.Revision, imageId);
            writeAttempted = true;
            var saved = await api.PostAsync<TemplateRevision>("documents", $"{TemplatePath(form.TenantId, form.TemplateId)}/revisions", new
            { expectedCurrentRevision = form.ExpectedRevision, payload.EditorContentJson, payload.HeaderContentJson, payload.FooterContentJson, payload.PageLayoutJson, payload.WatermarkJson });
            if (saved is null || saved.TemplateId != form.TemplateId || saved.RevisionNumber != form.ExpectedRevision + 1) throw new BackendException(HttpStatusCode.BadGateway);
            return Saved(form, UiText.Get("YeniSablonSurumuKaydedildiOncekiBelgelerVe"));
        }
        catch (BackendException error)
        {
            if (error.Status == HttpStatusCode.Unauthorized) throw;
            var prefix = created ? UiText.Get("SablonOlusturulduAncakGorselEklemeTamamlanmadiAyni") : UiText.Get("KaydinTamamlandigiDogrulanamadiFormBilgileriKorunmustur");
            if (writeAttempted) prefix += UiText.Get("TekrarDenemedenOnceSonSurumuKontrolEdin");
            ModelState.Clear();
            return SaveFailure(page, (int)error.Status, prefix + error.UserMessage, error.Status == HttpStatusCode.Conflict);
        }
    }
    private IActionResult SaveFailure(SettingsPageView page, int status, string message, bool conflict = false)
    { Response.StatusCode = status; page.Message = message; page.Conflict = conflict; return View("Templates", page); }
    private IActionResult Saved(TemplateEditForm form, string message)
    {
        TempData["Notice"] = message;
        return RedirectToAction(nameof(Templates), new { tenantId = form.TenantId, templateId = form.TemplateId });
    }
}
