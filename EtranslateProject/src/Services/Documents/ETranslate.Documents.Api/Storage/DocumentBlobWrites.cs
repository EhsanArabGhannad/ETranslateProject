using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.Documents.Api.Storage;

// SQL and file storage cannot commit atomically. Never infer rollback from a lost response.
public sealed class DocumentBlobWrites(
    IDocumentBlobStore store, IServiceScopeFactory scopes, ILogger<DocumentBlobWrites> logger)
{
    public PendingBlob Begin(string key) => new(store, key, IsReferencedAsync, logger);

    private async Task<bool> IsReferencedAsync(string key, CancellationToken token)
    {
        // Use a fresh context, independent of the failed request's tracking/connection state.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        db.Database.SetCommandTimeout(5);
        return await db.SourceFiles.Where(x => x.StorageKey == key).Select(x => x.StorageKey)
            .Concat(db.DocumentImages.Where(x => x.StorageKey == key).Select(x => x.StorageKey))
            .Concat(db.TemplateAssets.Where(x => x.StorageKey == key).Select(x => x.StorageKey))
            .Concat(db.PdfVersions.Where(x => x.StorageKey == key).Select(x => x.StorageKey))
            .Concat(db.SigningPreviews.Where(x => x.StorageKey == key).Select(x => x.StorageKey))
            .AnyAsync(token);
    }
}

public sealed class PendingBlob(
    IDocumentBlobStore store, string key, Func<string, CancellationToken, Task<bool>> isReferenced, ILogger logger) : IAsyncDisposable
{
    private bool persistenceAttempted;
    private bool finished;

    // Call immediately before SaveChanges, including when that call is cancelled or retried.
    public void PersistenceStarting() => persistenceAttempted = true;
    public void Complete() => finished = true;

    public async Task DiscardAfterConflictAsync()
    {
        // A retry may report a duplicate after its first attempt actually committed.
        // Only an explicit concurrency/unique conflict may use a negative reference check.
        // General SQL failures remain uncertain even if a later query finds no row.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            if (await isReferenced(key, deadline.Token)) { finished = true; return; }
            await DeleteBestEffortAsync(deadline.Token);
        }
        catch (Exception error)
        {
            finished = true;
            logger.LogWarning(error, "Blob conflict cleanup could not be confirmed; retained {StorageKey}", key);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (finished) return;
        if (persistenceAttempted)
        {
            finished = true;
            logger.LogWarning("Blob retained after uncertain database outcome; reconcile before deletion: {StorageKey}", key);
            return;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await DeleteBestEffortAsync(deadline.Token);
    }

    private async Task DeleteBestEffortAsync(CancellationToken token)
    {
        finished = true;
        try { await store.DeleteIfExistsAsync(key, token); }
        catch (Exception error)
        {
            // Cleanup must not replace the original failure or turn a conflict into an unconfirmed write.
            logger.LogWarning(error, "Blob cleanup failed; retained for reconciliation: {StorageKey}", key);
        }
    }
}
