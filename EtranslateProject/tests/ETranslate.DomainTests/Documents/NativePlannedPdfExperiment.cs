using System.Text.Json;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

// QA-only candidate. Not registered with DI, endpoints or a product renderer-version identifier.
internal static class NativePlannedPdfExperiment
{
    internal sealed record Result(byte[] Bytes, JsonElement Plan);
    private static string Fonts()
    {
        string Face(string family, string file, int weight, string style = "normal") =>
            $"@font-face{{font-family:'{family}';font-weight:{weight};font-style:{style};src:url(data:font/ttf;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Fonts", file)))}) format('truetype');}}";
        return string.Join('\n', Face("Noto Sans", "NotoSans-Regular.ttf", 400), Face("Noto Sans", "NotoSans-Bold.ttf", 700),
            Face("Noto Sans", "NotoSans-Italic.ttf", 400, "italic"), Face("Noto Sans", "NotoSans-BoldItalic.ttf", 700, "italic"),
            Face("Noto Sans Arabic", "NotoSansArabic-Regular.ttf", 400), Face("Noto Sans Arabic", "NotoSansArabic-Bold.ttf", 700));
    }
    internal static async Task<Result> Render(IPage page, DraftPdfInput input, string planner)
    {
        if (input.PreviewPlan is not null) throw new PdfRenderValidationException("Signing appendices are outside the row-plan experiment.");
        var prepared = DraftPdfHtml.Prepare(input, Fonts());
        var layout = prepared.Layout;
        await page.SetContentAsync(prepared.Html);
        var measured = await page.EvaluateAsync<JsonElement>("""
            async () => {
                await document.fonts.ready;
                for (const image of document.images) await image.decode();
                return {header:document.querySelector('.letterhead').getBoundingClientRect().height*25.4/96,
                    footer:document.querySelector('.letterfoot').getBoundingClientRect().height*25.4/96,
                    row:Math.max(0,...[...document.querySelectorAll('main table')].map(table=>{
                        const rows=[...table.rows], first=rows[0];
                        const header=first?.cells.length && [...first.cells].every(cell=>cell.tagName==='TH') ? rows.shift().getBoundingClientRect().height : 0;
                        return Math.max(header,...rows.map(row=>header+row.getBoundingClientRect().height));
                    }))*25.4/96};
            }
            """);
        var header = measured.GetProperty("header").GetDecimal(); var footer = measured.GetProperty("footer").GetDecimal();
        if (header > 45 || footer > 35) throw new PdfRenderValidationException("Template exceeds the experimental layout limits.");
        var headerReserve = header > 0 ? decimal.Ceiling(header) + 4 : 0;
        var footerReserve = footer > 0 ? decimal.Ceiling(footer) + 4 : 0;
        var height = layout.DimensionsMm.Height - layout.Top - layout.Bottom - headerReserve - footerReserve - 8;
        if (height < 60 || measured.GetProperty("row").GetDecimal() > height)
            throw new PdfRenderValidationException("A table row with its repeated heading is taller than the printable page.");
        await page.EvaluateAsync("() => {document.querySelector('.letterhead').style.display='none';document.querySelector('.letterfoot').style.display='none';}");
        var geometry = new { contentWidth = (double)(layout.DimensionsMm.Width - layout.Left - layout.Right) * 96 / 25.4,
            contentHeight = (double)height * 96 / 25.4, columnGap = (double)(layout.Left + layout.Right) * 96 / 25.4 + 28 };
        var plan = await page.EvaluateAsync<JsonElement>("geometry => { " + planner + "; return NativeTableRows.prepareRowPlan(document.querySelector('main'),geometry); }", geometry);
        var bytes = await page.PdfAsync(new() {
            Width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm", Height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
            Margin = new() { Top = $"{DraftPdfHtml.Number(layout.Top + headerReserve)}mm", Right = $"{DraftPdfHtml.Number(layout.Right)}mm",
                Bottom = $"{DraftPdfHtml.Number(layout.Bottom + footerReserve + 8)}mm", Left = $"{DraftPdfHtml.Number(layout.Left)}mm" },
            PrintBackground = true, DisplayHeaderFooter = true, HeaderTemplate = "<span></span>", Tagged = true,
            FooterTemplate = "<div style=\"font:8px sans-serif;width:100%;text-align:center;color:#843737\">EXPERIMENTAL - UNSIGNED | Page <span class=\"pageNumber\"></span> of <span class=\"totalPages\"></span></div>" });
        if (header > 0 || footer > 0)
        {
            await page.SetContentAsync(prepared.Html);
            await page.EvaluateAsync("""
                async value => {
                    const header=document.querySelector('.letterhead'),footer=document.querySelector('.letterfoot');
                    document.body.append(header,footer);document.querySelector('main').remove();
                    for(const mark of document.querySelectorAll('.watermark,.draft-watermark'))mark.remove();
                    Object.assign(document.body.style,{width:value.width,height:value.height});
                    Object.assign(header.style,{position:'absolute',top:value.top,left:value.left});
                    Object.assign(footer.style,{position:'absolute',bottom:value.bottom,left:value.left});
                    await document.fonts.ready;for(const image of document.images)await image.decode();
                }
                """, new { width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm", height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
                top = $"{DraftPdfHtml.Number(layout.Top)}mm", left = $"{DraftPdfHtml.Number(layout.Left)}mm", bottom = $"{DraftPdfHtml.Number(layout.Bottom + 8)}mm" });
            var options = new PagePdfOptions { Width = $"{DraftPdfHtml.Number(layout.DimensionsMm.Width)}mm", Height = $"{DraftPdfHtml.Number(layout.DimensionsMm.Height)}mm",
                Margin = new() { Top = "0", Right = "0", Bottom = "0", Left = "0" }, PrintBackground = true, DisplayHeaderFooter = false };
            var overlay = await page.PdfAsync(options); byte[]? earlier = null;
            if (layout.FooterPlacement == "LastPage" && footer > 0 && header > 0) {
                await page.EvaluateAsync("() => {document.querySelector('.letterfoot').style.display='none';}"); earlier = await page.PdfAsync(options);
            }
            bytes = Overlay(bytes, overlay, earlier, layout.FooterPlacement == "LastPage" && footer > 0);
        }
        if (bytes.Length > DraftPdfPolicy.MaximumBytes) throw new PdfRenderValidationException("Experimental PDF is too large.");
        return new(bytes, plan);
    }
    private static byte[] Overlay(byte[] body, byte[] overlay, byte[]? earlier, bool lastOnly)
    {
        using var bodyStream = new MemoryStream(body); using var source = PdfReader.Open(bodyStream, PdfDocumentOpenMode.Import);
        using var bodyFormStream = new MemoryStream(body); using var bodyForm = XPdfForm.FromStream(bodyFormStream);
        using var overlayStream = new MemoryStream(overlay); using var overlayForm = XPdfForm.FromStream(overlayStream);
        using var earlierStream = earlier is null ? null : new MemoryStream(earlier); using var earlierForm = earlierStream is null ? null : XPdfForm.FromStream(earlierStream);
        if (overlayForm.PageCount != 1 || earlierForm is { PageCount: not 1 } || source.PageCount > 50)
            throw new PdfRenderValidationException("Experimental template or body exceeds page limits.");
        using var document = new PdfDocument(); document.Info.Title = "EXPERIMENTAL - UNSIGNED row plan";
        for (var i = 0; i < source.PageCount; i++) {
            var page = document.AddPage(); page.Width = source.Pages[i].Width; page.Height = source.Pages[i].Height; bodyForm.PageNumber = i + 1;
            using var graphics = XGraphics.FromPdfPage(page); graphics.DrawImage(bodyForm, 0, 0, page.Width.Point, page.Height.Point);
            if (!lastOnly || i == source.PageCount - 1) graphics.DrawImage(overlayForm, 0, 0, page.Width.Point, page.Height.Point);
            else if (earlierForm is not null) graphics.DrawImage(earlierForm, 0, 0, page.Width.Point, page.Height.Point);
        }
        using var result = new MemoryStream(); document.Save(result, closeStream:false); return result.ToArray();
    }
}
