using System.Security.Claims;
using ETranslate.Contracts.Tenants;
using ETranslate.IdentityAccess.Api.Identity;
using ETranslate.IdentityAccess.Api.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.IdentityAccess.Api.Endpoints;

public static class TenantTeamEndpoints
{
    public static IEndpointRouteBuilder MapTenantTeamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/team").RequireAuthorization().WithTags("Tenant Team");
        group.MapGet("/", GetTeamAsync);
        group.MapPost("/invitations", InviteAsync);
        group.MapPost("/invitations/{invitationId:guid}/cancel", CancelAsync);
        group.MapPut("/members/{userId:guid}/role", ChangeRoleAsync);
        group.MapPut("/members/{userId:guid}/status", SetStatusAsync);
        var invitations = endpoints.MapGroup("/api/v1/team-invitations").RequireAuthorization().WithTags("Tenant Team");
        invitations.MapGet("/{invitationId:guid}", GetInvitationAsync);
        invitations.MapPost("/{invitationId:guid}/accept", AcceptAsync);
        return endpoints;
    }

    private static async Task<TenantMembership?> ManagerAsync(Guid tenantId, ClaimsPrincipal principal, IdentityAccessDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return null;
        var actor = await db.TenantMemberships.Include(item => item.Tenant).SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.UserId == actorId && item.IsActive && item.Tenant.IsActive, ct);
        return actor is not null && TenantTeamPolicy.CanManage(actor) ? actor : null;
    }

    private static async Task<IResult> GetTeamAsync(Guid tenantId, ClaimsPrincipal principal, IdentityAccessDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await ManagerAsync(tenantId, principal, db, ct); if (actor is null) return Results.StatusCode(403);
        var members = await db.TenantMemberships.AsNoTracking().Include(item => item.User).Where(item => item.TenantId == tenantId)
            .OrderBy(item => item.JoinedAtUtc).ToListAsync(ct);
        var invitations = await db.TenantInvitations.AsNoTracking().Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.CreatedAtUtc).Take(100).Join(db.Users, item => item.TargetUserId, user => user.Id,
                (item, user) => new { Invitation = item, Email = user.Email }).ToListAsync(ct);
        var audits = await db.TeamAudits.AsNoTracking().Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.TeamVersion).Take(50).Select(item => new TeamAuditResponse(
                item.TeamVersion, item.ActorUserId, item.TargetUserId, item.Action, item.PreviousRole, item.Role, item.OccurredAtUtc)).ToListAsync(ct);
        return Results.Ok(new TeamResponse(tenantId, actor.Tenant.Name, actor.UserId, actor.Role, actor.Tenant.TeamVersion,
            members.Select(item => new TeamMemberResponse(item.UserId, item.User.Email!, item.Role, item.IsActive, item.JoinedAtUtc,
                TenantTeamPolicy.CanChange(actor, item, item.Role))).ToArray(),
            invitations.Select(item => InvitationResponse(item.Invitation, item.Email!, actor, clock.GetUtcNow())).ToArray(), audits));
    }

    private static async Task<IResult> InviteAsync(Guid tenantId, InviteTeamMemberRequest request, ClaimsPrincipal principal,
        IdentityAccessDbContext db, UserManager<ApplicationUser> users, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var actor = await ManagerAsync(tenantId, principal, db, ct); if (actor is null) return Results.StatusCode(403);
        if (!TenantTeamPolicy.CanAssign(actor, request.Role)) return Results.StatusCode(403);
        if (actor.Tenant.TeamVersion != request.ExpectedTeamVersion) return Conflict();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 256 ||
            !System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out var address) || address.Address != request.Email.Trim()) return Invalid("email", "A registered recipient email is required.");
        var target = await users.FindByEmailAsync(request.Email.Trim());
        if (target is null) return Invalid("email", "A registered recipient account is required before invitation.");
        if (await db.TenantMemberships.AnyAsync(item => item.TenantId == tenantId && item.UserId == target.Id && item.IsActive, ct) ||
            await db.TenantInvitations.AnyAsync(item => item.TenantId == tenantId && item.TargetUserId == target.Id && item.Status == TenantInvitationStatus.Pending, ct))
            return Conflict(); // Cancel expired invitations before issuing another link.
        var now = clock.GetUtcNow(); var created = TenantInvitation.Create(tenantId, target.Id, actor.UserId, request.Role, now);
        actor.Tenant.TouchTeam(request.ExpectedTeamVersion); db.TenantInvitations.Add(created.Invitation);
        await AuditAsync(db, publish, actor.Tenant, actor.UserId, target.Id, created.Invitation.Id, "Invited", null, request.Role, now, ct);
        if (!await SaveAsync(db, ct)) return Conflict();
        return Results.Created($"/api/v1/team-invitations/{created.Invitation.Id}", new CreatedTeamInvitationResponse(
            created.Invitation.Id, tenantId, target.Id, target.Email!, request.Role, created.Invitation.ExpiresAtUtc, created.Token));
    }

    private static async Task<IResult> CancelAsync(Guid tenantId, Guid invitationId, TeamVersionRequest request, ClaimsPrincipal principal,
        IdentityAccessDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var actor = await ManagerAsync(tenantId, principal, db, ct); if (actor is null) return Results.StatusCode(403);
        if (actor.Tenant.TeamVersion != request.ExpectedTeamVersion) return Conflict();
        var invitation = await db.TenantInvitations.SingleOrDefaultAsync(item => item.Id == invitationId && item.TenantId == tenantId, ct);
        if (invitation is null) return Results.NotFound();
        if (!TenantTeamPolicy.CanAssign(actor, invitation.Role)) return Results.StatusCode(403);
        if (invitation.Status != TenantInvitationStatus.Pending) return Conflict();
        var now = clock.GetUtcNow(); invitation.Cancel(now); actor.Tenant.TouchTeam(request.ExpectedTeamVersion);
        await AuditAsync(db, publish, actor.Tenant, actor.UserId, invitation.TargetUserId, invitation.Id, "InvitationCancelled", null, invitation.Role, now, ct);
        return await SaveAsync(db, ct) ? Results.Ok(new { status = invitation.Status }) : Conflict();
    }

    private static Task<IResult> ChangeRoleAsync(Guid tenantId, Guid userId, ChangeTeamRoleRequest request, ClaimsPrincipal principal,
        IdentityAccessDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct) =>
        ChangeMemberAsync(tenantId, userId, request.ExpectedTeamVersion, request.Role, null, principal, db, publish, clock, ct);

    private static Task<IResult> SetStatusAsync(Guid tenantId, Guid userId, SetTeamMemberStatusRequest request, ClaimsPrincipal principal,
        IdentityAccessDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct) => request.IsActive is null
        ? Task.FromResult(Invalid("isActive", "An explicit active status is required."))
        : ChangeMemberAsync(tenantId, userId, request.ExpectedTeamVersion, null, request.IsActive, principal, db, publish, clock, ct);

    private static async Task<IResult> ChangeMemberAsync(Guid tenantId, Guid userId, long version, TenantRole? newRole, bool? active,
        ClaimsPrincipal principal, IdentityAccessDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var actor = await ManagerAsync(tenantId, principal, db, ct); if (actor is null) return Results.StatusCode(403);
        if (actor.Tenant.TeamVersion != version) return Conflict();
        var target = await db.TenantMemberships.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.UserId == userId, ct);
        if (target is null) return Results.NotFound();
        if (!TenantTeamPolicy.CanChange(actor, target, newRole ?? target.Role)) return Results.StatusCode(403);
        var previous = target.Role; var now = clock.GetUtcNow();
        if (newRole.HasValue) target.ChangeRole(newRole.Value); else target.SetActive(active!.Value);
        actor.Tenant.TouchTeam(version);
        await AuditAsync(db, publish, actor.Tenant, actor.UserId, target.UserId, null, newRole.HasValue ? "RoleChanged" : active == true ? "MemberActivated" : "MemberDeactivated", previous, target.Role, now, ct);
        return await SaveAsync(db, ct) ? Results.Ok(new { target.UserId, target.Role, target.IsActive }) : Conflict();
    }

    private static async Task<IResult> GetInvitationAsync(Guid invitationId, ClaimsPrincipal principal, IdentityAccessDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        var item = await (from invitation in db.TenantInvitations.AsNoTracking() join tenant in db.Tenants on invitation.TenantId equals tenant.Id
                          where invitation.Id == invitationId && invitation.TargetUserId == userId && tenant.IsActive
                          select new { Invitation = invitation, TenantName = tenant.Name }).SingleOrDefaultAsync(ct);
        return item is null ? Results.NotFound() : Results.Ok(new RecipientInvitationResponse(item.Invitation.Id, item.Invitation.TenantId,
            item.TenantName, item.Invitation.Role, item.Invitation.Status, item.Invitation.ExpiresAtUtc, item.Invitation.IsPending(clock.GetUtcNow())));
    }

    private static async Task<IResult> AcceptAsync(Guid invitationId, AcceptTeamInvitationRequest request, ClaimsPrincipal principal,
        IdentityAccessDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        var tenantId = await db.TenantInvitations.AsNoTracking().Where(item => item.Id == invitationId && item.TargetUserId == userId)
            .Select(item => (Guid?)item.TenantId).SingleOrDefaultAsync(ct);
        if (tenantId is null) return Results.NotFound();
        // Anchor the tenant concurrency version BEFORE reading invitation/authority/membership.
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId && item.IsActive, ct);
        if (tenant is null) return Results.NotFound();
        var invitation = await db.TenantInvitations.SingleAsync(item => item.Id == invitationId, ct);
        if (!invitation.Matches(userId, request.Token)) return Results.NotFound();
        var now = clock.GetUtcNow();
        if (!invitation.IsPending(now)) return Conflict();
        var inviter = await db.TenantMemberships.SingleOrDefaultAsync(item => item.TenantId == tenant.Id && item.UserId == invitation.CreatedByUserId, ct);
        if (inviter is null || !TenantTeamPolicy.CanAssign(inviter, invitation.Role)) return Conflict();
        var membership = await db.TenantMemberships.SingleOrDefaultAsync(item => item.TenantId == tenant.Id && item.UserId == userId, ct);
        if (membership?.IsActive == true) return Conflict();
        var previous = membership?.Role;
        if (membership is null) db.TenantMemberships.Add(TenantMembership.CreateMember(tenant.Id, userId, invitation.Role, now));
        else membership.Rejoin(invitation.Role);
        invitation.Accept(now); tenant.TouchTeam(tenant.TeamVersion);
        await AuditAsync(db, publish, tenant, userId, userId, invitation.Id, "InvitationAccepted", previous, invitation.Role, now, ct);
        return await SaveAsync(db, ct) ? Results.Ok(new { tenantId = tenant.Id, role = invitation.Role }) : Conflict();
    }

    private static TeamInvitationResponse InvitationResponse(TenantInvitation item, string email, TenantMembership actor, DateTimeOffset now) =>
        new(item.Id, item.TargetUserId, email, item.Role, item.Status, item.CreatedAtUtc, item.ExpiresAtUtc,
            item.Status == TenantInvitationStatus.Pending && now >= item.ExpiresAtUtc,
            item.Status == TenantInvitationStatus.Pending && TenantTeamPolicy.CanAssign(actor, item.Role));
    private static IResult Conflict() => Results.Conflict(new { error = "tenant_team_conflict", detail = "Refresh team state; membership or invitation changed, expired or already exists." });
    private static IResult Invalid(string name, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [name] = [message] });
    private static async Task<bool> SaveAsync(IdentityAccessDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 }) { return false; }
    }
    private static async Task AuditAsync(IdentityAccessDbContext db, IPublishEndpoint publish, Tenant tenant, Guid actor, Guid target,
        Guid? invitationId, string action, TenantRole? previous, TenantRole? role, DateTimeOffset now, CancellationToken ct)
    {
        var audit = new TenantTeamAudit(tenant.Id, tenant.TeamVersion, actor, target, invitationId, action, previous, role, now);
        db.TeamAudits.Add(audit);
        await publish.Publish(new TenantTeamChangedV1(audit.Id, tenant.Id, tenant.TeamVersion, actor, target, invitationId,
            action, previous?.ToString(), role?.ToString(), now), ct);
    }
}

