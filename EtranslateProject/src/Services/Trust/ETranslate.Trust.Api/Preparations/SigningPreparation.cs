namespace ETranslate.Trust.Api.Preparations;

public enum SigningPreparationStatus { Prepared = 0, Cancelled = 1 }
public enum RequestedSignatureMethod { EImza = 1, MobilImza = 2 }
public enum PlannedSignerRole { Translator = 1, TranslationOffice = 2 }

// Immutable planning metadata. This is never a signature, consent or signable final PDF.
public sealed class SigningPreparation
{
    private readonly List<PlannedSignatureStage> _stages = [];
    private SigningPreparation() { }
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid TranslationJobId { get; private init; }
    public Guid DocumentId { get; private init; }
    public Guid ReviewId { get; private init; }
    public int ReviewRound { get; private init; }
    public Guid PdfVersionId { get; private init; }
    public Guid DraftRevisionId { get; private init; }
    public int RevisionNumber { get; private init; }
    public Guid? TemplateRevisionId { get; private init; }
    public string PdfSha256 { get; private init; } = "";
    public long PdfSizeBytes { get; private init; }
    public string ArtifactKind { get; private init; } = "DraftUnsigned";
    public string SourceFilesJson { get; private init; } = "[]";
    public string SignaturePolicy { get; private init; } = "";
    public Guid ApprovedByUserId { get; private init; }
    public DateTimeOffset ApprovedAtUtc { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public SigningPreparationStatus Status { get; private set; }
    public long Version { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public IReadOnlyCollection<PlannedSignatureStage> Stages => _stages.AsReadOnly();

    public static SigningPreparation Create(PreparationArtifact artifact, string signaturePolicy, Guid creator, DateTimeOffset now,
        Guid translator, string translatorStatement, RequestedSignatureMethod translatorMethod,
        Guid? officeSigner, string? officeStatement, RequestedSignatureMethod? officeMethod)
    {
        if (creator == Guid.Empty || translator == Guid.Empty || artifact.TenantId == Guid.Empty || artifact.TranslationJobId == Guid.Empty ||
            artifact.DocumentId == Guid.Empty || artifact.ReviewId == Guid.Empty || artifact.PdfVersionId == Guid.Empty ||
            artifact.DraftRevisionId == Guid.Empty || artifact.ApprovedByUserId == Guid.Empty || artifact.RevisionNumber < 1 || artifact.ReviewRound < 1 ||
            artifact.Sha256 is not { Length: 64 } || !artifact.Sha256.All(Uri.IsHexDigit) || artifact.SizeBytes is <= 0 or > 25 * 1024 * 1024 ||
            artifact.Kind != "DraftUnsigned") throw new ArgumentException("Invalid internally approved artifact snapshot.");
        if (signaturePolicy is not ("TranslatorOnly" or "TranslatorAndTranslationOffice")) throw new ArgumentException("Unknown signature policy.");
        if (signaturePolicy == "TranslatorOnly" && (officeSigner is not null || officeStatement is not null || officeMethod is not null))
            throw new ArgumentException("Independent translation has no office stage.");
        if (signaturePolicy == "TranslatorAndTranslationOffice" && (officeSigner is null || officeSigner == Guid.Empty || officeMethod is null))
            throw new ArgumentException("Office signer, proposed statement and method are required.");
        var preparation = new SigningPreparation
        {
            Id = Guid.NewGuid(), TenantId = artifact.TenantId, TranslationJobId = artifact.TranslationJobId, DocumentId = artifact.DocumentId,
            ReviewId = artifact.ReviewId, ReviewRound = artifact.ReviewRound, PdfVersionId = artifact.PdfVersionId, DraftRevisionId = artifact.DraftRevisionId,
            RevisionNumber = artifact.RevisionNumber, TemplateRevisionId = artifact.TemplateRevisionId, PdfSha256 = artifact.Sha256.ToLowerInvariant(),
            PdfSizeBytes = artifact.SizeBytes, ArtifactKind = artifact.Kind, SourceFilesJson = artifact.SourceFilesJson,
            SignaturePolicy = signaturePolicy, ApprovedByUserId = artifact.ApprovedByUserId, ApprovedAtUtc = artifact.ApprovedAtUtc,
            CreatedByUserId = creator, CreatedAtUtc = now
        };
        preparation._stages.Add(new(preparation.Id, 1, PlannedSignerRole.Translator, translator, translatorStatement, translatorMethod));
        if (officeSigner.HasValue) preparation._stages.Add(new(preparation.Id, 2, PlannedSignerRole.TranslationOffice, officeSigner.Value, officeStatement!, officeMethod!.Value));
        return preparation;
    }
    public void Cancel(long expectedVersion, Guid actor, string reason, DateTimeOffset now)
    {
        if (Status != SigningPreparationStatus.Prepared || Version != expectedVersion) throw new InvalidOperationException("Preparation changed or is already cancelled.");
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new ArgumentException("Cancellation requires an actor and reason (maximum 2000 characters).");
        Status = SigningPreparationStatus.Cancelled; Version = checked(Version + 1);
        CancelledByUserId = actor; CancelledAtUtc = now; CancellationReason = reason.Trim();
    }
    public bool MatchesCurrentArtifact(Guid reviewId, Guid pdfId, string hash) => ReviewId == reviewId && PdfVersionId == pdfId &&
        PdfSha256.Equals(hash, StringComparison.OrdinalIgnoreCase);
}

public sealed record PreparationArtifact(Guid TenantId, Guid TranslationJobId, Guid DocumentId, Guid ReviewId, int ReviewRound,
    Guid PdfVersionId, Guid DraftRevisionId, int RevisionNumber, Guid? TemplateRevisionId, string Sha256, long SizeBytes,
    string Kind, Guid ApprovedByUserId, DateTimeOffset ApprovedAtUtc, string SourceFilesJson);
