namespace ETranslate.Documents.Api.Documents;

public sealed class TemplateAsset
{
    public const long MaximumSizeBytes = 5 * 1024 * 1024;

    private TemplateAsset() { }

    public Guid Id { get; private init; }
    public Guid TemplateId { get; private init; }
    public string OriginalFileName { get; private init; } = string.Empty;
    public string ContentType { get; private init; } = string.Empty;
    public long SizeBytes { get; private init; }
    public string Sha256 { get; private init; } = string.Empty;
    public string StorageKey { get; private init; } = string.Empty;
    public Guid UploadedByUserId { get; private init; }
    public DateTimeOffset UploadedAtUtc { get; private init; }

    public static bool IsSupportedContentType(string contentType) =>
        contentType is "image/png" or "image/jpeg";

    public static TemplateAsset Create(
        Guid id, Guid templateId, string fileName, string contentType, long sizeBytes,
        string sha256, string storageKey, Guid userId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(templateId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        var errors = new Dictionary<string, string[]>();
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 255)
            errors[nameof(OriginalFileName)] = ["File name is required and cannot exceed 255 characters."];
        if (!IsSupportedContentType(contentType))
            errors[nameof(ContentType)] = ["Only PNG and JPEG template images are supported."];
        if (sizeBytes <= 0 || sizeBytes > MaximumSizeBytes)
            errors[nameof(SizeBytes)] = ["Template images must be between 1 byte and 5 MiB."];
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            errors[nameof(Sha256)] = ["SHA-256 must contain 64 hexadecimal characters."];
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500)
            errors[nameof(StorageKey)] = ["Storage key is required and cannot exceed 500 characters."];
        if (errors.Count > 0) throw new DocumentTemplateValidationException(errors);

        return new TemplateAsset
        {
            Id = id,
            TemplateId = templateId,
            OriginalFileName = safeName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256.ToLowerInvariant(),
            StorageKey = storageKey,
            UploadedByUserId = userId,
            UploadedAtUtc = now
        };
    }
}
