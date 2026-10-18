using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalTemplateSqlFactAttribute : FactAttribute
{
    public LocalTemplateSqlFactAttribute() { if (Environment.GetEnvironmentVariable("ETRANSLATE_TEMPLATE_SQL_TESTS") != "1") Skip = "Local SQL template race fixtures only."; }
}
public sealed class LocalTemplateMigrationFactAttribute : FactAttribute
{
    public LocalTemplateMigrationFactAttribute() { if (Environment.GetEnvironmentVariable("ETRANSLATE_TEMPLATE_SQL_TESTS") != "1" || Environment.GetEnvironmentVariable("ETRANSLATE_TEMPLATE_MIGRATION_CUTOFF") is null) Skip = "Requires an explicit predeployment cutoff for legacy backfill audit."; }
}
public sealed class LateTemplateSqlTests
{
    private static DocumentsDbContext Db() => new(new DbContextOptionsBuilder<DocumentsDbContext>()
        .UseSqlServer("Server=.\\ESIMSSQLSERVER;Database=ETranslateDocuments;Integrated Security=True;Encrypt=True;TrustServerCertificate=True").Options);
    [LocalTemplateMigrationFact] public async Task Legacy_backfill_retains_original_pins_when_a_predeployment_cutoff_is_supplied()
    {
        var cutoff = Environment.GetEnvironmentVariable("ETRANSLATE_TEMPLATE_MIGRATION_CUTOFF");
        await using var db = Db(); var before = DateTimeOffset.Parse(cutoff!);
        var legacy = from draft in db.DraftRevisions join document in db.TranslationDocuments on draft.DocumentId equals document.Id
                     where document.CreatedAtUtc < before select new { draft.TemplateRevisionId, DocumentPin = document.TemplateRevisionId };
        Assert.True(await legacy.AnyAsync()); Assert.True(await legacy.AnyAsync(x => x.DocumentPin != null));
        Assert.False(await legacy.AnyAsync(x => x.TemplateRevisionId != x.DocumentPin));
    }
    [LocalTemplateSqlFact] public async Task Change_vs_save_or_review_has_one_winner_and_consistent_snapshot()
    {
        foreach (var reviewRace in new[] { false, true }) {
            var actor = Guid.NewGuid(); var tenant = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var document = TranslationDocument.Create(tenant, Guid.NewGuid(), actor, now);
            var first = document.AddDraftRevision(0, "{\"type\":\"doc\",\"content\":[]}", null, actor, now);
            var template = DocumentTemplate.Create(tenant, "Race", null, "{}", null, null, "{}", null, actor, now);
            var pdf = new DocumentPdfVersion(document.Id, first.Id, 1, null, "fixture", $"fixture/{Guid.NewGuid()}.pdf", 100, new string('a',64), actor, now);
            await using (var seed = Db()) { seed.AddRange(document, template, pdf); await seed.SaveChangesAsync(); }
            await using var left = Db(); await using var right = Db();
            var a = await left.TranslationDocuments.SingleAsync(d => d.Id == document.Id);
            var b = await right.TranslationDocuments.SingleAsync(d => d.Id == document.Id);
            var current = await left.DraftRevisions.AsNoTracking().SingleAsync(d => d.Id == first.Id);
            left.DraftRevisions.Add(a.ChangeTemplate(1, template, template.Revisions.Single(), current, actor, now));
            if (reviewRace) right.Reviews.Add(b.SubmitReview(1, 0, pdf, null, "[]", actor, now));
            else right.DraftRevisions.Add(b.AddDraftRevision(1, "{\"edited\":true}", null, actor, now));
            // Alternate the winner deterministically; the stale operation must roll back its insert too.
            if (reviewRace) { await right.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => left.SaveChangesAsync()); }
            else {
                await left.SaveChangesAsync();
                var error = await Record.ExceptionAsync(() => right.SaveChangesAsync());
                Assert.True(error is DbUpdateConcurrencyException || error is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } });
            }
            await using var verify = Db(); var saved = await verify.TranslationDocuments.SingleAsync(d => d.Id == document.Id);
            var revisions = await verify.DraftRevisions.Where(d => d.DocumentId == document.Id).OrderBy(d => d.RevisionNumber).ToListAsync();
            Assert.Equal(reviewRace ? 1 : 2, revisions.Count); Assert.Equal(saved.TemplateRevisionId, revisions.Last().TemplateRevisionId);
            Assert.Null(revisions[0].TemplateRevisionId);
            Assert.Equal(reviewRace ? 1 : 0, await verify.Reviews.CountAsync(r => r.DocumentId == document.Id));
        }
    }
}
