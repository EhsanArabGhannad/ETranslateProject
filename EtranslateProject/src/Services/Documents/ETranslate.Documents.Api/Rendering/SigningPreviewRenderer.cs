using System.Net;
using System.Text;
using ETranslate.Contracts.Trust;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ETranslate.Documents.Api.Rendering;

public interface ISigningPreviewRenderer
{
    Task<byte[]> RenderAsync(byte[] approvedUnsignedPdf, SigningPreviewSnapshotV1 plan, Guid previewId, CancellationToken ct);
}
public sealed class SigningPreviewRenderer(IDraftPdfRenderer renderer) : ISigningPreviewRenderer
{
    public async Task<byte[]> RenderAsync(byte[] approvedUnsignedPdf, SigningPreviewSnapshotV1 plan, Guid previewId, CancellationToken ct)
    {
        Validate(plan, previewId);
        var appendix = await renderer.RenderAsync(new DraftPdfInput(plan.DocumentId, plan.RevisionNumber, null,
            "{\"type\":\"doc\",\"content\":[]}", null, null, null, null, new Dictionary<Guid, PdfImage>())
            { PreviewPlan = plan, PreviewId = previewId }, ct);
        return Combine(approvedUnsignedPdf, appendix);
    }
    // Import existing translation pages without rerendering. Never overwrite the archived source.
    public static byte[] Combine(byte[] source, byte[] appendix)
    {
        if (source.LongLength is <= 0 or > DraftPdfPolicy.MaximumBytes || appendix.LongLength is <= 0 or > DraftPdfPolicy.MaximumBytes)
            throw new PdfRenderValidationException("Preview input exceeds PDF limits.");
        try
        {
            using var originalStream = new MemoryStream(source, writable: false); using var appendixStream = new MemoryStream(appendix, writable: false);
            using var original = PdfReader.Open(originalStream, PdfDocumentOpenMode.Import); using var extra = PdfReader.Open(appendixStream, PdfDocumentOpenMode.Import);
            if (original.PageCount < 1 || extra.PageCount < 1 || original.PageCount + extra.PageCount > 50)
                throw new PdfRenderValidationException("Preview must contain 1-50 pages, including its appendix.");
            using var output = new PdfDocument();
            output.Info.Title = "Signing proposal preview - UNSIGNED";
            output.Info.Subject = "Planning only; declarations are proposed, not accepted or signed. Not a final signable artifact.";
            output.Info.Creator = "ETranslate unsigned preview renderer";
            foreach (var page in original.Pages) output.AddPage(page);
            foreach (var page in extra.Pages) output.AddPage(page);
            using var buffer = new MemoryStream(); output.Save(buffer, closeStream: false);
            if (buffer.Length > DraftPdfPolicy.MaximumBytes) throw new PdfRenderValidationException("Preview exceeds PDF size limit.");
            return buffer.ToArray();
        }
        catch (PdfReaderException) { throw new PdfRenderValidationException("Preview PDF inputs cannot be read safely."); }
        catch (InvalidOperationException) { throw new PdfRenderValidationException("Preview PDF inputs cannot be read safely."); }
    }
    private static void Validate(SigningPreviewSnapshotV1 plan, Guid id)
    {
        if (!SigningPreviewPolicy.ValidSnapshot(plan, plan.TenantId, plan.TranslationJobId, plan.DocumentId, plan.Id) ||
            !SigningPreviewPolicy.CanGenerate(plan) || id == Guid.Empty) throw new PdfRenderValidationException("Invalid preview plan.");
    }
    public static string AppendixHtml(SigningPreviewSnapshotV1 plan, Guid previewId)
    {
        Validate(plan, previewId);
        string Encode(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder("<section class=\"proposal-appendix\" dir=\"ltr\"><h1>Signing proposal preview - UNSIGNED</h1><p class=\"proposal-warning\">Planning only. No signature, signer consent, office seal or notarization. NOT a final signable PDF.</p>");
        html.Append("<p dir=\"rtl\">پیوست برنامه‌ریزی - بدون امضا. بیانیه‌ها پیشنهادی هستند و به‌معنای پذیرش یا امضای شخص نیستند.</p>");
        html.Append($"<p>Preview ID: {previewId}<br>Preparation ID: {plan.Id}<br>Policy: {Encode(plan.SignaturePolicy)}<br>Review ID: {plan.ReviewId} (round {plan.ReviewRound})<br>Translation revision: {plan.RevisionNumber}<br>Source PDF ID: {plan.PdfVersionId}</p>");
        html.Append($"<p class=\"proposal-hash\">Source PDF SHA-256 (not this preview's hash):<br>{Encode(plan.PdfSha256)}</p>");
        html.Append($"<p>Prepared by account ID: {plan.CreatedByUserId}<br>Prepared at: {plan.CreatedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC</p>");
        html.Append("<p>This is a stored planning snapshot. Later cancellation or reopening is not reflected in this file. Original translation pages retain their original pagination; appendix pages are numbered separately. Check current status in the authenticated workspace.</p>");
        foreach (var stage in plan.Stages)
        {
            var role = stage.Role == "Translator" ? "Translator / مترجم" : "Translation office / دارالترجمه";
            html.Append($"<section class=\"proposal-stage\"><h2>{stage.Order}. {role}</h2><p>Proposed signer account ID: {stage.SignerUserId}<br>Requested method: {Encode(stage.RequestedMethod)} (not enabled)</p><h3>Proposed statement - NOT ACCEPTED / متن پیشنهادی</h3><p dir=\"auto\">{Encode(stage.ProposedStatement)}</p><p class=\"proposal-warning\">No signature has been applied to this stage.</p></section>");
        }
        return html.Append("</section>").ToString();
    }
}
