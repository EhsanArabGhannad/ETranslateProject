namespace ETranslate.Documents.Api.Storage;

public interface IDocumentBlobStore
{
    Task<StoredBlob> SaveAsync(
        string storageKey,
        Stream source,
        string contentType,
        long maximumBytes,
        CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed record StoredBlob(long SizeBytes, string Sha256);

public sealed class BlobTooLargeException(long maximumBytes) : Exception(
    $"Blob exceeds the maximum permitted size of {maximumBytes} bytes.")
{
    public long MaximumBytes { get; } = maximumBytes;
}
