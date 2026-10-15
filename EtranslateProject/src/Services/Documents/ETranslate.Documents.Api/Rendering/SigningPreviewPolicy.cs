using ETranslate.Contracts.Trust;

namespace ETranslate.Documents.Api.Rendering;

public static class SigningPreviewPolicy
{
    public const string Kind = "SigningPreviewUnsigned";
    public const string RendererVersion = "signing-preview-v2-letterhead";
    public static bool ValidSnapshot(SigningPreviewSnapshotV1? plan, Guid tenant, Guid job, Guid document, Guid preparation)
    {
        if (plan is null || plan.Id != preparation || plan.TenantId != tenant || plan.TranslationJobId != job || plan.DocumentId != document ||
            new[] { tenant, job, document, preparation, plan.ReviewId, plan.PdfVersionId, plan.DraftRevisionId, plan.CreatedByUserId }.Any(id => id == Guid.Empty) ||
            plan.ArtifactKind != "DraftUnsigned" || plan.ReviewRound < 1 || plan.RevisionNumber < 1 ||
            plan.PdfSha256 is not { Length: 64 } || !plan.PdfSha256.All(Uri.IsHexDigit) || plan.PdfSizeBytes is <= 0 or > DraftPdfPolicy.MaximumBytes ||
            plan.SignaturePolicy is not ("TranslatorOnly" or "TranslatorAndTranslationOffice") || plan.Stages is null ||
            plan.Stages.Count != (plan.SignaturePolicy == "TranslatorOnly" ? 1 : 2)) return false;
        for (var index = 0; index < plan.Stages.Count; index++)
        {
            var stage = plan.Stages[index];
            if (stage is null || stage.Order != index + 1 || stage.Role != (index == 0 ? "Translator" : "TranslationOffice") || stage.SignerUserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(stage.ProposedStatement) || stage.ProposedStatement.Length > 4000 || stage.RequestedMethod is not ("EImza" or "MobilImza")) return false;
        }
        return true;
    }
    public static bool CanGenerate(SigningPreviewSnapshotV1 plan) => plan.Status == "Prepared" && plan.Version == 0;
}
