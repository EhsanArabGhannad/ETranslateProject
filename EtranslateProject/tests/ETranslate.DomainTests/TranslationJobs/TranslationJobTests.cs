using ETranslate.TranslationWorkflow.Api.TranslationJobs;

namespace ETranslate.DomainTests.TranslationJobs;

public sealed class TranslationJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IndependentTranslator_RequiresOnlyTranslatorSignature()
    {
        var job = CreateJob(TenantProviderType.IndependentTranslator);

        Assert.Equal(SignaturePolicy.TranslatorOnly, job.SignaturePolicy);
    }

    [Fact]
    public void TranslationOffice_RequiresTranslatorAndOfficeSignatures()
    {
        var job = CreateJob(TenantProviderType.TranslationOffice);

        Assert.Equal(SignaturePolicy.TranslatorAndTranslationOffice, job.SignaturePolicy);
    }

    [Fact]
    public void AcceptanceProfile_IsOptional()
    {
        var job = CreateJob(
            TenantProviderType.IndependentTranslator,
            acceptanceProfile: null,
            acceptanceProfileOther: null);

        Assert.Null(job.AcceptanceProfile);
        Assert.Null(job.AcceptanceProfileOther);
    }

    [Theory]
    [InlineData(NotaryProcessingMode.Physical)]
    [InlineData(NotaryProcessingMode.Digital)]
    [InlineData(NotaryProcessingMode.Hybrid)]
    public void RequiredNotary_AcceptsEverySupportedProcessingMode(NotaryProcessingMode mode)
    {
        var job = CreateJob(
            TenantProviderType.TranslationOffice,
            notaryRequirement: NotaryRequirement.Required,
            notaryProcessingMode: mode);

        Assert.Equal(mode, job.NotaryProcessingMode);
    }

    [Fact]
    public void RequiredNotary_RequiresProcessingMode()
    {
        var exception = Assert.Throws<TranslationJobValidationException>(() =>
            CreateJob(
                TenantProviderType.TranslationOffice,
                notaryRequirement: NotaryRequirement.Required,
                notaryProcessingMode: null));

        Assert.Contains(nameof(TranslationJob.NotaryProcessingMode), exception.Errors.Keys);
    }

    [Fact]
    public void NotRequired_RejectsProcessingMode()
    {
        var exception = Assert.Throws<TranslationJobValidationException>(() =>
            CreateJob(
                TenantProviderType.IndependentTranslator,
                notaryRequirement: NotaryRequirement.NotRequired,
                notaryProcessingMode: NotaryProcessingMode.Physical));

        Assert.Contains(nameof(TranslationJob.NotaryProcessingMode), exception.Errors.Keys);
    }

    [Fact]
    public void OtherAcceptanceProfile_RequiresDescription()
    {
        var exception = Assert.Throws<TranslationJobValidationException>(() =>
            CreateJob(
                TenantProviderType.IndependentTranslator,
                acceptanceProfile: AcceptanceProfile.Other,
                acceptanceProfileOther: null));

        Assert.Contains(nameof(TranslationJob.AcceptanceProfileOther), exception.Errors.Keys);
    }

    [Fact]
    public void LanguageCodes_AreNormalizedAndMustDiffer()
    {
        var job = CreateJob(
            TenantProviderType.IndependentTranslator,
            sourceLanguageCode: "EN-us",
            targetLanguageCode: "TR");

        Assert.Equal("en-US", job.SourceLanguageCode);
        Assert.Equal("tr", job.TargetLanguageCode);

        Assert.Throws<TranslationJobValidationException>(() =>
            CreateJob(
                TenantProviderType.IndependentTranslator,
                sourceLanguageCode: "tr",
                targetLanguageCode: "TR"));
    }

    private static TranslationJob CreateJob(
        TenantProviderType providerType,
        string sourceLanguageCode = "en",
        string targetLanguageCode = "tr",
        NotaryRequirement notaryRequirement = NotaryRequirement.NotRequired,
        NotaryProcessingMode? notaryProcessingMode = null,
        AcceptanceProfile? acceptanceProfile = null,
        string? acceptanceProfileOther = null) =>
        TranslationJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            providerType,
            "Passport translation",
            sourceLanguageCode,
            targetLanguageCode,
            notaryRequirement,
            notaryProcessingMode,
            acceptanceProfile,
            acceptanceProfileOther,
            Now);
}
