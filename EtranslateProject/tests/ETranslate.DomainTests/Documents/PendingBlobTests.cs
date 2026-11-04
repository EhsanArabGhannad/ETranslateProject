using ETranslate.Documents.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETranslate.DomainTests.Documents;

public sealed class PendingBlobTests
{
    [Fact]
    public async Task Failure_before_database_attempt_discards_file_without_using_cancelled_request()
    {
        var store = new CleanupStore();
        await using (var write = Write(store)) { }
        Assert.Equal(1, store.Deletes);
        Assert.False(store.Cancelled);
    }

    [Fact]
    public async Task Unknown_outcome_retains_file_without_trusting_a_negative_probe()
    {
        var store = new CleanupStore(); var probes = 0;
        await using (var write = Write(store, (_, _) => { probes++; return Task.FromResult(false); })) write.PersistenceStarting();
        Assert.Equal(0, store.Deletes); Assert.Equal(0, probes);
    }

    [Fact]
    public async Task Confirmed_success_keeps_file()
    {
        var store = new CleanupStore();
        await using (var write = Write(store)) { write.PersistenceStarting(); write.Complete(); }
        Assert.Equal(0, store.Deletes);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Conflict_checks_reference_before_discarding_own_attempt(bool referenced, int deletes)
    {
        var store = new CleanupStore(); var probes = 0;
        await using (var write = Write(store, (key, token) => {
            Assert.Equal("private/attempt.png", key); Assert.False(token.IsCancellationRequested); probes++;
            return Task.FromResult(referenced);
        })) { write.PersistenceStarting(); await write.DiscardAfterConflictAsync(); }
        Assert.Equal(deletes, store.Deletes); Assert.Equal(1, probes);
    }

    [Fact]
    public async Task Failed_reference_check_retains_blob()
    {
        var store = new CleanupStore();
        await using (var write = Write(store, (_, _) => throw new IOException("SQL unavailable"))) {
            write.PersistenceStarting(); await write.DiscardAfterConflictAsync();
        }
        Assert.Equal(0, store.Deletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_failure_does_not_mask_original_failure_or_conflict(bool conflict)
    {
        var store = new CleanupStore { FailDelete = true };
        await using (var write = Write(store, (_, _) => Task.FromResult(false)))
            if (conflict) { write.PersistenceStarting(); await write.DiscardAfterConflictAsync(); }
        Assert.Equal(1, store.Deletes);
    }

    private static PendingBlob Write(CleanupStore store, Func<string, CancellationToken, Task<bool>>? probe = null)
        => new(store, "private/attempt.png", probe ?? ((_, _) => throw new InvalidOperationException("Unexpected SQL probe")), NullLogger.Instance);

    private sealed class CleanupStore : IDocumentBlobStore
    {
        public int Deletes; public bool Cancelled; public bool FailDelete;
        public Task DeleteIfExistsAsync(string key, CancellationToken token) {
            Deletes++; Cancelled = token.IsCancellationRequested;
            if (FailDelete) throw new UnauthorizedAccessException("Storage unavailable");
            return Task.CompletedTask;
        }
        public Task<StoredBlob> SaveAsync(string key, Stream stream, string type, long limit, CancellationToken token) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string key, CancellationToken token) => throw new NotSupportedException();
    }
}
