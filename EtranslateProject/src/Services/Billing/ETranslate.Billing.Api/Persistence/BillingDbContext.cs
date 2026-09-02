using ETranslate.Billing.Api.Subscriptions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Billing.Api.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

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
        });

        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
