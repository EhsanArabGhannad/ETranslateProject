using ETranslate.TranslationWorkflow.Api.TranslationJobs;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.TranslationWorkflow.Api.Persistence;

public sealed class TranslationWorkflowDbContext(
    DbContextOptions<TranslationWorkflowDbContext> options) : DbContext(options)
{
    public DbSet<TranslationJob> TranslationJobs => Set<TranslationJob>();
    public DbSet<TranslationJobAssignmentChange> AssignmentChanges => Set<TranslationJobAssignmentChange>();

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
            entity.Property(job => job.AssignmentVersion).IsConcurrencyToken();
            entity.HasIndex(job => new { job.TenantId, job.AssignedTranslatorUserId, job.CreatedAtUtc });
        });

        modelBuilder.Entity<TranslationJobAssignmentChange>(entity =>
        {
            entity.ToTable("translation_job_assignment_changes");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Note).HasMaxLength(500);
            entity.HasIndex(item => new { item.TranslationJobId, item.Version }).IsUnique();
            entity.HasOne<TranslationJob>().WithMany().HasForeignKey(item => item.TranslationJobId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
