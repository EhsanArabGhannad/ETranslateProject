using System.Buffers;
using System.Security.Cryptography;
using ETranslate.Documents.Api.Documents;

namespace ETranslate.Documents.Api.Storage;

public sealed class LocalDocumentBlobStore : IDocumentBlobStore
{
    private readonly string _rootPath;

    public LocalDocumentBlobStore(DocumentStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new InvalidOperationException("Document storage root path is not configured.");
        }

        _rootPath = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredBlob> SaveAsync(
        string storageKey,
        Stream source,
        string contentType,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var destinationPath = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Storage key has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.uploading";
        var buffer = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long totalBytes = 0;
            var signature = new byte[SourceFilePolicy.MaximumSignatureLength];
            var signatureBytes = 0;
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int bytesRead;

                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > maximumBytes)
                    {
                        throw new BlobTooLargeException(maximumBytes);
                    }

                    if (signatureBytes < signature.Length)
                    {
                        var bytesToCopy = Math.Min(bytesRead, signature.Length - signatureBytes);
                        buffer.AsSpan(0, bytesToCopy).CopyTo(signature.AsSpan(signatureBytes));
                        signatureBytes += bytesToCopy;
                    }

                    hash.AppendData(buffer.AsSpan(0, bytesRead));
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);

                if (totalBytes == 0)
                {
                    throw new InvalidDataException("Empty files are not permitted.");
                }

                if (!SourceFilePolicy.HasValidSignature(signature.AsSpan(0, signatureBytes), contentType))
                {
                    throw new InvalidDataException("File content does not match its declared type.");
                }
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
            return new StoredBlob(totalBytes, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;

        return Task.FromResult(stream);
    }

    public Task DeleteIfExistsAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
        {
            throw new ArgumentException("Storage key must be a relative path.", nameof(storageKey));
        }

        var normalizedKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var resolvedPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedKey));
        var rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!resolvedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Storage key resolves outside the configured root.", nameof(storageKey));
        }

        return resolvedPath;
    }
}
