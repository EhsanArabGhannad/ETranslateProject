using System.Text.Json;
using ETranslate.Contracts.Documents;

namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentTemplate
{
    public const int MaximumNameLength = 200;
    public const int MaximumDescriptionLength = 1_000;
    public const int MaximumEditorContentLength = 2_000_000;
    public const int MaximumHeaderOrFooterLength = 500_000;
    public const int MaximumPageLayoutLength = 50_000;
    public const int MaximumWatermarkLength = 100_000;

    private readonly List<DocumentTemplateRevision> _revisions = [];

    private DocumentTemplate()
    {
    }

    private DocumentTemplate(
        Guid tenantId,
        string name,
        string? description,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public int CurrentRevision { get; private set; }
    public Guid CreatedByUserId { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<DocumentTemplateRevision> Revisions => _revisions;

    public static DocumentTemplate Create(
        Guid tenantId,
        string name,
        string? description,
        string editorContentJson,
        string? headerContentJson,
        string? footerContentJson,
        string pageLayoutJson,
        string? watermarkJson,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc, string? contentFormat = null, int? contentSchemaVersion = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(createdByUserId, Guid.Empty);

        if (!DocumentContentDescriptor.TryResolveNext(DocumentContentDescriptor.Legacy, contentFormat, contentSchemaVersion, out var descriptor))
            throw ContentFormatError();
        var errors = Validate(
            name,
            description,
            editorContentJson,
            headerContentJson,
            footerContentJson,
            pageLayoutJson,
            watermarkJson, descriptor);
        if (errors.Count > 0)
        {
            throw new DocumentTemplateValidationException(errors);
        }

        var template = new DocumentTemplate(
            tenantId,
            name.Trim(),
            NormalizeOptional(description),
            createdByUserId,
            createdAtUtc);
        template.AddRevisionCore(
            editorContentJson,
            headerContentJson,
            footerContentJson,
            pageLayoutJson,
            watermarkJson,
            createdByUserId,
            createdAtUtc, descriptor);

        return template;
    }

    public DocumentTemplateRevision AddRevision(
        int expectedCurrentRevision,
        string editorContentJson,
        string? headerContentJson,
        string? footerContentJson,
        string pageLayoutJson,
        string? watermarkJson,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc, string? contentFormat = null, int? contentSchemaVersion = null,
        DocumentTemplateRevision? previousRevision = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(createdByUserId, Guid.Empty);

        if (!IsActive)
        {
            throw new InvalidOperationException("Archived templates cannot receive new revisions.");
        }

        if (expectedCurrentRevision != CurrentRevision)
        {
            throw new DocumentTemplateRevisionConflictException(
                expectedCurrentRevision,
                CurrentRevision);
        }

        var previous = previousRevision ?? _revisions.SingleOrDefault(r => r.RevisionNumber == CurrentRevision);
        if (previous is null || previous.TemplateId != Id || previous.RevisionNumber != CurrentRevision ||
            !DocumentContentDescriptor.TryResolveNext(previous.ContentDescriptor, contentFormat, contentSchemaVersion, out var descriptor))
            throw ContentFormatError();
        var errors = ValidateRevision(
            editorContentJson,
            headerContentJson,
            footerContentJson,
            pageLayoutJson,
            watermarkJson, descriptor);
        if (errors.Count > 0)
        {
            throw new DocumentTemplateValidationException(errors);
        }

        return AddRevisionCore(
            editorContentJson,
            headerContentJson,
            footerContentJson,
            pageLayoutJson,
            watermarkJson,
            createdByUserId,
            createdAtUtc, descriptor);
    }

    public void SetActive(bool isActive, DateTimeOffset updatedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = updatedAtUtc;
    }

    private DocumentTemplateRevision AddRevisionCore(
        string editorContentJson,
        string? headerContentJson,
        string? footerContentJson,
        string pageLayoutJson,
        string? watermarkJson,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc, DocumentContentDescriptor descriptor)
    {
        var revision = new DocumentTemplateRevision(
            Id,
            CurrentRevision + 1,
            editorContentJson,
            NormalizeOptional(headerContentJson),
            NormalizeOptional(footerContentJson),
            pageLayoutJson,
            NormalizeOptional(watermarkJson),
            createdByUserId,
            createdAtUtc, descriptor);

        _revisions.Add(revision);
        CurrentRevision = revision.RevisionNumber;
        UpdatedAtUtc = createdAtUtc;
        return revision;
    }

    private static Dictionary<string, string[]> Validate(
        string name,
        string? description,
        string editorContentJson,
        string? headerContentJson,
        string? footerContentJson,
        string pageLayoutJson,
        string? watermarkJson, DocumentContentDescriptor descriptor)
    {
        var errors = ValidateRevision(
            editorContentJson,
            headerContentJson,
            footerContentJson,
            pageLayoutJson,
            watermarkJson, descriptor);

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
        {
            errors[nameof(Name)] = [$"Template name is required and cannot exceed {MaximumNameLength} characters."];
        }

        if (description?.Trim().Length > MaximumDescriptionLength)
        {
            errors[nameof(Description)] = [$"Description cannot exceed {MaximumDescriptionLength} characters."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRevision(
        string editorContentJson,
        string? headerContentJson,
        string? footerContentJson,
        string pageLayoutJson,
        string? watermarkJson, DocumentContentDescriptor descriptor)
    {
        var errors = new Dictionary<string, string[]>();
        if (!descriptor.IsSupported) errors[nameof(DocumentTemplateRevision.ContentFormat)] = ["Unsupported content format/version."];
        if (descriptor.IsRichV1)
        {
            if (!descriptor.TryRead(editorContentJson, out _)) errors[nameof(DocumentTemplateRevision.EditorContentJson)] = ["Body does not conform to the declared content schema."];
            if (!string.IsNullOrWhiteSpace(headerContentJson) && !descriptor.TryRead(headerContentJson, out _, "header")) errors[nameof(DocumentTemplateRevision.HeaderContentJson)] = ["Header does not conform to the declared content schema."];
            if (!string.IsNullOrWhiteSpace(footerContentJson) && !descriptor.TryRead(footerContentJson, out _, "footer")) errors[nameof(DocumentTemplateRevision.FooterContentJson)] = ["Footer does not conform to the declared content schema."];
        }
        ValidateJson(
            errors,
            nameof(DocumentTemplateRevision.EditorContentJson),
            editorContentJson,
            MaximumEditorContentLength,
            required: true,
            JsonValueKind.Object,
            JsonValueKind.Array);
        ValidateJson(
            errors,
            nameof(DocumentTemplateRevision.HeaderContentJson),
            headerContentJson,
            MaximumHeaderOrFooterLength,
            required: false,
            JsonValueKind.Object,
            JsonValueKind.Array);
        ValidateJson(
            errors,
            nameof(DocumentTemplateRevision.FooterContentJson),
            footerContentJson,
            MaximumHeaderOrFooterLength,
            required: false,
            JsonValueKind.Object,
            JsonValueKind.Array);
        ValidateJson(
            errors,
            nameof(DocumentTemplateRevision.PageLayoutJson),
            pageLayoutJson,
            MaximumPageLayoutLength,
            required: true,
            JsonValueKind.Object);
        ValidateJson(
            errors,
            nameof(DocumentTemplateRevision.WatermarkJson),
            watermarkJson,
            MaximumWatermarkLength,
            required: false,
            JsonValueKind.Object);
        return errors;
    }

    private static void ValidateJson(
        IDictionary<string, string[]> errors,
        string fieldName,
        string? value,
        int maximumLength,
        bool required,
        params JsonValueKind[] allowedRootKinds)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors[fieldName] = ["A JSON value is required."];
            }

            return;
        }

        if (value.Length > maximumLength)
        {
            errors[fieldName] = [$"JSON content cannot exceed {maximumLength} characters."];
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (!allowedRootKinds.Contains(document.RootElement.ValueKind))
            {
                errors[fieldName] = ["JSON content has an unsupported root type."];
            }
        }
        catch (JsonException)
        {
            errors[fieldName] = ["Content must be valid JSON."];
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DocumentTemplateValidationException ContentFormatError() => new(new Dictionary<string, string[]>
    { [nameof(DocumentTemplateRevision.ContentFormat)] = ["Unsupported content format/version, conversion or missing current revision metadata."] });
}
