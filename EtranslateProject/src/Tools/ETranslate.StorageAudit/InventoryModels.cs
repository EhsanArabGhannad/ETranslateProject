namespace ETranslate.StorageAudit;

public sealed record BlobReference(string Kind, Guid AssetId, Guid OwnerId, string StorageKey,
    long SizeBytes, string Sha256, DateTimeOffset RecordedAtUtc);

public sealed record ReferenceSnapshot(IReadOnlyList<BlobReference> References, bool Complete);

public sealed record InventoryEntry(string StorageKey, string Status, IReadOnlyList<BlobReference> References,
    long? ActualSizeBytes = null, string? ActualSha256 = null, DateTimeOffset? LastWriteUtc = null,
    double? LastWriteAgeHours = null, string? Detail = null);

public sealed record InventoryReport(DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    bool Complete, IReadOnlyList<string> Issues, IReadOnlyList<InventoryEntry> Entries)
{
    public int ReportVersion => 1;
    public bool ReadOnly => true;
    public bool AuthorizesDeletion => false;
    public string Consistency => "NonAtomicObservation";
    public bool HasFindings => Issues.Count > 0 || Entries.Any(x => x.Status != "Verified");
    public IReadOnlyDictionary<string, int> Counts => Entries.GroupBy(x => x.Status).OrderBy(x => x.Key, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.Count());
}

public sealed record InventoryOptions
{
    public int MaximumFiles { get; init; } = 100_000;
    public int MaximumReferences { get; init; } = 100_000;
    public long MaximumFileHashBytes { get; init; } = 25 * 1024 * 1024;
    public long MaximumTotalHashBytes { get; init; } = 1024 * 1024 * 1024;
    public TimeSpan RecentWindow { get; init; } = TimeSpan.FromHours(24);

    public void Validate()
    {
        if (MaximumFiles is < 1 or > 1_000_000 || MaximumReferences is < 1 or > 1_000_000 ||
            MaximumFileHashBytes <= 0 || MaximumTotalHashBytes <= 0 || RecentWindow < TimeSpan.Zero)
            throw new ArgumentException("Invalid inventory limits.");
    }
}
