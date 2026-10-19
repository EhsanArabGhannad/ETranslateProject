using System.Net;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

[Authorize]
public sealed class PurchaseOrdersController(BackendApi api) : Controller
{
    private static string Path(Guid tenantId) => $"/api/v1/tenants/{tenantId}/subscription/orders";
    private async Task<TenantView> Manager(Guid tenantId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.OnStarting(() => { Response.Headers.CacheControl = "private, no-store"; return Task.CompletedTask; });
        var tenant = await api.ReadAsync<TenantView>("identity-access", $"/api/v1/tenants/{tenantId}");
        if (tenant.Id != tenantId) throw new BackendException(HttpStatusCode.BadGateway);
        if (tenant.Role is not ("Owner" or "Administrator")) throw new BackendException(HttpStatusCode.Forbidden);
        return tenant;
    }
    [HttpGet] public async Task<IActionResult> Index(Guid tenantId)
    {
        if (tenantId == Guid.Empty) return BadRequest();
        var tenant = await Manager(tenantId);
        var catalog = await api.ReadAsync<PurchasePlanCatalogView>("billing", $"/api/v1/tenants/{tenantId}/subscription/plans");
        var orders = await api.ReadAsync<List<PurchaseOrderView>>("billing", Path(tenantId));
        if (catalog?.IsValidFor(tenant) != true || orders is null || orders.Count > 50 ||
            orders.Any(o => o?.IsValidFor(tenantId) != true) || orders.Select(o => o.Id).Distinct().Count() != orders.Count)
            throw new BackendException(HttpStatusCode.BadGateway);
        return View(new PurchaseOrdersPageView { Tenant = tenant, Catalog = catalog, Orders = orders });
    }
    [HttpPost] public async Task<IActionResult> Create(Guid tenantId, string planCode, Guid requestId)
    {
        _ = await Manager(tenantId);
        if (!ModelState.IsValid || requestId == Guid.Empty || string.IsNullOrWhiteSpace(planCode) || planCode.Length > 80) return BadRequest();
        var order = await api.PostAsync<PurchaseOrderView>("billing", Path(tenantId), new { planCode, requestId });
        if (order?.IsValidFor(tenantId) != true) throw new BackendException(HttpStatusCode.BadGateway);
        TempData["Notice"] = UiText.Get("OnSiparisKaydedildiVeyaMevcutKayitGetirildi");
        return RedirectToAction(nameof(Index), new { tenantId });
    }
    [HttpPost] public async Task<IActionResult> Cancel(Guid tenantId, Guid orderId, long expectedVersion)
    {
        _ = await Manager(tenantId);
        if (!ModelState.IsValid || orderId == Guid.Empty || expectedVersion < 0) return BadRequest();
        var order = await api.PostAsync<PurchaseOrderView>("billing", $"{Path(tenantId)}/{orderId}/cancel", new { expectedVersion });
        if (order?.IsValidFor(tenantId) != true || order.Id != orderId || order.Status != "Canceled") throw new BackendException(HttpStatusCode.BadGateway);
        TempData["Notice"] = UiText.Get("OnSiparisIptalEdildiAbonelikVeBelgeler");
        return RedirectToAction(nameof(Index), new { tenantId });
    }
}
