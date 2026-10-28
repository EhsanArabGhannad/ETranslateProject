using Microsoft.Playwright;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;

namespace ETranslate.Documents.Api.Rendering;

public sealed class ChromiumDraftPdfRenderer : IDraftPdfRenderer, IDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly Lazy<string> _fonts = new(LoadFonts);
    private readonly Lazy<string> _typographyFonts = new(() => LoadFonts()+LoadTypographyFonts());

    public async Task<byte[]> RenderAsync(DraftPdfInput input, CancellationToken cancellationToken)
    {
        var prepared = DraftPdfHtml.Prepare(input, input.BodyDescriptor.IsRichV2||input.BodyDescriptor.IsRichV3||input.BodyDescriptor.IsRichV4||input.BodyDescriptor.IsRichV5||input.BodyDescriptor.IsRichV6||input.BodyDescriptor.IsRichV7||input.BodyDescriptor.IsRichV8||input.TemplateDescriptor.IsRichV2||input.TemplateDescriptor.IsRichV3||input.TemplateDescriptor.IsRichV4||input.TemplateDescriptor.IsRichV5||input.TemplateDescriptor.IsRichV6||input.TemplateDescriptor.IsRichV7||input.TemplateDescriptor.IsRichV8 ? _typographyFonts.Value : _fonts.Value);
        if (!await _slot.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)) throw new PdfRendererBusyException();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var token = deadline.Token;
            using var playwright = await Playwright.CreateAsync();
            // No personal browser profiles, relaxed certificate checks, extensions or no-sandbox flags.
            await using var browser = await playwright.Chromium.LaunchAsync(new()
            { Headless = true, ChromiumSandbox = true, Timeout = 10000 });
            token.ThrowIfCancellationRequested();
            await using var context = await browser.NewContextAsync(new()
            { JavaScriptEnabled = false, AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block, Locale = "en-US" });
            await context.RouteAsync("**/*", route => route.AbortAsync());
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(10000);
            await page.SetContentAsync(prepared.Html, new() { WaitUntil = WaitUntilState.Load }).WaitAsync(token);
            // Trusted renderer code only: layout measurements, font/image readiness, never user scripts.
            var measurement = await page.EvaluateAsync<JsonElement>("""
                async () => {
                    await document.fonts.ready;
                    for (const image of document.images) await image.decode();
                    return {
                        HeaderMm: document.querySelector('.letterhead').getBoundingClientRect().height * 25.4 / 96,
                        FooterMm: document.querySelector('.letterfoot').getBoundingClientRect().height * 25.4 / 96,
                        BodyMm: document.querySelector('main').getBoundingClientRect().height * 25.4 / 96,
                        MaximumTableRowMm: Math.max(0, ...[...document.querySelectorAll('main table')].map(table => {
                            const header = table.tHead?.getBoundingClientRect().height ?? 0;
                            const row = Math.max(0, ...[...table.querySelectorAll('tbody tr')].map(row => row.getBoundingClientRect().height));
                            return (header + row) * 25.4 / 96;
                        })),
                        OversizedImage: [...document.images].some(image => image.naturalWidth * image.naturalHeight > 16000000),
                        HasTable: !!document.querySelector('main table')
                    };
                }
                """).WaitAsync(token);
            var metrics = measurement.Deserialize<PrintMetrics>()
                ?? throw new PdfRenderValidationException("Unable to measure the draft PDF layout.");
            if (metrics.HeaderMm > 45 || metrics.FooterMm > 35 || metrics.OversizedImage)
                throw new PdfRenderValidationException("Letterhead/footer or image exceeds draft PDF layout limits.");
            var headerReserve = metrics.HeaderMm > 0 ? decimal.Ceiling(metrics.HeaderMm) + 4 : 0;
            var footerReserve = metrics.FooterMm > 0 ? decimal.Ceiling(metrics.FooterMm) + 4 : 0;
            var layout = prepared.Layout;
            var bodyHeight = layout.DimensionsMm.Height - layout.Top - layout.Bottom - headerReserve - footerReserve - 8;
            if (bodyHeight < 60 || metrics.BodyMm / bodyHeight > 50)
                throw new PdfRenderValidationException("Page layout leaves too little space or the document exceeds the draft PDF length limit.");
            if (metrics.MaximumTableRowMm > bodyHeight)
                throw new PdfRenderValidationException("A table row with its repeated heading is taller than the printable page; shorten its cells before generating a PDF.");
            // Browser fragmentation is used only for body text, never for repeating letterhead/footer.
            await page.EvaluateAsync<bool>("""
                () => {
                    document.querySelector('.letterhead').style.display = 'none';
                    document.querySelector('.letterfoot').style.display = 'none';
                    return true;
                }
                """).WaitAsync(token);
            int? expectedPages = null;
            // Signing-preview appendices retain their existing rendering path/version. Old stored
            // PDFs are downloaded by identity; only new drafts use the new renderer identifier.
            if (input.PreviewPlan is null && metrics.HasTable)
                expectedPages = await DocumentRowPlanner.ApplyAsync(page,
                    (double)(layout.DimensionsMm.Width - layout.Left - layout.Right) * 96 / 25.4,
                    (double)bodyHeight * 96 / 25.4, (double)(layout.Left + layout.Right) * 96 / 25.4 + 28, token);
            var bytes = await page.PdfAsync(new()
            {
                Width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm",
                Height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
                Margin = new()
                {
                    Top = $"{DraftPdfHtml.Number(layout.Top + headerReserve)}mm", Right = $"{DraftPdfHtml.Number(layout.Right)}mm",
                    Bottom = $"{DraftPdfHtml.Number(layout.Bottom + footerReserve + 8)}mm", Left = $"{DraftPdfHtml.Number(layout.Left)}mm"
                },
                PrintBackground = true, DisplayHeaderFooter = true, HeaderTemplate = "<span></span>",
                FooterTemplate = $"<div style=\"font:8px sans-serif;width:100%;text-align:center;color:#843737\">{(input.PreviewPlan is null ? $"DRAFT - UNSIGNED | Revision {input.RevisionNumber} | Page" : "SIGNING PREVIEW - UNSIGNED | Appendix page")} <span class=\"pageNumber\"></span> of <span class=\"totalPages\"></span></div>",
                Tagged = true, Outline = false
            }).WaitAsync(token);
            if (bytes.LongLength is <= 0 or > DraftPdfPolicy.MaximumBytes) throw new PdfRenderValidationException("Rendered PDF exceeds the size limit.");
            ValidatePageCount(bytes, expectedPages);
            if (metrics.HeaderMm > 0 || metrics.FooterMm > 0)
            {
                await page.SetContentAsync(prepared.Html, new() { WaitUntil = WaitUntilState.Load }).WaitAsync(token);
                await page.EvaluateAsync<bool>("""
                    async layout => {
                        const header = document.querySelector('.letterhead'), footer = document.querySelector('.letterfoot');
                        document.body.append(header, footer);
                        document.querySelector('main').remove();
                        for (const mark of document.querySelectorAll('.watermark,.draft-watermark')) mark.remove();
                        Object.assign(document.body.style, { width:layout.width, height:layout.height });
                        Object.assign(header.style, { position:'absolute', top:layout.top, left:layout.left });
                        Object.assign(footer.style, { position:'absolute', bottom:layout.bottom, left:layout.left });
                        await document.fonts.ready;
                        for (const image of document.images) await image.decode();
                        return true;
                    }
                    """, new { width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm", height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
                        top = $"{DraftPdfHtml.Number(layout.Top)}mm", left = $"{DraftPdfHtml.Number(layout.Left)}mm", bottom = $"{DraftPdfHtml.Number(layout.Bottom + 8)}mm" }).WaitAsync(token);
                var letterhead = await page.PdfAsync(new()
                {
                    Width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm", Height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
                    Margin = new() { Top = "0", Right = "0", Bottom = "0", Left = "0" },
                    PrintBackground = true, DisplayHeaderFooter = false, Tagged = false
                }).WaitAsync(token);
                byte[]? earlierLetterhead = null;
                if (layout.FooterPlacement == "LastPage" && metrics.FooterMm > 0 && metrics.HeaderMm > 0)
                {
                    // Same measured geometry and reserved space, but no custom footer on earlier pages.
                    await page.EvaluateAsync<bool>("() => { document.querySelector('.letterfoot').style.display = 'none'; return true; }").WaitAsync(token);
                    earlierLetterhead = await page.PdfAsync(new()
                    {
                        Width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm", Height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
                        Margin = new() { Top = "0", Right = "0", Bottom = "0", Left = "0" },
                        PrintBackground = true, DisplayHeaderFooter = false, Tagged = false
                    }).WaitAsync(token);
                }
                bytes = ApplyLetterhead(bytes, letterhead, earlierLetterhead, layout.FooterPlacement == "LastPage" && metrics.FooterMm > 0, token);
            }
            else ValidatePageCount(bytes);
            if (bytes.LongLength > DraftPdfPolicy.MaximumBytes) throw new PdfRenderValidationException("Final PDF exceeds the size limit.");
            return bytes;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new PdfRendererUnavailableException(error); }
        catch (PlaywrightException error) { throw new PdfRendererUnavailableException(error); }
        finally { _slot.Release(); }
    }
    private sealed record PrintMetrics(decimal HeaderMm, decimal FooterMm, decimal BodyMm, decimal MaximumTableRowMm, bool OversizedImage, bool HasTable);
    private static byte[] ApplyLetterhead(byte[] body, byte[] letterhead, byte[]? earlierLetterhead, bool lastOnly, CancellationToken token)
    {
        using var bodyStream = new MemoryStream(body, writable:false);
        using var source = PdfReader.Open(bodyStream, PdfDocumentOpenMode.Import);
        if (source.PageCount > 50) throw new PdfRenderValidationException("Document exceeds 50 PDF pages.");
        using var document = new PdfDocument();
        document.Info.Title = source.Info.Title;
        document.Info.Creator = "ETranslate unsigned draft renderer";
        using var bodyFormStream = new MemoryStream(body, writable:false);
        using var bodyForm = XPdfForm.FromStream(bodyFormStream);
        using var letterheadStream = new MemoryStream(letterhead, writable:false);
        using var form = XPdfForm.FromStream(letterheadStream);
        if (form.PageCount != 1) throw new PdfRenderValidationException("Letterhead must fit one page.");
        using var earlierStream = earlierLetterhead is null ? null : new MemoryStream(earlierLetterhead, writable:false);
        using var earlierForm = earlierStream is null ? null : XPdfForm.FromStream(earlierStream);
        if (earlierForm is not null && earlierForm.PageCount != 1) throw new PdfRenderValidationException("Letterhead must fit one page.");
        for (var index = 0; index < source.PageCount; index++)
        {
            token.ThrowIfCancellationRequested();
            var pdfPage = document.AddPage();
            pdfPage.Width = source.Pages[index].Width;
            pdfPage.Height = source.Pages[index].Height;
            bodyForm.PageNumber = index + 1;
            // Each form has its own graphics state: body clipping/background cannot affect the letterhead.
            using var graphics = XGraphics.FromPdfPage(pdfPage);
            graphics.DrawImage(bodyForm, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
            if (!lastOnly || index == source.PageCount - 1) graphics.DrawImage(form, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
            else if (earlierForm is not null) graphics.DrawImage(earlierForm, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
        }
        using var output = new MemoryStream(); document.Save(output, closeStream:false); return output.ToArray();
    }
    private static void ValidatePageCount(byte[] bytes, int? expectedPages = null)
    {
        using var stream = new MemoryStream(bytes, writable:false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        if (document.PageCount > 50) throw new PdfRenderValidationException("Document exceeds 50 PDF pages.");
        if (expectedPages.HasValue && document.PageCount != expectedPages.Value)
            throw new PdfRenderValidationException("Printed table pages did not match the measured plan; no PDF was accepted.");
    }
    private static string LoadFonts()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Fonts");
        string Font(string family, string file, int weight, string style = "normal") =>
            $"@font-face{{font-family:'{family}';font-weight:{weight};font-style:{style};src:url(data:font/ttf;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(fontPath, file)))}) format('truetype');}}";
        return string.Join('\n', Font("Noto Sans", "NotoSans-Regular.ttf", 400), Font("Noto Sans", "NotoSans-Bold.ttf", 700),
            Font("Noto Sans", "NotoSans-Italic.ttf", 400, "italic"), Font("Noto Sans", "NotoSans-BoldItalic.ttf", 700, "italic"),
            Font("Noto Sans Arabic", "NotoSansArabic-Regular.ttf", 400), Font("Noto Sans Arabic", "NotoSansArabic-Bold.ttf", 700));
    }
    public void Dispose() => _slot.Dispose();
    private static string LoadTypographyFonts()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"Rendering","Fonts");
        string Face(string file,string style)=>$"@font-face{{font-family:'Noto Serif';font-weight:100 900;font-style:{style};src:url(data:font/ttf;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(path,file)))}) format('truetype');}}";
        return Face("NotoSerif-Variable.ttf","normal")+Face("NotoSerif-Italic-Variable.ttf","italic");
    }
}
