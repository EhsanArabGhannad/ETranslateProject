namespace ETranslate.StorageAudit;

public static class AuditPaths
{
    public static string ExistingRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute storage root is required.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (root == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!)) throw new ArgumentException("A drive root cannot be audited.");
        EnsureNoLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Storage root must already exist.");
        return root;
    }

    public static string ResolveKey(string root, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 500 || key.Contains('\\') || key.Contains(':') ||
            key.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || IsDeviceName(part) ||
                part.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c))))
            throw new ArgumentException("Invalid managed storage key.");
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(root, path)) throw new ArgumentException("Storage key escapes the root.");
        return path;
    }

    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9';
    }

    public static bool IsWithin(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void EnsureNoLinks(string path)
    {
        var current = new DirectoryInfo(Path.GetPathRoot(path)!);
        foreach (var part in Path.GetRelativePath(current.FullName, path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current.FullName, part);
            try { if ((File.GetAttributes(next) & FileAttributes.ReparsePoint) != 0) throw new UnsafeAuditPathException(); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = new DirectoryInfo(next);
        }
    }
}

public sealed class UnsafeAuditPathException : IOException;
