namespace ETranslate.Web.Models;

// UI-side guard; Documents remains the authoritative MIME/signature/size validator.
public static class SourceFileRules
{
    public const long MaximumBytes = 25 * 1024 * 1024;
    public static bool IsAllowed(string? contentType, long size) =>
        size is > 0 and <= MaximumBytes && contentType is
            "application/pdf" or "image/png" or "image/jpeg" or "image/tiff";
}
