using System.Text.RegularExpressions;

namespace ETranslate.IdentityAccess.Api.Identity;

public static partial class TenantSlug
{
    public static string Create(string? requestedSlug, TenantType tenantType)
    {
        if (string.IsNullOrWhiteSpace(requestedSlug))
        {
            var prefix = tenantType == TenantType.IndependentTranslator ? "translator" : "office";
            return $"{prefix}-{Guid.NewGuid():N}"[..23];
        }

        var normalized = requestedSlug.Trim().ToLowerInvariant();

        if (!ValidSlug().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Slug must be 3-63 characters and contain only lowercase letters, numbers, and hyphens.",
                nameof(requestedSlug));
        }

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$")]
    private static partial Regex ValidSlug();
}
