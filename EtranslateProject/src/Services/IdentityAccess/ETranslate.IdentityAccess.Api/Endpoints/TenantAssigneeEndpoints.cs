using System.Security.Claims;
using ETranslate.IdentityAccess.Api.Identity;
using ETranslate.IdentityAccess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.IdentityAccess.Api.Endpoints;

public static class TenantAssigneeEndpoints
{
    public static IEndpointRouteBuilder MapTenantAssigneeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/translation-assignees")
            .RequireAuthorization().WithTags("Translation Assignees");
        group.MapGet("/", ListAsync);
        group.MapGet("/{userId:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<bool> CanAssignAsync(Guid tenantId, ClaimsPrincipal principal, IdentityAccessDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return false;
        return await db.TenantMemberships.AnyAsync(item => item.TenantId == tenantId && item.UserId == actorId &&
            item.IsActive && item.Tenant.IsActive && (item.Role == TenantRole.Owner || item.Role == TenantRole.Administrator), ct);
    }

    private static IQueryable<TenantMembership> Eligible(Guid tenantId, IdentityAccessDbContext db) =>
        db.TenantMemberships.AsNoTracking().Where(item => item.TenantId == tenantId && item.IsActive && item.Tenant.IsActive &&
            (item.Role == TenantRole.Owner || item.Role == TenantRole.Administrator || item.Role == TenantRole.Translator));

    private static async Task<IResult> ListAsync(Guid tenantId, ClaimsPrincipal principal, IdentityAccessDbContext db, CancellationToken ct)
    {
        if (!await CanAssignAsync(tenantId, principal, db, ct)) return Results.StatusCode(403);
        var members = await Eligible(tenantId, db).OrderBy(item => item.User.Email).Take(200)
            .Select(item => new TranslationAssigneeResponse(item.UserId, item.User.Email!, item.Role.ToString())).ToListAsync(ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> GetAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, IdentityAccessDbContext db, CancellationToken ct)
    {
        if (!await CanAssignAsync(tenantId, principal, db, ct)) return Results.StatusCode(403);
        var member = await Eligible(tenantId, db).Where(item => item.UserId == userId)
            .Select(item => new TranslationAssigneeResponse(item.UserId, item.User.Email!, item.Role.ToString())).SingleOrDefaultAsync(ct);
        return member is null ? Results.NotFound() : Results.Ok(member);
    }
}

public sealed record TranslationAssigneeResponse(Guid UserId, string Email, string Role);
