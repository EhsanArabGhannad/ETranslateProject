using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.StorageAudit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalStorageInventoryFactAttribute : FactAttribute
{
    public LocalStorageInventoryFactAttribute() {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_INVENTORY_TESTS") != "1") Skip = "Run Verify-StorageInventory.ps1 with new owned QA databases.";
        Timeout = 180000;
    }
}

public sealed class StorageInventorySqlTests
{
    private const string Body = "{\"type\":\"doc\",\"content\":[]}";
    private static readonly byte[] Bytes = BodyImageFixture.Png;

    [LocalStorageInventoryFact]
    public async Task Read_only_inventory_and_real_cli_report_all_asset_kinds_and_preserve_sql_and_files()
    {
        var root = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_INVENTORY_ROOT") ?? throw new InvalidOperationException("Solution root required.");
        var server = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_INVENTORY_SERVER") ?? @".\ESIMSSQLSERVER";
        await using var owned = new IsolatedSqlProductHost(server, root, "storage-inventory");
        await owned.CreateDatabasesAsync(); var checks = new List<string>();
        try
        {
            await Seed(owned);
            var before = Files(owned.Storage); var beforeDb = await Snapshot(owned);
            await using (var sql = new SqlConnection(owned.Connection("documentsdb"))) {
                await sql.OpenAsync();
                await using var command = new SqlCommand("CREATE USER inventory_reader WITHOUT LOGIN; GRANT SELECT ON SCHEMA::documents TO inventory_reader; DENY INSERT, UPDATE, DELETE ON SCHEMA::documents TO inventory_reader;", sql);
                await command.ExecuteNonQueryAsync();
            }
            ReferenceSnapshot references;
            await using (var sql = new SqlConnection(owned.Connection("documentsdb"))) {
                await sql.OpenAsync();
                await using var impersonate = new SqlCommand("EXECUTE AS USER='inventory_reader'; SELECT HAS_PERMS_BY_NAME('documents.source_files','OBJECT','INSERT');", sql);
                Assert.Equal(0, Convert.ToInt32(await impersonate.ExecuteScalarAsync()));
                await using var reader = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(sql).Options);
                references = await SqlBlobReferences.ReadAsync(reader, 100, default);
                Assert.Equal(8, references.References.Count); Assert.True(references.Complete); Assert.Empty(reader.ChangeTracker.Entries());
                var limited = await SqlBlobReferences.ReadAsync(reader, 1, default); Assert.False(limited.Complete); Assert.Single(limited.References);
            }
            checks.Add("SELECT-only database principal reads all five asset tables without tracked entities");
            checks.Add("reference limit reports incomplete");
            Assert.Equal(new[] { "DocumentImage", "DraftPdf", "SigningPreview", "Source", "TemplateAsset" }, references.References.Select(x => x.Kind).Distinct().Order(StringComparer.Ordinal));
            Assert.Contains(references.References, x => x.StorageKey == "fixture/old-draft.pdf");
            Assert.Contains(references.References, x => x.StorageKey == "fixture/archived-template.png");
            checks.Add("historical PDF, unused retained image and archived-template asset included");
            var report = await new BlobInventory(TimeProvider.System).ScanAsync(owned.Storage, references, new(), default);
            Expected(report);
            checks.Add("healthy/missing/size/hash/recent/aged/temporary statuses and exact counts");
            var reportPath = Path.Combine(owned.Output, "inventory.json");
            Assert.Equal(1, await Cli(owned, reportPath));
            using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath))) {
                var result = json.RootElement.GetProperty("report"); Assert.True(result.GetProperty("complete").GetBoolean());
                Assert.False(result.GetProperty("authorizesDeletion").GetBoolean()); Assert.Equal(12, result.GetProperty("entries").GetArrayLength());
                Assert.Equal(5, result.GetProperty("counts").GetProperty("Verified").GetInt32());
            }
            var originalReport = await File.ReadAllBytesAsync(reportPath);
            Assert.Equal(3, await Cli(owned, reportPath)); Assert.Equal(originalReport, await File.ReadAllBytesAsync(reportPath));
            checks.Add("real console entry point writes report outside storage and refuses overwrite");
            var limitPath = Path.Combine(owned.Output, "incomplete.json"); Assert.Equal(2, await Cli(owned, limitPath, "--max-references", "1"));
            using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(limitPath))) {
                var result = json.RootElement.GetProperty("report"); Assert.False(result.GetProperty("complete").GetBoolean());
                Assert.DoesNotContain(result.GetProperty("entries").EnumerateArray(), x => x.GetProperty("status").GetString()!.StartsWith("Unreferenced", StringComparison.Ordinal));
            }
            checks.Add("real CLI incomplete exit code and provisional classifications");
            await Junction(owned, references); checks.Add("Windows junction/root escape skipped; outside fixture preserved");
            Assert.Equal(before, Files(owned.Storage)); Assert.Equal(beforeDb, await Snapshot(owned));
            checks.Add("all SQL metadata/drafts/reviews and file bytes/lengths/last-write timestamps unchanged");
            var latest = Path.Combine(root, "tmp/storage-inventory"); Directory.CreateDirectory(latest);
            await File.WriteAllTextAsync(Path.Combine(latest, "latest.json"), JsonSerializer.Serialize(new { runId = owned.RunId }));
        }
        finally {
            await File.WriteAllTextAsync(Path.Combine(owned.Output, "acceptance.json"), JsonSerializer.Serialize(new { runId = owned.RunId, checks, existingDatabasesUsed = false, syntheticDataOnly = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void Expected(InventoryReport report) {
        Assert.True(report.Complete); Assert.True(report.HasFindings); Assert.Equal(12, report.Entries.Count); Assert.Equal(5, report.Counts["Verified"]);
        foreach (var status in new[] { "Missing", "HashMismatch", "SizeMismatch", "UnreferencedRecent", "UnreferencedAged", "TemporaryRecent", "TemporaryAged" }) Assert.Equal(1, report.Counts[status]);
    }
    private static async Task Seed(IsolatedSqlProductHost owned)
    {
        await using var db = Db(owned); await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow.AddDays(-2); var tenant = Guid.NewGuid(); var actor = Guid.NewGuid();
        var doc = TranslationDocument.Create(tenant, Guid.NewGuid(), actor, now);
        var draft = doc.AddDraftRevision(0, Body, null, actor, now, DocumentContentDescriptor.RichFormat, 10);
        var template = DocumentTemplate.Create(tenant, "Synthetic inventory template", null, "{}", null, null, "{}", null, actor, now);
        // Keep historical assets even though the current draft/template no longer uses them.
        template.SetActive(false, now.AddHours(1));
        var pdf = new DocumentPdfVersion(doc.Id, draft.Id, 1, null, "fixture-previous-renderer", "fixture/old-draft.pdf", Bytes.Length, Hash(Bytes), actor, now);
        var review = doc.SubmitReview(1, 0, pdf, null, "[]", actor, now); doc.DecideReview(review, DocumentReviewStatus.Approved, actor, null, now);
        var preview = new DocumentSigningPreview(Guid.NewGuid(), doc.Id, Guid.NewGuid(), review.Id, pdf.Id, pdf.Sha256, "fixture", "{}", "fixture/preview.pdf", Bytes.Length, Hash(Bytes), actor, now);
        doc.ReopenReview(review, actor, "Synthetic reopen", now.AddHours(1));
        doc.AddDraftRevision(1, Body, null, actor, now.AddHours(2), DocumentContentDescriptor.RichFormat, 10, draft);
        var source = doc.AddSourceFile(Guid.NewGuid(), "synthetic.png", "image/png", Bytes.Length, Hash(Bytes), "fixture/source.png", actor, now);
        var missing = doc.AddSourceFile(Guid.NewGuid(), "missing.png", "image/png", Bytes.Length, Hash(Bytes), "fixture/missing.png", actor, now);
        db.AddRange(doc, template, pdf, review, preview, source, missing,
            DocumentImage.Create(Guid.NewGuid(), doc.Id, "retained.png", "image/png", Bytes.Length, Hash(Bytes), "fixture/unused-image.png", actor, now),
            DocumentImage.Create(Guid.NewGuid(), doc.Id, "bad-hash.png", "image/png", Bytes.Length, Hash(Bytes), "fixture/bad-hash.png", actor, now),
            TemplateAsset.Create(Guid.NewGuid(), template.Id, "archived.png", "image/png", Bytes.Length, Hash(Bytes), "fixture/archived-template.png", actor, now),
            TemplateAsset.Create(Guid.NewGuid(), template.Id, "bad-size.png", "image/png", Bytes.Length, Hash(Bytes), "fixture/bad-size.png", actor, now));
        await db.SaveChangesAsync();
        foreach (var key in new[] { "source.png", "unused-image.png", "archived-template.png", "old-draft.pdf", "preview.pdf" }) Write("fixture/" + key, Bytes, 48);
        var altered = Bytes.ToArray(); altered[^1] ^= 1; Write("fixture/bad-hash.png", altered, 48); Write("fixture/bad-size.png", Bytes[..^1], 48);
        Write("orphans/aged.png", Bytes, 48); Write("orphans/recent.png", Bytes, 2); Write("pending/aged.uploading", Bytes, 48); Write("pending/recent.uploading", Bytes, 2);
        void Write(string key, byte[] bytes, int age) {
            var path = AuditPaths.ResolveKey(owned.Storage, key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-age));
        }
    }
    private static async Task<int> Cli(IsolatedSqlProductHost owned, string reportPath, params string[] extra)
    {
        var dll = Path.Combine(owned.Root, "src/Tools/ETranslate.StorageAudit/bin/Debug/net10.0/ETranslate.StorageAudit.dll");
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { dll, "--connection-env", "ETRANSLATE_AUDIT_QA_CONNECTION", "--storage-root", owned.Storage, "--output", reportPath }.Concat(extra)) info.ArgumentList.Add(arg);
        info.Environment["ETRANSLATE_AUDIT_QA_CONNECTION"] = owned.Connection("documentsdb");
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        var log = await stdout + await stderr; await File.AppendAllTextAsync(Path.Combine(owned.Output, "cli.log"), log);
        Assert.DoesNotContain("ConnectionString", log); return process.ExitCode;
    }
    private static async Task Junction(IsolatedSqlProductHost owned, ReferenceSnapshot snapshot)
    {
        var pwsh = Environment.GetEnvironmentVariable("ETRANSLATE_STORAGE_INVENTORY_PWSH") ?? throw new InvalidOperationException("Explicit PowerShell executable required for Windows junction fixture.");
        var outside = Path.Combine(owned.Output, "outside"); Directory.CreateDirectory(outside); var outsideFile = Path.Combine(outside, "outside.bin"); await File.WriteAllBytesAsync(outsideFile, Bytes);
        var link = Path.Combine(owned.Storage, "junction"); var script = Path.Combine(owned.Output, "create-fixture-junction.ps1");
        await File.WriteAllTextAsync(script, "param([string]$LinkPath,[string]$TargetPath)\n$ErrorActionPreference='Stop'\nNew-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null\n");
        var info = new ProcessStartInfo(pwsh) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-LinkPath", link, "-TargetPath", outside }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(0, process.ExitCode);
        try {
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            var refs = snapshot.References.Append(new BlobReference("Source", Guid.NewGuid(), Guid.NewGuid(), "junction/outside.bin", Bytes.Length, Hash(Bytes), DateTimeOffset.UtcNow)).ToArray();
            var report = await new BlobInventory(TimeProvider.System).ScanAsync(owned.Storage, new(refs, true), new(), default);
            Assert.False(report.Complete); Assert.Equal(2, report.Counts["UnsafePath"]);
            Assert.Throws<UnsafeAuditPathException>(() => AuditPaths.ExistingRoot(link));
            Assert.Equal(Bytes, await File.ReadAllBytesAsync(outsideFile));
        }
        finally {
            // Remove only the freshly-created link, never its target or a recursive enumeration.
            if (!Path.GetFullPath(link).StartsWith(Path.GetFullPath(owned.Storage) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0) throw new InvalidOperationException("Junction cleanup scope failed.");
            Directory.Delete(link, recursive: false);
        }
    }
    private static async Task<string> Snapshot(IsolatedSqlProductHost owned) {
        await using var db = Db(owned);
        return JsonSerializer.Serialize(new { refs = await SqlBlobReferences.ReadAsync(db, 1000, default),
            docs = await db.TranslationDocuments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            drafts = await db.DraftRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            reviews = await db.Reviews.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() });
    }
    private static string Files(string root) => string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => $"{Path.GetRelativePath(root, path)}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path):O}|{Hash(File.ReadAllBytes(path))}"));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static DocumentsDbContext Db(IsolatedSqlProductHost owned) => new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(owned.Connection("documentsdb")).Options);
}
