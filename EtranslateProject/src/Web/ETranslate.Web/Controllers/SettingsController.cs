using System.Net;
using ETranslate.SubscriptionAccess;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

[Authorize]
public sealed partial class SettingsController(BackendApi api, SubscriptionWriteClient subscriptions) : Controller
{
    private async Task<SettingsPageView?> LoadAsync(Guid? tenantId)
    {
        // Live membership list only; never carry a tenant or role from cookies/browser state.
        Response.Headers.CacheControl = "private, no-store";
        var tenants = await api.ReadAsync<List<TenantView>>("identity-access", "/api/v1/tenants");
        if (tenants is null || tenants.Any(t => t is null || t.Id == Guid.Empty) || tenants.Select(t => t.Id).Distinct().Count() != tenants.Count)
            throw new BackendException(HttpStatusCode.BadGateway);
        var tenant = tenantId.HasValue ? tenants.Find(t => t.Id == tenantId) : tenants.FirstOrDefault();
        return tenantId.HasValue && tenant is null ? null : new() { Tenants = tenants, Tenant = tenant };
    }

    [HttpGet] public async Task<IActionResult> Index(Guid? tenantId)
    {
        if (!ModelState.IsValid || tenantId == Guid.Empty) return BadRequest();
        var model = await LoadAsync(tenantId);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet] public async Task<IActionResult> Templates(Guid? tenantId, Guid? templateId = null, Guid? pendingAssetId = null)
    {
        if (!ModelState.IsValid || tenantId == Guid.Empty || templateId == Guid.Empty || pendingAssetId == Guid.Empty || pendingAssetId.HasValue && !templateId.HasValue) return BadRequest();
        var model = await LoadTemplatesAsync(tenantId, templateId);
        if (model?.Selected is not null && pendingAssetId.HasValue)
        {
            var assets = await api.ReadAsync<List<AssetView>>("documents", $"{TemplatePath(model.Tenant!.Id, templateId)}/assets");
            if (assets is null) throw new BackendException(HttpStatusCode.BadGateway);
            if (!assets.Any(a => a.Id == pendingAssetId)) return NotFound();
            model.Form.PendingAssetId = pendingAssetId;
        }
        return model is null ? NotFound() : View(model);
    }

    private async Task<SettingsPageView?> LoadTemplatesAsync(Guid? tenantId, Guid? templateId)
    {
        var model = await LoadAsync(tenantId);
        if (model is null) return null;
        if (model.Tenant is { } tenant)
        {
            var token = await HttpContext.GetTokenAsync("access_token");
            model.WriteStatus = await subscriptions.CheckAsync(tenant.Id, token is null ? null : $"Bearer {token}", HttpContext.RequestAborted);
            if (model.WriteStatus == SubscriptionWriteStatus.Unauthorized) throw new BackendException(HttpStatusCode.Unauthorized);
            if (model.WriteStatus == SubscriptionWriteStatus.Forbidden) throw new BackendException(HttpStatusCode.Forbidden);
            model.Templates = await api.ReadAsync<List<TemplateView>>("documents", $"/api/v1/tenants/{tenant.Id}/document-templates");
            if (templateId.HasValue) model.Selected = await ReadTemplate(tenant.Id, templateId.Value);
            model.Projection = TemplateProjection.From(model.Selected, tenant.Id, out var form); model.Form = form;
        }
        return model;
    }
}
