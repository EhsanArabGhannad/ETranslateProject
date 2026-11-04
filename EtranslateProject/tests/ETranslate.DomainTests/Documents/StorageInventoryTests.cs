using System.Security.Cryptography;
using System.Text;
using ETranslate.StorageAudit;

namespace ETranslate.DomainTests.Documents;

public sealed class StorageInventoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly string parent = Path.Combine(Path.GetTempPath(), "etranslate-inventory-tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(parent, "blobs");
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("synthetic storage inventory İstanbul ترجمه");
    public StorageInventoryTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task Healthy_and_historical_shared_references_are_hashed_once_and_preserved()
    {
        Write("tenant/archived.bin", Bytes, 48);
        var refs = new[] { Reference("tenant/archived.bin"), Reference("tenant/archived.bin") with { Kind = "HistoricalPdf", RecordedAtUtc = Now.AddYears(-1) } };
        var before = Files(); var report = await Scan(refs);
        Assert.True(report.Complete); Assert.False(report.HasFindings); Assert.True(report.ReadOnly); Assert.False(report.AuthorizesDeletion);
        var item = Assert.Single(report.Entries); Assert.Equal("Verified", item.Status); Assert.Equal(Hash(Bytes), item.ActualSha256);
        Assert.Equal(2, item.References.Count); Assert.Equal(48, item.LastWriteAgeHours); Assert.Equal(before, Files());
    }

    [Fact]
    public async Task Missing_size_and_same_length_hash_mismatches_remain_distinct()
    {
        Write("size.bin", [1], 48); Write("hash.bin", Enumerable.Repeat((byte)0x41, Bytes.Length).ToArray(), 48);
        var report = await Scan([Reference("missing.bin"), Reference("size.bin"), Reference("hash.bin")]);
        Assert.True(report.Complete); Assert.True(report.HasFindings);
        Assert.Equal("Missing", Item(report, "missing.bin").Status);
        Assert.Equal("SizeMismatch", Item(report, "size.bin").Status); Assert.Equal("HashMismatch", Item(report, "hash.bin").Status);
        Assert.Equal(Hash([1]), Item(report, "size.bin").ActualSha256);
    }

    [Fact]
    public async Task Recent_aged_and_locked_temporary_files_are_reported_without_deletion()
    {
        Write("recent.bin", Bytes, 2); Write("aged.bin", Bytes, 48); Write("active.uploading", Bytes, 2); Write("old.uploading", Bytes, 48);
        using var busy = new FileStream(Path.Combine(Root, "active.uploading"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var report = await Scan([]);
        Assert.True(report.Complete);
        Assert.Equal("UnreferencedRecent", Item(report, "recent.bin").Status); Assert.Equal("UnreferencedAged", Item(report, "aged.bin").Status);
        Assert.Equal("TemporaryRecent", Item(report, "active.uploading").Status); Assert.Equal("TemporaryAged", Item(report, "old.uploading").Status);
        Assert.Null(Item(report, "active.uploading").ActualSha256); Assert.Equal(4, Directory.GetFiles(Root).Length);
    }

    [Fact]
    public async Task Unreadable_referenced_file_is_not_misreported_as_missing()
    {
        Write("locked.bin", Bytes, 48);
        using var busy = new FileStream(Path.Combine(Root, "locked.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var report = await Scan([Reference("locked.bin")]);
        Assert.False(report.Complete); Assert.Equal("Unverifiable", Assert.Single(report.Entries).Status);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("/outside.bin")]
    [InlineData("C:/outside.bin")]
    [InlineData("a/../outside.bin")]
    [InlineData("a\\outside.bin")]
    [InlineData("a/file:stream")]
    [InlineData("a//file")]
    [InlineData("a/./file")]
    [InlineData("a/file.png.")]
    [InlineData("a/file.png ")]
    [InlineData("a/NUL.png")]
    [InlineData("a/COM1.png")]
    public async Task Unsafe_metadata_keys_are_reported_without_reading_outside_root(string key)
    {
        var outside = Path.Combine(parent, "outside.bin"); File.WriteAllBytes(outside, Bytes);
        var report = await Scan([Reference(key)]);
        Assert.False(report.Complete); Assert.Equal("InvalidKey", Assert.Single(report.Entries).Status);
        Assert.Equal(Bytes, File.ReadAllBytes(outside));
    }

    [Fact]
    public async Task Conflicting_reference_metadata_does_not_hide_behind_a_matching_row()
    {
        Write("shared.bin", Bytes, 48);
        var report = await Scan([Reference("shared.bin"), Reference("shared.bin") with { Sha256 = new string('a', 64) }]);
        Assert.Equal("MetadataConflict", Assert.Single(report.Entries).Status); Assert.True(report.HasFindings);
    }

    [Fact]
    public async Task Reference_to_directory_is_invalid_metadata_without_opening_contents()
    {
        Directory.CreateDirectory(Path.Combine(Root, "expected-file"));
        var report = await Scan([Reference("expected-file")]);
        Assert.False(report.Complete); Assert.Equal("InvalidMetadata", Assert.Single(report.Entries).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hash_limits_never_produce_a_false_healthy_result(bool perFile)
    {
        Write("a.bin", Bytes, 48); Write("b.bin", Bytes, 48);
        var options = new InventoryOptions { MaximumFileHashBytes = perFile ? 1 : 1000, MaximumTotalHashBytes = perFile ? 1000 : Bytes.Length };
        var report = await Scan([Reference("a.bin"), Reference("b.bin")], options);
        Assert.False(report.Complete); Assert.Equal(perFile ? 2 : 1, report.Counts["HashLimit"]);
    }

    [Fact]
    public async Task Incomplete_reference_snapshot_cannot_label_files_unreferenced()
    {
        Write("candidate.bin", Bytes, 48);
        var report = await new BlobInventory(new FixedClock()).ScanAsync(Root, new([], false), new(), default);
        Assert.False(report.Complete); Assert.Equal("ReferenceUnknown", Assert.Single(report.Entries).Status);
    }

    [Fact]
    public async Task Enumeration_limit_is_explicit_and_never_removes_files()
    {
        Write("a.bin", Bytes, 48); Write("b.bin", Bytes, 48);
        var report = await Scan([], new() { MaximumFiles = 1 });
        Assert.False(report.Complete); Assert.Contains("FileEntryLimitReached", report.Issues); Assert.Equal(2, Directory.GetFiles(Root).Length);
    }

    [Fact]
    public async Task Missing_storage_root_is_not_created_and_cancelled_scan_does_not_continue()
    {
        var missing = Path.Combine(parent, "missing");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new BlobInventory(new FixedClock()).ScanAsync(missing, new([], true), new(), default));
        Assert.False(Directory.Exists(missing));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BlobInventory(new FixedClock()).ScanAsync(Root, new([], true), new(), new CancellationToken(true)));
        Assert.Throws<ArgumentException>(() => AuditPaths.ExistingRoot(Path.GetPathRoot(Root)!));
    }

    [Theory]
    [InlineData("inside")]
    [InlineData("existing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public async Task Command_rejects_unsafe_output_or_arguments_before_database_access_and_redacts_secrets(string mode)
    {
        var environment = "ETRANSLATE_AUDIT_UNIT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(environment, "Server=unreachable.invalid;Database=Synthetic;User Id=fixture;Password=SECRET_AUDIT_TEST");
        try
        {
            var existing = Path.Combine(parent, "existing.json"); File.WriteAllText(existing, "KEEP");
            var args = new List<string> { "--connection-env", environment, "--storage-root", Root, "--output", mode == "inside" ? Path.Combine(Root, "report.json") : existing };
            if (mode == "unknown") args.AddRange(["--delete", "yes"]);
            if (mode == "duplicate") args.AddRange(["--output", existing]);
            using var output = new StringWriter(); using var errors = new StringWriter();
            Assert.Equal(3, await AuditCommand.RunAsync(args.ToArray(), output, errors, default));
            Assert.DoesNotContain("SECRET_AUDIT_TEST", errors.ToString()); Assert.Equal("KEEP", File.ReadAllText(existing));
            Assert.False(File.Exists(Path.Combine(Root, "report.json")));
        }
        finally { Environment.SetEnvironmentVariable(environment, null); }
    }

    [Fact]
    public async Task Cancelled_command_does_not_open_database_or_write_report()
    {
        var environment = "ETRANSLATE_AUDIT_CANCEL_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(environment, "Server=unreachable.invalid;Database=Synthetic;User Id=fixture;Password=SECRET_CANCEL_TEST");
        try {
            var report = Path.Combine(parent, "cancelled.json"); using var output = new StringWriter(); using var errors = new StringWriter();
            Assert.Equal(130, await AuditCommand.RunAsync(["--connection-env", environment, "--storage-root", Root, "--output", report], output, errors, new CancellationToken(true)));
            Assert.False(File.Exists(report)); Assert.DoesNotContain("SECRET_CANCEL_TEST", errors.ToString());
        }
        finally { Environment.SetEnvironmentVariable(environment, null); }
    }

    private Task<InventoryReport> Scan(IReadOnlyList<BlobReference> refs, InventoryOptions? options = null)
        => new BlobInventory(new FixedClock()).ScanAsync(Root, new(refs, true), options ?? new(), default);
    private static BlobReference Reference(string key) => new("Source", Guid.NewGuid(), Guid.NewGuid(), key, Bytes.Length, Hash(Bytes), Now.AddDays(-2));
    private static InventoryEntry Item(InventoryReport report, string key) => Assert.Single(report.Entries, x => x.StorageKey == key);
    private void Write(string key, byte[] bytes, int ageHours) {
        var path = AuditPaths.ResolveKey(Root, key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, Now.AddHours(-ageHours).UtcDateTime);
    }
    private string Files() => string.Join("\n", Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => $"{path}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path):O}|{Hash(File.ReadAllBytes(path))}"));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    public void Dispose() {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "etranslate-inventory-tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(parent).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture cleanup scope failed.");
        Directory.Delete(parent, recursive: true);
    }
}