public sealed record InviteTeamMemberRequest(string Email, TenantRole Role, long ExpectedTeamVersion);
public sealed record TeamVersionRequest(long ExpectedTeamVersion);
public sealed record ChangeTeamRoleRequest(TenantRole Role, long ExpectedTeamVersion);
public sealed record SetTeamMemberStatusRequest(bool? IsActive, long ExpectedTeamVersion);
public sealed record AcceptTeamInvitationRequest(string Token);
public sealed record TeamMemberResponse(Guid UserId, string Email, TenantRole Role, bool IsActive, DateTimeOffset JoinedAtUtc, bool CanEdit);
public sealed record TeamInvitationResponse(Guid Id, Guid TargetUserId, string Email, TenantRole Role, TenantInvitationStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, bool IsExpired, bool CanCancel);
public sealed record CreatedTeamInvitationResponse(Guid Id, Guid TenantId, Guid TargetUserId, string Email, TenantRole Role, DateTimeOffset ExpiresAtUtc, string Token);
public sealed record TeamAuditResponse(long TeamVersion, Guid ActorUserId, Guid TargetUserId, string Action, TenantRole? PreviousRole, TenantRole? Role, DateTimeOffset OccurredAtUtc);
public sealed record TeamResponse(Guid TenantId, string TenantName, Guid UserId, TenantRole Role, long TeamVersion,
    IReadOnlyList<TeamMemberResponse> Members, IReadOnlyList<TeamInvitationResponse> Invitations, IReadOnlyList<TeamAuditResponse> Audit);
public sealed record RecipientInvitationResponse(Guid Id, Guid TenantId, string TenantName, TenantRole Role, TenantInvitationStatus Status, DateTimeOffset ExpiresAtUtc, bool CanAccept);
