using System.Net;
using System.Text.Json;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

[Authorize]
public sealed class SubscriptionController(BackendApi api) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(Guid tenantId)
    {
        Response.Headers.CacheControl = "private, no-store";
        // The layout generates a logout antiforgery token and may replace cache headers while rendering.
        Response.OnStarting(() =>
        {
            Response.Headers.CacheControl = "private, no-store";
            return Task.CompletedTask;
        });
        if (tenantId == Guid.Empty) return BadRequest();
        var tenant = await api.ReadAsync<TenantView>("identity-access", $"/api/v1/tenants/{tenantId}");
        if (tenant.Id != tenantId) throw new BackendException(HttpStatusCode.BadGateway);
        var page = new SubscriptionPageView { Tenant = tenant };
        try
        {
            using var response = await api.SendAsync("billing", HttpMethod.Get, $"/api/v1/tenants/{tenantId}/subscription");
            BackendApi.EnsureSuccess(response);
            var subscription = await response.Content.ReadFromJsonAsync<SubscriptionView>(HttpContext.RequestAborted);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted) || subscription?.IsValidFor(tenantId) != true ||
                (response.StatusCode == HttpStatusCode.Accepted) != (subscription.Status == "Pending"))
                throw new BackendException(HttpStatusCode.BadGateway);
            page.Subscription = subscription;
            Response.StatusCode = (int)response.StatusCode;
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway)
        {
            Response.StatusCode = (int)error.Status;
            page.Error = "وضعیت اشتراک فعلاً قابل بررسی نیست. این خطا به‌معنای انقضا یا فعال بودن اشتراک نیست؛ کمی بعد دوباره بررسی کنید.";
        }
        catch (JsonException)
        {
            Response.StatusCode = 502;
            page.Error = "پاسخ وضعیت اشتراک قابل تأیید نیست؛ کمی بعد دوباره بررسی کنید.";
        }
        return View(page);
    }
}
