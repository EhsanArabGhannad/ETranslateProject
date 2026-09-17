namespace ETranslate.Documents.Api.Documents;

public static class SourceFilePolicy
{
    public const long MaximumFileSizeBytes = 25 * 1024 * 1024;
    public const int MaximumSignatureLength = 8;

    private static readonly IReadOnlyDictionary<string, string> ExtensionsByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/tiff"] = ".tiff"
        };

    public static bool IsSupportedContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && ExtensionsByContentType.ContainsKey(contentType);

    public static string GetExtension(string contentType) =>
        ExtensionsByContentType.TryGetValue(contentType, out var extension)
            ? extension
            : throw new ArgumentException("The source file type is not supported.", nameof(contentType));

    public static bool HasValidSignature(ReadOnlySpan<byte> content, string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "application/pdf" => content.StartsWith("%PDF-"u8),
            "image/jpeg" => content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            "image/png" => content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/tiff" => content.StartsWith(new byte[] { 0x49, 0x49, 0x2A, 0x00 }) ||
                            content.StartsWith(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }),
            _ => false
        };
}
