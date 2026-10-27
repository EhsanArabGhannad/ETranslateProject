namespace ETranslate.Contracts.Documents;

// Content schema version is independent of the draft revision, editor vendor and PDF renderer version.
// NULL/NULL is deliberately legacy, not an automatic assertion that old arbitrary JSON is rich v1.
public sealed record DocumentContentDescriptor(string? Format, int? SchemaVersion)
{
    public const string RichFormat = "etranslate.rich-document";
    public const int RichSchemaVersion = 1;
    public const string LegacyDisplayFormat = "etranslate.legacy-json";
    public static readonly DocumentContentDescriptor Legacy = new(null, null);
    public static readonly DocumentContentDescriptor RichV1 = new(RichFormat, RichSchemaVersion);
    public static readonly DocumentContentDescriptor RichV2 = new(RichFormat, 2);
    public static readonly DocumentContentDescriptor RichV3 = new(RichFormat, 3);
    public bool IsLegacy => Format is null && SchemaVersion is null;
    public bool IsRichV1 => Format == RichFormat && SchemaVersion == RichSchemaVersion;
    public bool IsRichV2 => Format == RichFormat && SchemaVersion == 2;
    public bool IsRichV3 => Format == RichFormat && SchemaVersion == 3;
    public bool IsSupported => IsLegacy || IsRichV1 || IsRichV2 || IsRichV3;
    public string DisplayFormat => Format ?? LegacyDisplayFormat;
    public int DisplaySchemaVersion => SchemaVersion ?? 0;

    public static bool TryResolveNext(DocumentContentDescriptor current, string? requestedFormat,
        int? requestedVersion, out DocumentContentDescriptor next)
    {
        next = new(requestedFormat, requestedVersion);
        if (!current.IsSupported) return false;
        // Old clients may omit both fields. Never downgrade an already-declared rich revision to legacy.
        if (next.IsLegacy) { next = current; return true; }
        return next.IsSupported && !next.IsLegacy && (current.IsLegacy || next.SchemaVersion >= current.SchemaVersion);
    }

    public bool TryRead(string? json, out string? plainText, string rootType = "doc")
    {
        plainText = null;
        if (json is null || rootType is not ("doc" or "header" or "footer")) return false;
        // Explicit dispatch: legacy rendering remains a narrow, safe subset, not generic JSON flattening.
        // v1 remains strict; v2 adds bounded typography; v3 adds bounded paragraph layout.
        return (Format, SchemaVersion) switch
        {
            (null, null) => ReadV1Vocabulary(json, out plainText, rootType),
            (RichFormat, RichSchemaVersion) => ReadV1Vocabulary(json, out plainText, rootType),
            (RichFormat, 2) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 2) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 2),
            (RichFormat, 3) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 3) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 3),
            _ => false
        };
    }

    private static bool ReadV1Vocabulary(string json, out string? plainText, string rootType)
    {
        if (DocumentContentJson.TryGetPlainText(json, out plainText, rootType)) return true;
        // Early simple letterheads used a doc root. Preserve that already-supported representation.
        return rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText);
    }
}
