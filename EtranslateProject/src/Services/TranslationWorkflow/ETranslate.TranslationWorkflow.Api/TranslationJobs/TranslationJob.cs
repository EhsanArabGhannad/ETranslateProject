using System.Text.RegularExpressions;

namespace ETranslate.TranslationWorkflow.Api.TranslationJobs;

public sealed partial class TranslationJob
{
    private TranslationJob()
    {
    }

    private TranslationJob(
        Guid tenantId,
        Guid createdByUserId,
        TenantProviderType providerType,
        string title,
        string sourceLanguageCode,
        string targetLanguageCode,
        NotaryRequirement notaryRequirement,
        NotaryProcessingMode? notaryProcessingMode,
        AcceptanceProfile? acceptanceProfile,
        string? acceptanceProfileOther,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        CreatedByUserId = createdByUserId;
        ProviderType = providerType;
        SignaturePolicy = providerType == TenantProviderType.IndependentTranslator
            ? SignaturePolicy.TranslatorOnly
            : SignaturePolicy.TranslatorAndTranslationOffice;
        Status = TranslationJobStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;

        ApplyDraftDetails(
            title,
            sourceLanguageCode,
            targetLanguageCode,
            notaryRequirement,
            notaryProcessingMode,
            acceptanceProfile,
            acceptanceProfileOther);
    }

    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public TenantProviderType ProviderType { get; private init; }
    public SignaturePolicy SignaturePolicy { get; private init; }
    public TranslationJobStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string SourceLanguageCode { get; private set; } = string.Empty;
    public string TargetLanguageCode { get; private set; } = string.Empty;
    public NotaryRequirement NotaryRequirement { get; private set; }
    public NotaryProcessingMode? NotaryProcessingMode { get; private set; }
    public AcceptanceProfile? AcceptanceProfile { get; private set; }
    public string? AcceptanceProfileOther { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TranslationJob Create(
        Guid tenantId,
        Guid createdByUserId,
        TenantProviderType providerType,
        string title,
        string sourceLanguageCode,
        string targetLanguageCode,
        NotaryRequirement notaryRequirement,
        NotaryProcessingMode? notaryProcessingMode,
        AcceptanceProfile? acceptanceProfile,
        string? acceptanceProfileOther,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(createdByUserId, Guid.Empty);

        if (!Enum.IsDefined(providerType))
        {
            throw new ArgumentOutOfRangeException(nameof(providerType));
        }

        return new TranslationJob(
            tenantId,
            createdByUserId,
            providerType,
            title,
            sourceLanguageCode,
            targetLanguageCode,
            notaryRequirement,
            notaryProcessingMode,
            acceptanceProfile,
            acceptanceProfileOther,
            createdAtUtc);
    }

    public void UpdateDraft(
        string title,
        string sourceLanguageCode,
        string targetLanguageCode,
        NotaryRequirement notaryRequirement,
        NotaryProcessingMode? notaryProcessingMode,
        AcceptanceProfile? acceptanceProfile,
        string? acceptanceProfileOther,
        DateTimeOffset updatedAtUtc)
    {
        if (Status != TranslationJobStatus.Draft)
        {
            throw new InvalidOperationException("Only draft translation jobs can be edited.");
        }

        ApplyDraftDetails(
            title,
            sourceLanguageCode,
            targetLanguageCode,
            notaryRequirement,
            notaryProcessingMode,
            acceptanceProfile,
            acceptanceProfileOther);
        UpdatedAtUtc = updatedAtUtc;
    }

    private void ApplyDraftDetails(
        string title,
        string sourceLanguageCode,
        string targetLanguageCode,
        NotaryRequirement notaryRequirement,
        NotaryProcessingMode? notaryProcessingMode,
        AcceptanceProfile? acceptanceProfile,
        string? acceptanceProfileOther)
    {
        var errors = Validate(
            title,
            sourceLanguageCode,
            targetLanguageCode,
            notaryRequirement,
            notaryProcessingMode,
            acceptanceProfile,
            acceptanceProfileOther);

        if (errors.Count > 0)
        {
            throw new TranslationJobValidationException(errors);
        }

        Title = title.Trim();
        SourceLanguageCode = NormalizeLanguageCode(sourceLanguageCode);
        TargetLanguageCode = NormalizeLanguageCode(targetLanguageCode);
        NotaryRequirement = notaryRequirement;
        NotaryProcessingMode = notaryProcessingMode;
        AcceptanceProfile = acceptanceProfile;
        AcceptanceProfileOther = string.IsNullOrWhiteSpace(acceptanceProfileOther)
            ? null
            : acceptanceProfileOther.Trim();
    }

    private static Dictionary<string, string[]> Validate(
        string title,
        string sourceLanguageCode,
        string targetLanguageCode,
        NotaryRequirement notaryRequirement,
        NotaryProcessingMode? notaryProcessingMode,
        AcceptanceProfile? acceptanceProfile,
        string? acceptanceProfileOther)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            errors[nameof(Title)] = ["Title is required and cannot exceed 200 characters."];
        }

