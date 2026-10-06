using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Web.Models;
using ETranslate.Contracts.Documents;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

[Authorize]
public sealed partial class WorkspaceController(BackendApi api) : Controller
{
    private static string TenantPath(Guid tenantId) => $"/api/v1/tenants/{tenantId}";
    private static string JobsPath(Guid tenantId) => $"{TenantPath(tenantId)}/translation-jobs";
    private static string TemplatesPath(Guid tenantId) => $"{TenantPath(tenantId)}/document-templates";
    private static string DocumentsPath(Guid tenantId, Guid jobId) => $"{JobsPath(tenantId)}/{jobId}/documents";

    [HttpGet] public async Task<IActionResult> Index(Guid? tenantId)
    {
        var tenants = await api.ReadAsync<List<TenantView>>("identity-access", "/api/v1/tenants");
        var tenant = tenantId.HasValue ? tenants.Find(item => item.Id == tenantId) : tenants.FirstOrDefault();
        if (tenantId.HasValue && tenant is null) return NotFound();
        return View(new WorkspaceView
        {
            Tenants = tenants, Tenant = tenant,
            Jobs = tenant is null ? [] : await api.ReadAsync<List<JobView>>("translation-workflow", $"{JobsPath(tenant.Id)}?take=100"),
            Templates = tenant is null ? [] : await api.ReadAsync<List<TemplateView>>("documents", TemplatesPath(tenant.Id))
        });
    }

    [HttpPost] public async Task<IActionResult> CreateTenant(string name, string slug, string type)
    {
        try
        {
            var tenant = await api.PostAsync<TenantView>("identity-access", "/api/v1/tenants", new { name, slug, type });
            TempData["Notice"] = "فضای کاری ساخته شد؛ اشتراک آزمایشی توسط سرویس اشتراک فعال می‌شود.";
            return RedirectToAction(nameof(Index), new { tenantId = tenant.Id });
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        { TempData["Notice"] = error.UserMessage; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost] public async Task<IActionResult> CreateJob(Guid tenantId, string title, string sourceLanguageCode,
        string targetLanguageCode, string notaryRequirement, string? notaryProcessingMode, string? acceptanceProfile,
        string? acceptanceProfileOther)
    {
        try
        {
            var job = await api.PostAsync<JobView>("translation-workflow", JobsPath(tenantId), new
            {
                title, sourceLanguageCode, targetLanguageCode, notaryRequirement,
                notaryProcessingMode = notaryRequirement == "Required" ? notaryProcessingMode : null,
                acceptanceProfile = string.IsNullOrWhiteSpace(acceptanceProfile) ? null : acceptanceProfile,
                acceptanceProfileOther = acceptanceProfile == "Other" ? acceptanceProfileOther : null
            });
            return RedirectToAction(nameof(Editor), new { tenantId, jobId = job.Id });
        }
        catch (BackendException error) when (error.Status == HttpStatusCode.BadRequest)
        { TempData["Notice"] = error.UserMessage; return RedirectToAction(nameof(Index), new { tenantId }); }
    }

    private async Task<EditorView> LoadEditor(Guid tenantId, Guid jobId, int? revisionNumber = null)
    {
        var model = new EditorView
        {
            TenantId = tenantId,
            Job = await api.ReadAsync<JobView>("translation-workflow", $"{JobsPath(tenantId)}/{jobId}"),
            Document = await api.FindAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId)),
            Templates = await api.ReadAsync<List<TemplateView>>("documents", TemplatesPath(tenantId))
        };
        if (model.Document is { } document)
        {
            model.ExpectedRevision = document.CurrentDraftRevision;
            var path = $"{DocumentsPath(tenantId, jobId)}/{document.Id}";
            model.History = await api.ReadAsync<List<DraftSummary>>("documents", $"{path}/draft-revisions");
            model.PdfVersions = await api.ReadAsync<List<PdfVersionView>>("documents", $"{path}/pdfs");
            if (document.TemplateRevisionId is not null)
                model.Template = await api.ReadAsync<TemplateRevision>("documents", $"{path}/template");
            if (revisionNumber.HasValue || document.CurrentDraftRevision > 0)
            {
                var number = revisionNumber ?? document.CurrentDraftRevision;
                model.ViewedRevision = number;
                var draft = await api.ReadAsync<DraftView>("documents", $"{path}/draft-revisions/{number}");
                model.EditorContentJson = draft.EditorContentJson;
                model.Historical = number != document.CurrentDraftRevision;
            }
        }
        return model;
    }
    [HttpGet] public async Task<IActionResult> Editor(Guid tenantId, Guid jobId, int? revisionNumber)
        => View(await LoadEditor(tenantId, jobId, revisionNumber));

