using ETranslate.Trust.Api.Preparations;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Trust.Api.Persistence;

public sealed class TrustDbContext(DbContextOptions<TrustDbContext> options) : DbContext(options)
{
    public DbSet<SigningPreparation> Preparations => Set<SigningPreparation>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("trust");
        model.Entity<SigningPreparation>(entity =>
        {
            entity.ToTable("signing_preparations"); entity.HasKey(item => item.Id);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.Property(item => item.SignaturePolicy).HasMaxLength(50);
            entity.Property(item => item.ArtifactKind).HasMaxLength(40);
            entity.Property(item => item.PdfSha256).HasMaxLength(64);
            entity.Property(item => item.SourceFilesJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.CancellationReason).HasMaxLength(2000);
            entity.HasIndex(item => new { item.TenantId, item.DocumentId }).IsUnique().HasFilter("[Status] = 0");
            entity.HasIndex(item => new { item.TenantId, item.TranslationJobId, item.DocumentId, item.CreatedAtUtc });
            entity.HasMany(item => item.Stages).WithOne().HasForeignKey(item => item.PreparationId).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(item => item.Stages).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        model.Entity<PlannedSignatureStage>(entity =>
        {
            entity.ToTable("planned_signature_stages"); entity.HasKey(item => item.Id);
            entity.Property(item => item.ProposedStatement).HasMaxLength(4000);
            entity.HasIndex(item => new { item.PreparationId, item.Order }).IsUnique();
        });
        model.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        model.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        model.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
