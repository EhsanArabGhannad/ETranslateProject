using ETranslate.TranslationWorkflow.Api.TranslationJobs;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.TranslationWorkflow.Api.Persistence;

public sealed class TranslationWorkflowDbContext(
    DbContextOptions<TranslationWorkflowDbContext> options) : DbContext(options)
{
    public DbSet<TranslationJob> TranslationJobs => Set<TranslationJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("workflow");

        modelBuilder.Entity<TranslationJob>(entity =>
        {
            entity.ToTable("translation_jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.ProviderType).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(job => job.SignaturePolicy).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(job => job.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(job => job.Title).HasMaxLength(200).IsRequired();
            entity.Property(job => job.SourceLanguageCode).HasMaxLength(35).IsRequired();
            entity.Property(job => job.TargetLanguageCode).HasMaxLength(35).IsRequired();
            entity.Property(job => job.NotaryRequirement).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(job => job.NotaryProcessingMode).HasConversion<string>().HasMaxLength(30);
            entity.Property(job => job.AcceptanceProfile).HasConversion<string>().HasMaxLength(40);
            entity.Property(job => job.AcceptanceProfileOther).HasMaxLength(200);
            entity.HasIndex(job => new { job.TenantId, job.CreatedAtUtc });
            entity.HasIndex(job => new { job.TenantId, job.Status });
        });

        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
