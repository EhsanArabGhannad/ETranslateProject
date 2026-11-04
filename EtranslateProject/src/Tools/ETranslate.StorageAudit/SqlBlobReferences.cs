using ETranslate.Documents.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.StorageAudit;

public static class SqlBlobReferences
{
    // Include every retained asset, not just assets used by the latest draft/template/PDF.
    // No service startup, migrations, SaveChanges or mutation commands run here.
    public static async Task<ReferenceSnapshot> ReadAsync(DocumentsDbContext db, int maximum, CancellationToken token)
    {
        if (maximum is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximum));
        var rows = await db.SourceFiles.AsNoTracking().Select(x => new {
            Kind = "Source", AssetId = x.Id, OwnerId = x.DocumentId, x.StorageKey, x.SizeBytes, x.Sha256, RecordedAtUtc = x.UploadedAtUtc
        }).Concat(db.DocumentImages.AsNoTracking().Select(x => new {
            Kind = "DocumentImage", AssetId = x.Id, OwnerId = x.DocumentId, x.StorageKey, x.SizeBytes, x.Sha256, RecordedAtUtc = x.UploadedAtUtc
        })).Concat(db.TemplateAssets.AsNoTracking().Select(x => new {
            Kind = "TemplateAsset", AssetId = x.Id, OwnerId = x.TemplateId, x.StorageKey, x.SizeBytes, x.Sha256, RecordedAtUtc = x.UploadedAtUtc
        })).Concat(db.PdfVersions.AsNoTracking().Select(x => new {
            Kind = "DraftPdf", AssetId = x.Id, OwnerId = x.DocumentId, x.StorageKey, x.SizeBytes, x.Sha256, RecordedAtUtc = x.CreatedAtUtc
        })).Concat(db.SigningPreviews.AsNoTracking().Select(x => new {
            Kind = "SigningPreview", AssetId = x.Id, OwnerId = x.DocumentId, x.StorageKey, x.SizeBytes, x.Sha256, RecordedAtUtc = x.CreatedAtUtc
        })).OrderBy(x => x.StorageKey).ThenBy(x => x.Kind).ThenBy(x => x.AssetId).Take(maximum + 1).ToArrayAsync(token);
        return new(rows.Take(maximum).Select(x => new BlobReference(x.Kind, x.AssetId, x.OwnerId, x.StorageKey, x.SizeBytes, x.Sha256, x.RecordedAtUtc)).ToArray(), rows.Length <= maximum);
    }
}
