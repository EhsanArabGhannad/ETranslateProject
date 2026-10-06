namespace ETranslate.Contracts.Documents;

// Internal review only; consumers must not interpret this event as an e-signature or notarization.
public sealed record DocumentReviewChangedV1(Guid EventId, Guid TenantId, Guid TranslationJobId, Guid DocumentId,
    Guid ReviewId, int ReviewRound, int RevisionNumber, Guid PdfVersionId, string PdfSha256,
    string Status, Guid ActorUserId, DateTimeOffset OccurredAtUtc);
