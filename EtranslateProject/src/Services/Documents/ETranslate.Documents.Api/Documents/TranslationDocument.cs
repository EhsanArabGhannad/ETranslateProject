using System.Text.Json;

namespace ETranslate.Documents.Api.Documents;

public sealed class TranslationDocument
{
    public const int MaximumEditorContentLength = 2_000_000;
    public const int MaximumPlainTextLength = 1_000_000;

    private readonly List<DocumentDraftRevision> _draftRevisions = [];
    private readonly List<SourceFile> _sourceFiles = [];

    private TranslationDocument()
    {
    }

    private TranslationDocument(
        Guid tenantId,
        Guid translationJobId,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        TranslationJobId = translationJobId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid TranslationJobId { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public int CurrentDraftRevision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<DocumentDraftRevision> DraftRevisions => _draftRevisions;
    public IReadOnlyCollection<SourceFile> SourceFiles => _sourceFiles;

    public static TranslationDocument Create(
        Guid tenantId,
        Guid translationJobId,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(translationJobId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(createdByUserId, Guid.Empty);

        return new TranslationDocument(tenantId, translationJobId, createdByUserId, createdAtUtc);
    }

    public DocumentDraftRevision AddDraftRevision(
        int expectedCurrentRevision,
        string editorContentJson,
        string? plainText,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(createdByUserId, Guid.Empty);

        if (expectedCurrentRevision != CurrentDraftRevision)
        {
            throw new DocumentRevisionConflictException(expectedCurrentRevision, CurrentDraftRevision);
        }

        var errors = ValidateDraft(editorContentJson, plainText);
        if (errors.Count > 0)
        {
            throw new DocumentValidationException(errors);
        }

        var revision = new DocumentDraftRevision(
            Id,
            CurrentDraftRevision + 1,
            editorContentJson,
            string.IsNullOrWhiteSpace(plainText) ? null : plainText,
            createdByUserId,
            createdAtUtc);

        _draftRevisions.Add(revision);
        CurrentDraftRevision = revision.RevisionNumber;
        UpdatedAtUtc = createdAtUtc;

        return revision;
    }

    public SourceFile AddSourceFile(
        Guid sourceFileId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string storageKey,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sourceFileId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(uploadedByUserId, Guid.Empty);

        var errors = ValidateSourceFile(
            originalFileName,
            contentType,
            sizeBytes,
            sha256,
            storageKey);
        if (errors.Count > 0)
        {
            throw new DocumentValidationException(errors);
        }

        var sourceFile = new SourceFile(
            sourceFileId,
            Id,
            Path.GetFileName(originalFileName),
            contentType,
            sizeBytes,
            sha256.ToLowerInvariant(),
            storageKey,
            uploadedByUserId,
            uploadedAtUtc);

        _sourceFiles.Add(sourceFile);
        UpdatedAtUtc = uploadedAtUtc;
        return sourceFile;
    }

    private static Dictionary<string, string[]> ValidateDraft(string editorContentJson, string? plainText)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(editorContentJson) ||
            editorContentJson.Length > MaximumEditorContentLength)
        {
            errors[nameof(DocumentDraftRevision.EditorContentJson)] =
                [$"Editor content is required and cannot exceed {MaximumEditorContentLength} characters."];
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(editorContentJson);
                if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    errors[nameof(DocumentDraftRevision.EditorContentJson)] =
                        ["Editor content must be a JSON object or array."];
                }
            }
            catch (JsonException)
            {
                errors[nameof(DocumentDraftRevision.EditorContentJson)] = ["Editor content must be valid JSON."];
            }
        }

        if (plainText?.Length > MaximumPlainTextLength)
        {
            errors[nameof(DocumentDraftRevision.PlainText)] =
                [$"Plain text cannot exceed {MaximumPlainTextLength} characters."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateSourceFile(
        string originalFileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string storageKey)
    {
        var errors = new Dictionary<string, string[]>();
        var safeFileName = Path.GetFileName(originalFileName);

        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 255)
        {
            errors[nameof(SourceFile.OriginalFileName)] =
                ["File name is required and cannot exceed 255 characters."];
        }

        if (!SourceFilePolicy.IsSupportedContentType(contentType))
        {
            errors[nameof(SourceFile.ContentType)] = ["The source file type is not supported."];
        }

        if (sizeBytes <= 0 || sizeBytes > SourceFilePolicy.MaximumFileSizeBytes)
        {
            errors[nameof(SourceFile.SizeBytes)] =
                [$"Source file must be between 1 byte and {SourceFilePolicy.MaximumFileSizeBytes} bytes."];
        }

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            errors[nameof(SourceFile.Sha256)] = ["SHA-256 must contain 64 hexadecimal characters."];
        }

        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500)
        {
            errors[nameof(SourceFile.StorageKey)] =
                ["Storage key is required and cannot exceed 500 characters."];
        }

        return errors;
    }
}
