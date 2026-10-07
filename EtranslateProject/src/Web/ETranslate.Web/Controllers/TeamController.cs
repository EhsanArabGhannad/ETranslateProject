using System.Net;
using ETranslate.Web.Models;
using ETranslate.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETranslate.Web.Controllers;

[Authorize]
public sealed class TeamController(BackendApi api) : Controller
{
    private static string TeamPath(Guid tenantId) => $"/api/v1/tenants/{tenantId}/team";
    [HttpGet] public async Task<IActionResult> Index(Guid tenantId) => View(new TeamPageView
    { Team = await api.ReadAsync<TeamView>("identity-access", TeamPath(tenantId)) });

    [HttpPost] public async Task<IActionResult> Invite(Guid tenantId, string email, string role, long expectedTeamVersion)
    {
        try
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(email) || email.Length > 256 || role is not ("Administrator" or "Translator" or "Reviewer"))
                throw new BackendException(HttpStatusCode.BadRequest);
            var created = await api.PostAsync<CreatedTeamInvitationView>("identity-access", $"{TeamPath(tenantId)}/invitations", new { email, role, expectedTeamVersion });
            return View("Index", new TeamPageView { Team = await api.ReadAsync<TeamView>("identity-access", TeamPath(tenantId)),
                Created = created, InviteLink = $"{Url.Action(nameof(Accept), new { invitationId = created.Id })}#{created.Token}" });
        }
        catch (BackendException error) when (error.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            Response.StatusCode = (int)error.Status;
            return View("Index", new TeamPageView { Team = await api.ReadAsync<TeamView>("identity-access", TeamPath(tenantId)),
                Email = email ?? "", Role = role, Message = TeamError(error) });
        }
    }
    [HttpPost] public async Task<IActionResult> Cancel(Guid tenantId, Guid invitationId, long expectedTeamVersion)
    {
        try
        {
            if (!ModelState.IsValid) throw new BackendException(HttpStatusCode.BadRequest);
            await api.PostAsync<object>("identity-access", $"{TeamPath(tenantId)}/invitations/{invitationId}/cancel", new { expectedTeamVersion });
            TempData["Notice"] = "دعوت لغو شد؛ لینک قبلی دیگر قابل پذیرش نیست.";
        }
        catch (BackendException error) when (error.Status != HttpStatusCode.Unauthorized) { TempData["Notice"] = TeamError(error); }
        return RedirectToAction(nameof(Index), new { tenantId });
    }
    [HttpPost] public Task<IActionResult> Role(Guid tenantId, Guid userId, string role, long expectedTeamVersion)
        => ChangeMember(tenantId, userId, expectedTeamVersion, role, null);
    [HttpPost] public Task<IActionResult> Status(Guid tenantId, Guid userId, bool? isActive, long expectedTeamVersion)
        => ChangeMember(tenantId, userId, expectedTeamVersion, null, isActive);
    private async Task<IActionResult> ChangeMember(Guid tenantId, Guid userId, long version, string? role, bool? active)
    {
        try
        {
            if (!ModelState.IsValid || (role is null ? active is null : role is not ("Administrator" or "Translator" or "Reviewer")))
                throw new BackendException(HttpStatusCode.BadRequest);
            using var response = await api.SendAsync("identity-access", HttpMethod.Put,
                $"{TeamPath(tenantId)}/members/{userId}/{(role is null ? "status" : "role")}",
                role is null ? JsonContent.Create(new { isActive = active, expectedTeamVersion = version }) : JsonContent.Create(new { role, expectedTeamVersion = version }));
            BackendApi.EnsureSuccess(response); TempData["Notice"] = "دسترسی عضو به‌روز شد؛ درخواست‌های بعدی با دسترسی جدید بررسی می‌شوند.";
        }
        catch (BackendException error) when (error.Status != HttpStatusCode.Unauthorized) { TempData["Notice"] = TeamError(error); }
        return RedirectToAction(nameof(Index), new { tenantId });
    }
    [HttpGet] public async Task<IActionResult> Accept(Guid invitationId)
    {
        if (invitationId == Guid.Empty) return BadRequest();
        return View(new AcceptInvitationView { InvitationId = invitationId,
            Invitation = await api.ReadAsync<RecipientInvitationView>("identity-access", $"/api/v1/team-invitations/{invitationId}") });
    }
    [HttpPost] public async Task<IActionResult> Accept(Guid invitationId, string? token)
    {
        try
        {
            if (!ModelState.IsValid || token?.Length != 43) throw new BackendException(HttpStatusCode.BadRequest);
            var accepted = await api.PostAsync<AcceptedInvitation>("identity-access", $"/api/v1/team-invitations/{invitationId}/accept", new { token });
            TempData["Notice"] = "دعوت پذیرفته شد؛ به فضای کاری مشترک اضافه شدید.";
            return RedirectToAction("Index", "Workspace", new { tenantId = accepted.TenantId });
        }
        catch (BackendException error) when (error.Status != HttpStatusCode.Unauthorized)
        {
            Response.StatusCode = (int)error.Status;
            // Never echo a token after failed acceptance or put it in cookies/TempData.
            ModelState.Remove(nameof(token));
            return View(new AcceptInvitationView { InvitationId = invitationId, Message = TeamError(error) });
        }
    }
    private sealed record AcceptedInvitation(Guid TenantId, string Role);
    private static string TeamError(BackendException error) => error.Status switch
    {
        HttpStatusCode.BadRequest => "اطلاعات دعوت معتبر نیست؛ ابتدا حساب دعوت‌شونده ثبت شود و سپس ایمیل و نقش مجاز را وارد کنید.",
        HttpStatusCode.Conflict => "تیم یا دعوت تغییر کرده، منقضی یا تکراری است. آخرین وضعیت را باز کنید؛ دعوت قبلیِ در انتظار را پیش از صدور لینک جدید لغو کنید.",
        HttpStatusCode.Forbidden => "اجازه‌ی این تغییر را ندارید؛ مالک و نقش خودتان محافظت می‌شوند و مدیر نمی‌تواند مدیر دیگری را مدیریت کند.",
        _ => error.UserMessage
    };
}
