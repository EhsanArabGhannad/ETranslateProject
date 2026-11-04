using System.Security.Cryptography;

namespace ETranslate.StorageAudit;

public sealed class BlobInventory(TimeProvider clock)
{
    public async Task<InventoryReport> ScanAsync(string storageRoot, ReferenceSnapshot snapshot, InventoryOptions options, CancellationToken token)
    {
        options.Validate();
        var root = AuditPaths.ExistingRoot(storageRoot);
        var started = clock.GetUtcNow();
        var issues = new List<string>(); var entries = new List<InventoryEntry>();
        if (!snapshot.Complete) issues.Add("ReferenceLimitReached: unreferenced classifications are provisional.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var references = snapshot.References.GroupBy(x => x.StorageKey, comparer).ToDictionary(x => x.Key, x => (IReadOnlyList<BlobReference>)x.ToArray(), comparer);
        var observed = new HashSet<string>(comparer);
        var pending = new Stack<string>(); pending.Push(root);
        long hashBytes = 0; var files = 0; var exhausted = false;
        while (pending.TryPop(out var directory) && !exhausted)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // Do not traverse junctions, symlinks or other reparse points.
                AuditPaths.EnsureNoLinks(directory);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (++files > options.MaximumFiles) { issues.Add("FileEntryLimitReached"); exhausted = true; break; }
                    var key = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    var refs = references.GetValueOrDefault(key) ?? [];
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) {
                            observed.Add(key); entries.Add(new(key, "UnsafePath", refs, Detail: "Reparse point skipped.")); continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        observed.Add(key);
                        entries.Add(await InspectAsync(key, path, refs));
                    }
                    catch (ArgumentException) { observed.Add(key); entries.Add(new(key, "InvalidKey", refs)); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { observed.Add(key); entries.Add(new(key, "Unverifiable", refs, Detail: error.GetType().Name)); }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { issues.Add("DirectoryUnverifiable:" + Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/') + ":" + error.GetType().Name); }
        }
        foreach (var pair in references.Where(x => !observed.Contains(x.Key)).OrderBy(x => x.Key, comparer))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path = AuditPaths.ResolveKey(root, pair.Key); AuditPaths.EnsureNoLinks(path);
                // Direct inspection distinguishes absent files from unreadable/skipped directories.
                entries.Add(await InspectAsync(pair.Key, path, pair.Value));
            }
            catch (ArgumentException) { entries.Add(new(pair.Key, "InvalidKey", pair.Value)); }
            catch (UnsafeAuditPathException) { entries.Add(new(pair.Key, "UnsafePath", pair.Value)); }
            catch (FileNotFoundException) { entries.Add(new(pair.Key, "Missing", pair.Value)); }
            catch (DirectoryNotFoundException) { entries.Add(new(pair.Key, "Missing", pair.Value)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { entries.Add(new(pair.Key, "Unverifiable", pair.Value, Detail: error.GetType().Name)); }
        }
        var incomplete = new[] { "UnsafePath", "Unverifiable", "InvalidKey", "HashLimit", "ChangedDuringRead", "InvalidMetadata" };
        return new(started, clock.GetUtcNow(), issues.Count == 0 && !entries.Any(x => incomplete.Contains(x.Status)), issues,
            entries.OrderBy(x => x.StorageKey, comparer).ThenBy(x => x.Status, StringComparer.Ordinal).ToArray());

        async Task<InventoryEntry> InspectAsync(string key, string path, IReadOnlyList<BlobReference> refs)
        {
            if (refs.Count > 0) AuditPaths.ResolveKey(root, key);
            AuditPaths.EnsureNoLinks(path);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                return new(key, "InvalidMetadata", refs, Detail: "Expected a file; path is a directory.");
            var info = new FileInfo(path); var size = info.Length; var modified = new DateTimeOffset(info.LastWriteTimeUtc);
            var age = (started - modified).TotalHours;
            InventoryEntry Entry(string status, string? hash = null, string? detail = null) => new(key, status, refs, size, hash, modified, age, detail);
            if (key.EndsWith(".uploading", StringComparison.OrdinalIgnoreCase))
                return Entry(refs.Count > 0 ? "InvalidMetadata" : age < options.RecentWindow.TotalHours ? "TemporaryRecent" : "TemporaryAged", detail: "Temporary content is not opened or hashed.");
            if (refs.Any(x => x.SizeBytes <= 0 || x.Sha256 is not { Length: 64 } || !x.Sha256.All(Uri.IsHexDigit))) return Entry("InvalidMetadata");
            if (size > options.MaximumFileHashBytes || size > options.MaximumTotalHashBytes - hashBytes) return Entry("HashLimit");
            hashBytes += size;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            // Bound reads even if another writer can grow a file on this filesystem.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; long readBytes = 0; int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, size - readBytes + 1)), token)) > 0) {
                readBytes += read; if (readBytes > size) return Entry("ChangedDuringRead"); hash.AppendData(buffer.AsSpan(0, read));
            }
            info.Refresh();
            if (readBytes != size || !info.Exists || info.Length != size || info.LastWriteTimeUtc != modified.UtcDateTime) return Entry("ChangedDuringRead");
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (refs.Count == 0) return Entry(snapshot.Complete ? age < options.RecentWindow.TotalHours ? "UnreferencedRecent" : "UnreferencedAged" : "ReferenceUnknown", actual);
            if (refs.Select(x => (x.SizeBytes, Hash: x.Sha256.ToLowerInvariant())).Distinct().Count() > 1) return Entry("MetadataConflict", actual);
            if (refs.Any(x => x.SizeBytes != size)) return Entry("SizeMismatch", actual);
            return Entry(refs.All(x => x.Sha256.Equals(actual, StringComparison.OrdinalIgnoreCase)) ? "Verified" : "HashMismatch", actual);
        }
    }
}