    [HttpPost] public async Task<IActionResult> StartDocument(Guid tenantId, Guid jobId)
    {
        try { await api.PostAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId)); }
        catch (BackendException error) when (error.Status == HttpStatusCode.Conflict)
        { TempData["Notice"] = "سند قبلاً ایجاد شده است."; }
        return RedirectToAction(nameof(Editor), new { tenantId, jobId });
    }

    [HttpPost] public async Task<IActionResult> ApplyTemplate(Guid tenantId, Guid jobId, Guid templateId, int revisionNumber)
    {
        var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
        try
        {
            await api.PostAsync<DocumentView>("documents", $"{DocumentsPath(tenantId, jobId)}/{document.Id}/template",
                new { templateId, revisionNumber });
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        { TempData["Notice"] = error.UserMessage; }
        return RedirectToAction(nameof(Editor), new { tenantId, jobId });
    }

    [HttpPost, RequestSizeLimit(4_500_000)]
    public async Task<IActionResult> SaveDraft(Guid tenantId, Guid jobId, int expectedRevision,
        string? plainText, string? editorContentJson, string mode)
    {
        string json = editorContentJson ?? "";
        string? message = null;
        var conflict = false;
        try
        {
            if (!ModelState.IsValid) throw new BackendException(HttpStatusCode.BadRequest);
            var document = await api.ReadAsync<DocumentView>("documents", DocumentsPath(tenantId, jobId));
            // Check the immutable revision too, not a browser-supplied assertion of editability.
            if (mode == "text")
            {
                if (expectedRevision > 0)
                {
                    var original = await api.ReadAsync<DraftView>("documents",
                        $"{DocumentsPath(tenantId, jobId)}/{document.Id}/draft-revisions/{expectedRevision}");
                    if (DocumentText.TryPlainText(original.EditorContentJson) is null)
                        throw new BackendException(HttpStatusCode.BadRequest);
                }
                json = DocumentText.FromPlainText(plainText ?? "");
            }
            else if (mode == "rich")
            {
                if (!RichDocument.TryGetPlainText(json, out _)) throw new BackendException(HttpStatusCode.BadRequest);
            }
            else if (mode != "json") throw new BackendException(HttpStatusCode.BadRequest);
            await api.PostAsync<DraftView>("documents", $"{DocumentsPath(tenantId, jobId)}/{document.Id}/draft-revisions", new
            { expectedCurrentRevision = expectedRevision, editorContentJson = json,
                plainText = RichDocument.TryGetPlainText(json, out var extracted) ? extracted : DocumentText.TryPlainText(json) });
            TempData["Notice"] = "نسخه‌ی جدید ذخیره شد. نسخه‌های قبلی بدون تغییر باقی می‌مانند.";
            return RedirectToAction(nameof(Editor), new { tenantId, jobId });
        }
        catch (BackendException error) when (error.Status != HttpStatusCode.Unauthorized)
        {
            conflict = error.Status == HttpStatusCode.Conflict;
            message = conflict ? "نسخه‌ی دیگری ذخیره شده است. متن شما حفظ شده؛ آن را کپی کنید و آخرین نسخه را در تب دیگری باز کنید تا تغییرات را ادغام کنید." : error.UserMessage;
            if (mode == "text") json = DocumentText.FromPlainText(plainText ?? "");
        }
        var model = await LoadEditor(tenantId, jobId);
        model.EditorContentJson = json;
        model.ExpectedRevision = expectedRevision;
        model.Conflict = conflict;
        model.Message = message;
        ModelState.Clear();
        Response.StatusCode = conflict ? 409 : 400;
        return View("Editor", model);
    }

    [HttpPost] public async Task<IActionResult> CreateTemplate(Guid tenantId, string name, string? header,
        string? footer, string? starterText, string? watermark, string pageSize = "A4", string orientation = "Portrait",
        decimal marginTop = 25, decimal marginRight = 20, decimal marginBottom = 20, decimal marginLeft = 20)
    {
        var layout = new DocumentPageLayout(pageSize, orientation, marginTop, marginRight, marginBottom, marginLeft);
        if (!ModelState.IsValid || !layout.IsValid)
        { TempData["Notice"] = "اندازه، جهت یا حاشیه‌های صفحه معتبر نیست؛ حاشیه‌ها باید بین ۵ و ۵۰ میلی‌متر باشند."; return RedirectToAction(nameof(Index), new { tenantId }); }
        try
        {
            await api.PostAsync<TemplateDetail>("documents", TemplatesPath(tenantId), new
            {
                name, description = (string?)null,
                editorContentJson = DocumentText.FromPlainText(starterText ?? ""),
                headerContentJson = DocumentText.FromPlainText(header ?? ""),
                footerContentJson = DocumentText.FromPlainText(footer ?? ""),
                pageLayoutJson = layout.ToJson(),
                watermarkJson = string.IsNullOrWhiteSpace(watermark) ? null : JsonSerializer.Serialize(new { text = watermark, opacity = 0.12, rotation = -35 })
            });
            TempData["Notice"] = "قالب ساخته شد؛ اکنون می‌توانید لوگو را به یک نسخه‌ی جدید اضافه کنید.";
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        { TempData["Notice"] = error.UserMessage; }
        return RedirectToAction(nameof(Index), new { tenantId });
    }

    [HttpPost, RequestSizeLimit(5_500_000)]
    public async Task<IActionResult> AddLogo(Guid tenantId, Guid templateId, IFormFile? file)
    {
        if (file is null || file.Length is <= 0 or > 5 * 1024 * 1024 ||
            file.ContentType is not ("image/png" or "image/jpeg"))
        { TempData["Notice"] = "لوگو باید PNG یا JPEG و حداکثر ۵ مگابایت باشد."; return RedirectToAction(nameof(Index), new { tenantId }); }
        var path = $"{TemplatesPath(tenantId)}/{templateId}";
        var template = await api.ReadAsync<TemplateDetail>("documents", path);
        var header = JsonNode.Parse(template.Revision.HeaderContentJson ?? DocumentText.Empty) as JsonObject;
        if (header?["content"] is not JsonArray)
        { TempData["Notice"] = "ساختار سربرگ این قالب در رابط ساده پشتیبانی نمی‌شود؛ از API استفاده کنید."; return RedirectToAction(nameof(Index), new { tenantId }); }
        try
        {
            using var form = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            form.Add(content, "file", Path.GetFileName(file.FileName));
            using var response = await api.SendAsync("documents", HttpMethod.Post, $"{path}/assets", form);
            BackendApi.EnsureSuccess(response);
            var asset = (await response.Content.ReadFromJsonAsync<AssetView>())!;
            ((JsonArray)header!["content"]!).Add(JsonSerializer.SerializeToNode(new { type = "image", attrs = new { assetId = asset.Id } }));
            await api.PostAsync<TemplateRevision>("documents", $"{path}/revisions", new
            {
                expectedCurrentRevision = template.CurrentRevision, template.Revision.EditorContentJson,
                headerContentJson = header.ToJsonString(), template.Revision.FooterContentJson,
                template.Revision.PageLayoutJson, template.Revision.WatermarkJson
            });
            TempData["Notice"] = "نسخه‌ی جدید قالب با لوگو ساخته شد. اسناد قبلی تغییر نمی‌کنند.";
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        { TempData["Notice"] = $"{error.UserMessage} اگر تصویر بارگذاری شده باشد، در دارایی‌های قالب باقی می‌ماند."; }
        return RedirectToAction(nameof(Index), new { tenantId });
    }

    [HttpGet] public async Task<IActionResult> Asset(Guid tenantId, Guid templateId, Guid assetId)
    {
        using var response = await api.SendAsync("documents", HttpMethod.Get, $"{TemplatesPath(tenantId)}/{templateId}/assets/{assetId}");
        BackendApi.EnsureSuccess(response);
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/png" or "image/jpeg")) return BadRequest();
        return File(await response.Content.ReadAsByteArrayAsync(HttpContext.RequestAborted), type);
    }
}
