using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETranslate.Documents.Api.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.StorageAudit;

public static class AuditCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter errors, CancellationToken token)
    {
        if (args is ["--help"]) { await output.WriteLineAsync(Help); return 0; }
        try
        {
            var values = Parse(args);
            var environment = values["--connection-env"];
            if (!Regex.IsMatch(environment, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new ArgumentException("Invalid connection environment variable name.");
            var connection = Environment.GetEnvironmentVariable(environment);
            if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("The named connection environment variable is empty.");
            var sql = new SqlConnectionStringBuilder(connection) { ApplicationName = "ETranslate read-only storage audit", ApplicationIntent = ApplicationIntent.ReadOnly };
            if (string.IsNullOrWhiteSpace(sql.InitialCatalog) || sql.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An explicit documents catalog is required.");
            var root = AuditPaths.ExistingRoot(values["--storage-root"]);
            var destination = values["--output"];
            if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("An absolute report path is required.");
            destination = Path.GetFullPath(destination);
            if (AuditPaths.IsWithin(root, destination)) throw new ArgumentException("The report must be outside the audited storage root.");
            if (!destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a JSON report path.");
            AuditPaths.EnsureNoLinks(destination);
            if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new ArgumentException("The report directory must already exist.");
            if (File.Exists(destination) || Directory.Exists(destination)) throw new ArgumentException("The report path already exists; choose a new name.");
            var options = new InventoryOptions {
                RecentWindow = TimeSpan.FromHours(Integer("--recent-hours", 24, 0, 87600)),
                MaximumFiles = Integer("--max-entries", 100000, 1, 1000000),
                MaximumReferences = Integer("--max-references", 100000, 1, 1000000),
                MaximumTotalHashBytes = (long)Integer("--hash-budget-mib", 1024, 1, 1048576) * 1024 * 1024
            };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(Integer("--timeout-seconds", 300, 1, 3600)));
            deadline.Token.ThrowIfCancellationRequested();
            var started = DateTimeOffset.UtcNow;
            await using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
                .UseSqlServer(sql.ConnectionString, provider => provider.CommandTimeout(30).EnableRetryOnFailure()).Options);
            var snapshot = await SqlBlobReferences.ReadAsync(db, options.MaximumReferences, deadline.Token);
            var report = await new BlobInventory(TimeProvider.System).ScanAsync(root, snapshot, options, deadline.Token);
            // CreateNew cannot overwrite existing reports; only this explicitly selected report is written.
            // No exception messages, connection strings, original filenames or document text enter the report.
            var envelope = new { sql.DataSource, sql.InitialCatalog, storageRoot = root, observationStartedAtUtc = started, report };
            await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, envelope, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, deadline.Token);
            await output.WriteLineAsync($"Complete: {report.Complete}; findings: {report.HasFindings}; entries: {report.Entries.Count}. Report: {destination}");
            foreach (var count in report.Counts) await output.WriteLineAsync($"{count.Key}: {count.Value}");
            return report.Complete ? report.HasFindings ? 1 : 0 : 2;

            int Integer(string name, int fallback, int minimum, int maximum)
            {
                if (!values.TryGetValue(name, out var value)) return fallback;
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum)
                    throw new ArgumentException("A numeric limit is invalid.");
                return number;
            }
        }
        catch (OperationCanceledException) { await errors.WriteLineAsync("Audit cancelled or timed out. No complete report is confirmed; any partial JSON must be discarded."); return 130; }
        catch (ArgumentException) { await errors.WriteLineAsync("Invalid audit arguments or connection configuration. Use --help; no complete report is confirmed."); return 3; }
        catch (Exception error)
        { await errors.WriteLineAsync($"Audit failed ({error.GetType().Name}). No complete report is confirmed; check configuration and access privately."); return 3; }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var allowed = new[] { "--connection-env", "--storage-root", "--output", "--recent-hours", "--max-entries", "--max-references", "--hash-budget-mib", "--timeout-seconds" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) throw new ArgumentException();
        for (var index = 0; index < args.Length; index += 2)
            if (!allowed.Contains(args[index]) || string.IsNullOrWhiteSpace(args[index + 1]) || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException();
        if (new[] { "--connection-env", "--storage-root", "--output" }.Any(name => !values.ContainsKey(name))) throw new ArgumentException();
        return values;
    }

    public const string Help = """
        ETranslate storage audit (read only; no service startup, migrations, repairs or deletion)
        Required: --connection-env NAME --storage-root ABSOLUTE_DIRECTORY --output ABSOLUTE_NEW_JSON
        Optional: --recent-hours 24 --max-entries 100000 --max-references 100000
                  --hash-budget-mib 1024 --timeout-seconds 300
        Hashing: SHA-256, up to 25 MiB per file and the total byte budget. Temporary files are not opened.
        Output must be outside storage; existing reports are never overwritten. Use SELECT-only SQL credentials.
        Exit: 0 complete/no findings; 1 complete/findings; 2 incomplete; 3 failed; 130 cancelled/timeout.
        Aged or unreferenced does not mean safe to delete. This is a non-atomic observation.
        """;
}
