namespace ETranslate.Contracts.Trust;

// Authenticated planning snapshot; never a provider authorization, signature or accepted declaration.
public sealed record SigningPreviewSnapshotV1(Guid Id, Guid TenantId, Guid TranslationJobId, Guid DocumentId,
    string Status, long Version, string SignaturePolicy, Guid ReviewId, int ReviewRound, Guid PdfVersionId,
    Guid DraftRevisionId, int RevisionNumber, Guid? TemplateRevisionId, string PdfSha256, long PdfSizeBytes,
    string ArtifactKind, Guid CreatedByUserId, DateTimeOffset CreatedAtUtc, IReadOnlyList<SigningPreviewStageV1> Stages);
public sealed record SigningPreviewStageV1(int Order, string Role, Guid SignerUserId, string ProposedStatement, string RequestedMethod);
