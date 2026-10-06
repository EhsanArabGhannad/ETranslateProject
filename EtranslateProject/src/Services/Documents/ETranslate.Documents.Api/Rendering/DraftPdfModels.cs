using ETranslate.Contracts.Documents;

namespace ETranslate.Documents.Api.Rendering;

public sealed record DraftPdfInput(Guid DocumentId, int RevisionNumber, Guid? TemplateRevisionId,
    string BodyJson, string? HeaderJson, string? FooterJson, string? LayoutJson, string? WatermarkJson,
    IReadOnlyDictionary<Guid, PdfImage> Images);
public sealed record PdfImage(string ContentType, byte[] Bytes);
public sealed record PreparedPdf(string Html, DocumentPageLayout Layout);
public interface IDraftPdfRenderer
{
    Task<byte[]> RenderAsync(DraftPdfInput input, CancellationToken cancellationToken);
}
public sealed class PdfRenderValidationException(string message) : Exception(message);
public sealed class PdfRendererUnavailableException(Exception inner) : Exception("PDF renderer is unavailable.", inner);
public sealed class PdfRendererBusyException : Exception;
public static class DraftPdfPolicy
{
    // Bump this whenever HTML/layout/font/browser changes can change rendered bytes.
    public const string RendererVersion = "draft-html-v7-pw1.63.0-noto-pdfsharp6.2.4";
    public const long MaximumBytes = 25 * 1024 * 1024;
    public const int MaximumTextLength = 100_000;
    public const int MaximumImages = 10;
    public const long MaximumImageBytes = 10 * 1024 * 1024;
}
