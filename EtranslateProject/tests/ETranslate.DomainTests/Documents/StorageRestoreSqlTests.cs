using System.Security.Cryptography;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.StorageAudit;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalStorageRestoreFactAttribute : FactAttribute
{
    public LocalStorageRestoreFactAttribute() {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_RESTORE_TESTS") != "1") Skip = "Run Verify-StorageRestore.ps1; synthetic owned databases only.";
        Timeout = 300000;
    }
}

public sealed class StorageRestoreSqlTests
{
    private const string Body = "{\"type\":\"doc\",\"content\":[]}";
    private sealed record FileRecord(string Key, long Length, string Sha256);

    [LocalStorageRestoreFact]
    public async Task Copy_only_sql_backup_and_blob_copy_restore_historical_artifacts_and_detect_loss()
    {
        var root = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_RESTORE_ROOT") ?? throw new InvalidOperationException("Solution root required.");
        var server = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_RESTORE_SERVER") ?? @".\ESIMSSQLSERVER";
        await using var host = new IsolatedSqlProductHost(server, root, "storage-restore");
        await host.CreateDatabasesAsync(); var checks = new List<string>();
        try {
            await Seed(host);
            var sourceConnection = host.Connection("documentsdb");
            var expected = await Snapshot(sourceConnection); var expectedFiles = Manifest(host.Storage);
            var bundle = Path.Combine(host.Output, "backup-blobs"); var restored = Path.Combine(host.Output, "restored-blobs");
            // No services/writers are running in this fixture. Copy the complete retained tree.
            CopyFresh(host.Storage, bundle, expectedFiles);
            await File.WriteAllTextAsync(Path.Combine(host.Output, "blob-manifest.json"), JsonSerializer.Serialize(expectedFiles, new JsonSerializerOptions { WriteIndented = true }));
            var restoredConnection = await host.BackupAndRestoreDocumentsAsync();
            checks.Add("native COPY_ONLY/CHECKSUM backup, VERIFYONLY, new-catalog RESTORE WITH MOVE and CHECKDB");
            Assert.Equal(expected, await Snapshot(restoredConnection));
            checks.Add("exact metadata, two draft revisions, archived template and review history restored");
            CopyFresh(bundle, restored, expectedFiles);
            Assert.Equal(expectedFiles, Manifest(restored));
            var audit = await Audit(restoredConnection, restored); Assert.True(audit.Complete); Assert.False(audit.HasFindings); Assert.Equal(5, audit.Counts["Verified"]);
            checks.Add("all five retained asset kinds restored with identical bytes and SHA-256");

            // A later source change must never leak into the recovered backup.
            await using (var source = Db(sourceConnection)) {
                var doc = await source.TranslationDocuments.SingleAsync(); var previous = await source.DraftRevisions.SingleAsync(x => x.RevisionNumber == 2);
                source.DraftRevisions.Add(doc.AddDraftRevision(2, Body, null, doc.CreatedByUserId, DateTimeOffset.UtcNow, DocumentContentDescriptor.RichFormat, 10, previous));
                await source.SaveChangesAsync();
            }
            Assert.Equal(expected, await Snapshot(restoredConnection));
            await using (var source = Db(sourceConnection)) Assert.Equal(3, await source.DraftRevisions.CountAsync());
            checks.Add("restored catalog is independent of later source writes");

            var victim = expectedFiles.Single(x => x.Key.EndsWith("source.png", StringComparison.Ordinal));
            var path = AuditPaths.ResolveKey(restored, victim.Key);
            Assert.True(AuditPaths.IsWithin(restored, path)); File.Delete(path); // Owned recovery fixture only.
            audit = await Audit(restoredConnection, restored); Assert.Equal(1, audit.Counts["Missing"]);
            CopyOne(bundle, restored, victim); Assert.False((await Audit(restoredConnection, restored)).HasFindings);
            checks.Add("missing recovered source detected and restored from verified backup");
            var altered = File.ReadAllBytes(path); altered[^1] ^= 1; File.WriteAllBytes(path, altered);
            audit = await Audit(restoredConnection, restored); Assert.Equal(1, audit.Counts["HashMismatch"]);
            File.Delete(path); CopyOne(bundle, restored, victim); Assert.False((await Audit(restoredConnection, restored)).HasFindings);
            checks.Add("same-size corruption detected and recovered; SQL history unchanged");

            var badBundle = Path.Combine(host.Output, "bad-backup"); CopyFresh(bundle, badBundle, expectedFiles);
            var badPath = AuditPaths.ResolveKey(badBundle, victim.Key); File.WriteAllBytes(badPath, altered);
            var rejected = Path.Combine(host.Output, "rejected-restore");
            Assert.Throws<InvalidDataException>(() => CopyFresh(badBundle, rejected, expectedFiles)); Assert.False(Directory.Exists(rejected));
            checks.Add("damaged blob backup refused before creating a restore destination");
            Assert.Throws<IOException>(() => CopyFresh(bundle, restored, expectedFiles));
            Assert.Equal(expectedFiles, Manifest(restored)); Assert.Equal(expected, await Snapshot(restoredConnection));
            Assert.Equal(expectedFiles, Manifest(host.Storage)); Assert.Equal(expectedFiles, Manifest(bundle));
            checks.Add("existing restore folder refused; source and backup bytes unchanged");
            var report = new { host.RunId, checks, existingDatabasesUsed = false, syntheticOnly = true, sourceWritersStopped = true, assetCount = 5, sqlBackupSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(host.Output, "documents-copy-only.bak"))) };
            await File.WriteAllTextAsync(Path.Combine(host.Output, "restore-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            var latest = Path.Combine(root, "tmp/storage-restore"); Directory.CreateDirectory(latest);
            await File.WriteAllTextAsync(Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = host.RunId }));
        }
        finally { await File.WriteAllTextAsync(Path.Combine(host.Output, "checks.json"), JsonSerializer.Serialize(checks)); }
    }

    private static async Task Seed(IsolatedSqlProductHost host) {
        await using var db = Db(host.Connection("documentsdb")); await db.Database.MigrateAsync();
        var actor = Guid.NewGuid(); var tenant = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.AddDays(-2);
        var doc = TranslationDocument.Create(tenant, Guid.NewGuid(), actor, now);
        var draft = doc.AddDraftRevision(0, Body, null, actor, now, DocumentContentDescriptor.RichFormat, 10);
        var template = DocumentTemplate.Create(tenant, "Synthetic restore template", null, "{}", null, null, "{}", null, actor, now); template.SetActive(false, now);
        var bytes = BodyImageFixture.Png; var hash = Hash(bytes);
        var pdf = new DocumentPdfVersion(doc.Id, draft.Id, 1, null, "historical-fixture", "fixture/old.pdf", bytes.Length, hash, actor, now);
        var review = doc.SubmitReview(1, 0, pdf, null, "[]", actor, now); doc.DecideReview(review, DocumentReviewStatus.Approved, actor, null, now);
        var preview = new DocumentSigningPreview(Guid.NewGuid(), doc.Id, Guid.NewGuid(), review.Id, pdf.Id, hash, "fixture", "{}", "fixture/preview.pdf", bytes.Length, hash, actor, now);
        doc.ReopenReview(review, actor, "Synthetic reopened history", now);
        doc.AddDraftRevision(1, Body, null, actor, now, DocumentContentDescriptor.RichFormat, 10, draft);
        var source = doc.AddSourceFile(Guid.NewGuid(), "synthetic.png", "image/png", bytes.Length, hash, "fixture/source.png", actor, now);
        db.AddRange(doc, template, pdf, review, preview, source,
            DocumentImage.Create(Guid.NewGuid(), doc.Id, "unused.png", "image/png", bytes.Length, hash, "fixture/unused.png", actor, now),
            TemplateAsset.Create(Guid.NewGuid(), template.Id, "archived.png", "image/png", bytes.Length, hash, "fixture/archived.png", actor, now));
        await db.SaveChangesAsync();
        var refs = await SqlBlobReferences.ReadAsync(db, 100, default);
        foreach (var item in refs.References) { var path = AuditPaths.ResolveKey(host.Storage, item.StorageKey); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, bytes); }
    }
    private static FileRecord[] Manifest(string root) => Directory.GetFiles(AuditPaths.ExistingRoot(root), "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => { AuditPaths.EnsureNoLinks(path); return new FileRecord(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(path).Length, Hash(File.ReadAllBytes(path))); }).ToArray();
    private static void CopyFresh(string source, string destination, FileRecord[] manifest) {
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Restore destination already exists.");
        if (!Manifest(source).SequenceEqual(manifest)) throw new InvalidDataException("Backup does not match its manifest.");
        AuditPaths.EnsureNoLinks(destination); Directory.CreateDirectory(destination);
        foreach (var item in manifest) CopyOne(source, destination, item);
    }
    private static void CopyOne(string source, string destination, FileRecord item) {
        var input = AuditPaths.ResolveKey(source, item.Key); AuditPaths.EnsureNoLinks(input);
        var bytes = File.ReadAllBytes(input); if (bytes.LongLength != item.Length || Hash(bytes) != item.Sha256) throw new InvalidDataException("Backup integrity failure.");
        var output = AuditPaths.ResolveKey(destination, item.Key); AuditPaths.EnsureNoLinks(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes);
    }
    private static async Task<InventoryReport> Audit(string connection, string root) { await using var db = Db(connection); return await new BlobInventory(TimeProvider.System).ScanAsync(root, await SqlBlobReferences.ReadAsync(db, 100, default), new(), default); }
    private static async Task<string> Snapshot(string connection) {
        await using var db = Db(connection);
        return JsonSerializer.Serialize(new {
            refs = await SqlBlobReferences.ReadAsync(db, 100, default), docs = await db.TranslationDocuments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            drafts = await db.DraftRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), templates = await db.DocumentTemplates.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            revisions = await db.TemplateRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), reviews = await db.Reviews.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }
    private static DocumentsDbContext Db(string connection) => new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(connection).Options);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
