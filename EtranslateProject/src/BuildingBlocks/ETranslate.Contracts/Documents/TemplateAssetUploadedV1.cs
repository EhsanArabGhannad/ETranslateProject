namespace ETranslate.Contracts.Documents;

public sealed record TemplateAssetUploadedV1(
    Guid EventId, Guid AssetId, Guid TemplateId, Guid TenantId,
    string Sha256, long SizeBytes, Guid UploadedByUserId, DateTimeOffset OccurredAtUtc);
