using System.Security.Cryptography;
using System.Text;
using ETranslate.Documents.Api.Storage;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalDocumentBlobStoreTests : IAsyncLifetime
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "etranslate-tests",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveOpenAndDelete_PreservesContentAndReturnsSha256()
    {
        var store = CreateStore();
        var content = Encoding.UTF8.GetBytes("%PDF-1.7 source document");
        await using var input = new MemoryStream(content);

        var stored = await store.SaveAsync(
            "tenant/document/sources/file.pdf",
            input,
            "application/pdf",
            maximumBytes: 1024,
            CancellationToken.None);

        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), stored.Sha256);

        await using (var output = await store.OpenReadAsync(
            "tenant/document/sources/file.pdf",
            CancellationToken.None))
        {
            Assert.NotNull(output);
            using var copied = new MemoryStream();
            await output.CopyToAsync(copied, CancellationToken.None);
            Assert.Equal(content, copied.ToArray());
        }

        await store.DeleteIfExistsAsync(
            "tenant/document/sources/file.pdf",
            CancellationToken.None);
        Assert.Null(await store.OpenReadAsync(
            "tenant/document/sources/file.pdf",
            CancellationToken.None));
    }

    [Fact]
    public async Task Save_RejectsContentLargerThanLimit()
    {
        var store = CreateStore();
        await using var input = new MemoryStream(new byte[11]);

        await Assert.ThrowsAsync<BlobTooLargeException>(() => store.SaveAsync(
            "tenant/document/sources/file.pdf",
            input,
            "application/pdf",
            maximumBytes: 10,
            CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("/absolute.pdf")]
    public async Task StorageKey_CannotEscapeRoot(string storageKey)
    {
        var store = CreateStore();
        await using var input = new MemoryStream([1]);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            storageKey,
            input,
            "application/pdf",
            maximumBytes: 10,
            CancellationToken.None));
    }

    [Fact]
    public async Task Save_RejectsContentThatDoesNotMatchDeclaredType()
    {
        var store = CreateStore();
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("not a PDF"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            "tenant/document/sources/file.pdf",
            input,
            "application/pdf",
            maximumBytes: 1024,
            CancellationToken.None));

        Assert.Contains("does not match", exception.Message);
    }

    private LocalDocumentBlobStore CreateStore() =>
        new(new DocumentStorageOptions { RootPath = _rootPath });
}