        if (!IsValidLanguageCode(sourceLanguageCode))
        {
            errors[nameof(SourceLanguageCode)] = ["Source language must be a valid language code, such as tr or en-US."];
        }

        if (!IsValidLanguageCode(targetLanguageCode))
        {
            errors[nameof(TargetLanguageCode)] = ["Target language must be a valid language code, such as tr or en-US."];
        }
        else if (IsValidLanguageCode(sourceLanguageCode) &&
                 string.Equals(
                     NormalizeLanguageCode(sourceLanguageCode),
                     NormalizeLanguageCode(targetLanguageCode),
                     StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(TargetLanguageCode)] = ["Source and target languages must be different."];
        }

        if (!Enum.IsDefined(notaryRequirement))
        {
            errors[nameof(NotaryRequirement)] = ["Notary requirement is invalid."];
        }
        else if (notaryRequirement == NotaryRequirement.Required && notaryProcessingMode is null)
        {
            errors[nameof(NotaryProcessingMode)] = ["A notary processing mode is required when notarization is required."];
        }
        else if (notaryRequirement != NotaryRequirement.Required && notaryProcessingMode is not null)
        {
            errors[nameof(NotaryProcessingMode)] = ["Notary processing mode must be empty unless notarization is required."];
        }
        else if (notaryProcessingMode is not null && !Enum.IsDefined(notaryProcessingMode.Value))
        {
            errors[nameof(NotaryProcessingMode)] = ["Notary processing mode is invalid."];
        }

        if (acceptanceProfile is not null && !Enum.IsDefined(acceptanceProfile.Value))
        {
            errors[nameof(AcceptanceProfile)] = ["Acceptance profile is invalid."];
        }
        else if (acceptanceProfile == TranslationJobs.AcceptanceProfile.Other &&
                 (string.IsNullOrWhiteSpace(acceptanceProfileOther) || acceptanceProfileOther.Trim().Length > 200))
        {
            errors[nameof(AcceptanceProfileOther)] = ["Other acceptance profile is required and cannot exceed 200 characters."];
        }
        else if (acceptanceProfile != TranslationJobs.AcceptanceProfile.Other &&
                 !string.IsNullOrWhiteSpace(acceptanceProfileOther))
        {
            errors[nameof(AcceptanceProfileOther)] = ["Other acceptance profile must be empty unless profile is Other."];
        }

        return errors;
    }

    private static bool IsValidLanguageCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && LanguageCodePattern().IsMatch(value.Trim());

    private static string NormalizeLanguageCode(string value)
    {
        var parts = value.Trim().Split('-');
        parts[0] = parts[0].ToLowerInvariant();

        for (var index = 1; index < parts.Length; index++)
        {
            parts[index] = parts[index].Length == 2
                ? parts[index].ToUpperInvariant()
                : parts[index].ToLowerInvariant();
        }

        return string.Join('-', parts);
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageCodePattern();
}
