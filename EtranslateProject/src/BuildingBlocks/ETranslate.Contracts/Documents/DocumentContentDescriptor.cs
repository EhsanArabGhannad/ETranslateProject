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
    public static readonly DocumentContentDescriptor RichV4 = new(RichFormat, 4);
    public static readonly DocumentContentDescriptor RichV5 = new(RichFormat, 5);
    public static readonly DocumentContentDescriptor RichV6 = new(RichFormat, 6);
    public static readonly DocumentContentDescriptor RichV7 = new(RichFormat, 7);
    public static readonly DocumentContentDescriptor RichV8 = new(RichFormat, 8);
    public static readonly DocumentContentDescriptor RichV9 = new(RichFormat, 9);
    public bool IsRichV9 => Format == RichFormat && SchemaVersion == 9;
    public static readonly DocumentContentDescriptor RichV10 = new(RichFormat, 10);
    public bool IsRichV10 => Format == RichFormat && SchemaVersion == 10;
    public bool IsLegacy => Format is null && SchemaVersion is null;
    public bool IsRichV1 => Format == RichFormat && SchemaVersion == RichSchemaVersion;
    public bool IsRichV2 => Format == RichFormat && SchemaVersion == 2;
    public bool IsRichV3 => Format == RichFormat && SchemaVersion == 3;
    public bool IsRichV4 => Format == RichFormat && SchemaVersion == 4;
    public bool IsRichV5 => Format == RichFormat && SchemaVersion == 5;
    public bool IsRichV6 => Format == RichFormat && SchemaVersion == 6;
    public bool IsRichV7 => Format == RichFormat && SchemaVersion == 7;
    public bool IsRichV8 => Format == RichFormat && SchemaVersion == 8;
    public bool IsSupported => IsLegacy || IsRichV1 || IsRichV2 || IsRichV3 || IsRichV4 || IsRichV5 || IsRichV6 || IsRichV7 || IsRichV8 || IsRichV9 || IsRichV10;
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
        // Each version retains its vocabulary; v10 adds bounded manual page starts on body text blocks.
        return (Format, SchemaVersion) switch
        {
            (null, null) => ReadV1Vocabulary(json, out plainText, rootType),
            (RichFormat, RichSchemaVersion) => ReadV1Vocabulary(json, out plainText, rootType),
            (RichFormat, 2) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 2) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 2),
            (RichFormat, 3) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 3) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 3),
            (RichFormat, 4) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 4) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 4),
            (RichFormat, 5) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 5) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 5),
            (RichFormat, 6) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 6) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 6),
            (RichFormat, 7) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 7) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 7),
            (RichFormat, 8) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 8) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 8),
            (RichFormat, 9) => DocumentContentJson.TryGetPlainText(json, out plainText, rootType, 9) ||
                rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText, "doc", 9),
            (RichFormat, 10) => ReadV10Vocabulary(json, out plainText, rootType),
            _ => false
        };
    }

    private static bool ReadV10Vocabulary(string json, out string? text, string rootType)
    {
        var read = DocumentContentJson.TryGetPlainText(json, out text, rootType, 10) ||
            rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out text, "doc", 10);
        if (!read || rootType == "doc") return read;
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        if (DocumentPageBreak.Count(parsed.RootElement) == 0) return true;
        text = null; return false;
    }

    private static bool ReadV1Vocabulary(string json, out string? plainText, string rootType)
    {
        if (DocumentContentJson.TryGetPlainText(json, out plainText, rootType)) return true;
        // Early simple letterheads used a doc root. Preserve that already-supported representation.
        return rootType != "doc" && DocumentContentJson.TryGetPlainText(json, out plainText);
    }
}
