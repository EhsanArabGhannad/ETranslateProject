using ETranslate.Documents.Api.Documents;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Persistence;

public sealed class DocumentsDbContext(
    DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<TranslationDocument> TranslationDocuments => Set<TranslationDocument>();
    public DbSet<DocumentDraftRevision> DraftRevisions => Set<DocumentDraftRevision>();
    public DbSet<SourceFile> SourceFiles => Set<SourceFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("documents");

        modelBuilder.Entity<TranslationDocument>(entity =>
        {
            entity.ToTable("translation_documents");
            entity.HasKey(document => document.Id);
            entity.HasIndex(document => new { document.TenantId, document.TranslationJobId }).IsUnique();
            entity.HasIndex(document => new { document.TenantId, document.UpdatedAtUtc });

            entity.HasMany(document => document.DraftRevisions)
                .WithOne()
                .HasForeignKey(revision => revision.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(document => document.DraftRevisions)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(document => document.SourceFiles)
                .WithOne()
                .HasForeignKey(file => file.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(document => document.SourceFiles)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<DocumentDraftRevision>(entity =>
        {
            entity.ToTable("draft_revisions");
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.EditorContentJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(revision => revision.PlainText).HasColumnType("nvarchar(max)");
            entity.HasIndex(revision => new { revision.DocumentId, revision.RevisionNumber }).IsUnique();
        });

        modelBuilder.Entity<SourceFile>(entity =>
        {
            entity.ToTable("source_files");
            entity.HasKey(file => file.Id);
            entity.Property(file => file.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(file => file.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(file => file.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(file => file.StorageKey).HasMaxLength(500).IsRequired();
            entity.HasIndex(file => file.StorageKey).IsUnique();
            entity.HasIndex(file => new { file.DocumentId, file.UploadedAtUtc });
        });

        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_messages"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }
}
