using ETranslate.IdentityAccess.Api.Identity;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.IdentityAccess.Api.Persistence;

public sealed class IdentityAccessDbContext(
    DbContextOptions<IdentityAccessDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<TenantTeamAudit> TeamAudits => Set<TenantTeamAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.CreatedAtUtc).IsRequired();
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(tenant => tenant.Id);
            entity.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
            entity.Property(tenant => tenant.Slug).HasMaxLength(63).IsRequired();
            entity.Property(tenant => tenant.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.HasIndex(tenant => tenant.Slug).IsUnique();
            entity.Property(tenant => tenant.TeamVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.ToTable("tenant_memberships");
            entity.HasKey(membership => new { membership.TenantId, membership.UserId });
            entity.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(membership => membership.IsActive).HasDefaultValue(true);
            entity.HasOne(membership => membership.Tenant)
                .WithMany(tenant => tenant.Memberships)
                .HasForeignKey(membership => membership.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(membership => membership.User)
                .WithMany(user => user.Memberships)
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(membership => membership.UserId);
        });

        modelBuilder.Entity<TenantInvitation>(entity =>
        {
            entity.ToTable("tenant_invitations"); entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.Role).HasConversion<string>().HasMaxLength(40);
            entity.Property(invitation => invitation.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(invitation => new { invitation.TenantId, invitation.TargetUserId }).IsUnique().HasFilter("[Status] = 0");
            entity.HasOne<Tenant>().WithMany().HasForeignKey(invitation => invitation.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(invitation => invitation.TargetUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TenantTeamAudit>(entity =>
        {
            entity.ToTable("tenant_team_audit"); entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Action).HasMaxLength(40).IsRequired();
            entity.Property(audit => audit.PreviousRole).HasConversion<string>().HasMaxLength(40);
            entity.Property(audit => audit.Role).HasConversion<string>().HasMaxLength(40);
            entity.HasIndex(audit => new { audit.TenantId, audit.TeamVersion }).IsUnique();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(audit => audit.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
