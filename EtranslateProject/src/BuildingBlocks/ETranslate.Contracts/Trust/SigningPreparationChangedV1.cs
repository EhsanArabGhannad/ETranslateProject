namespace ETranslate.Contracts.Trust;

public sealed record SigningPreparationChangedV1(Guid EventId, Guid PreparationId, Guid TenantId, Guid TranslationJobId,
    Guid DocumentId, Guid ReviewId, Guid PdfVersionId, string PdfSha256, string Status, long Version,
    Guid ActorUserId, DateTimeOffset OccurredAtUtc);
