using ETranslate.Billing.Api.Subscriptions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PaidSubscriptionPeriod> PaidPeriods => Set<PaidSubscriptionPeriod>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(subscription => subscription.Id);
            entity.Property(subscription => subscription.PlanCode).HasMaxLength(80).IsRequired();
            entity.Property(subscription => subscription.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasIndex(subscription => subscription.TenantId).IsUnique();
            entity.Property(s => s.TenantType).HasMaxLength(30);
            entity.Property(s => s.PaidPlanName).HasMaxLength(200);
            entity.Property(s => s.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<PaidSubscriptionPeriod>(entity =>
        {
            entity.ToTable("paid_periods"); entity.HasKey(p => p.Id);
            entity.Property(p => p.Provider).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2").IsRequired();
            entity.Property(p => p.PaymentReference).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
            entity.Property(p => p.PlanCode).HasMaxLength(80).IsRequired();
            entity.Property(p => p.PlanName).HasMaxLength(200).IsRequired();
            entity.HasIndex(p => new { p.Provider, p.PaymentReference }).IsUnique();
            entity.HasIndex(p => p.TenantId);
            entity.HasOne<Subscription>().WithMany().HasForeignKey(p => p.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
