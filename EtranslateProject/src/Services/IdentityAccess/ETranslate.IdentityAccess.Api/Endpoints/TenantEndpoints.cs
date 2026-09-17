using System.Security.Claims;
using ETranslate.Contracts.Tenants;
using ETranslate.IdentityAccess.Api.Identity;
using ETranslate.IdentityAccess.Api.Persistence;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.IdentityAccess.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants")
            .RequireAuthorization()
            .WithTags("Tenants");

        group.MapPost("/", CreateTenantAsync);
        group.MapGet("/", GetMyTenantsAsync);
        group.MapGet("/{tenantId:guid}", GetTenantAsync);
        group.MapGet("/{tenantId:guid}/access", GetTenantAccessAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IdentityAccessDbContext database,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Name)] = ["Name is required and cannot exceed 200 characters."]
            });
        }

        if (!Enum.IsDefined(request.Type))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Type)] = ["Tenant type must be IndependentTranslator or TranslationOffice."]
            });
        }

        string slug;
        try
        {
            slug = TenantSlug.Create(request.Slug, request.Type);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Slug)] = [exception.Message]
            });
        }

        if (await database.Tenants.AnyAsync(tenant => tenant.Slug == slug, cancellationToken))
        {
            return Results.Conflict(new { error = "tenant_slug_already_exists" });
        }

        var now = timeProvider.GetUtcNow();
        var tenant = Tenant.Create(request.Name, slug, request.Type, now);
        var membership = TenantMembership.CreateOwner(tenant.Id, user.Id, now);

        database.Tenants.Add(tenant);
        database.TenantMemberships.Add(membership);

        await publishEndpoint.Publish(
            new TenantCreatedV1(
                Guid.NewGuid(),
                tenant.Id,
                user.Id,
                tenant.Type.ToString(),
                now),
            cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Results.Conflict(new { error = "tenant_slug_already_exists" });
        }

        var response = new TenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Type,
            TenantRole.Owner,
            tenant.CreatedAtUtc);

        return Results.Created($"/api/v1/tenants/{tenant.Id}", response);
    }

    private static async Task<IResult> GetMyTenantsAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IdentityAccessDbContext database,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var tenants = await database.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == user.Id && membership.Tenant.IsActive)
            .OrderBy(membership => membership.Tenant.Name)
            .Select(membership => new TenantResponse(
                membership.Tenant.Id,
                membership.Tenant.Name,
                membership.Tenant.Slug,
                membership.Tenant.Type,
                membership.Role,
                membership.Tenant.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(tenants);
    }

    private static async Task<IResult> GetTenantAsync(
        Guid tenantId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IdentityAccessDbContext database,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var tenant = await database.TenantMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.TenantId == tenantId &&
                membership.UserId == user.Id &&
                membership.Tenant.IsActive)
            .Select(membership => new TenantResponse(
                membership.Tenant.Id,
                membership.Tenant.Name,
                membership.Tenant.Slug,
                membership.Tenant.Type,
                membership.Role,
                membership.Tenant.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return tenant is null ? Results.NotFound() : Results.Ok(tenant);
    }

    private static async Task<IResult> GetTenantAccessAsync(
        Guid tenantId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IdentityAccessDbContext database,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var access = await database.TenantMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.TenantId == tenantId &&
                membership.UserId == user.Id &&
                membership.Tenant.IsActive)
            .Select(membership => new TenantAccessResponse(
                membership.TenantId,
                membership.UserId,
                membership.Tenant.Type.ToString(),
                membership.Role.ToString()))
            .SingleOrDefaultAsync(cancellationToken);

        return access is null ? Results.NotFound() : Results.Ok(access);
    }
}

public sealed record CreateTenantRequest(string Name, string? Slug, TenantType Type);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Slug,
    TenantType Type,
    TenantRole Role,
    DateTimeOffset CreatedAtUtc);

public sealed record TenantAccessResponse(
    Guid TenantId,
    Guid UserId,
    string TenantType,
    string Role);
