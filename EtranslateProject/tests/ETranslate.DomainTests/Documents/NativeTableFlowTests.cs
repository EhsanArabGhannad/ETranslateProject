using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class NativePaginationFlowTests
{
    private static async Task ProbeUnheadedTableFlow(IPage page, string output)
    {
        var results = new Dictionary<string, object>();
        foreach (var id in new[] { "table-no-header", "table-mixed-header" })
        {
            await page.EvaluateAsync("id => window.nativeFlowLab.load(id)", id); await Stable(page);
            var source = await Json(page); var variants = new Dictionary<string, object>();
            foreach (var (name, rule) in new[] {
                ("baseline", "#editor table { --control: unchanged }"),
                ("body-contents", "#editor table>tbody{display:contents!important}"),
                ("row-auto", "#editor tr{break-inside:auto!important}"),
                ("normal-space", "#editor table{white-space:normal!important}"),
                ("hide-columns", "#editor colgroup{display:none!important}"),
                ("orphans-one", "#editor tr{break-inside:auto!important}#editor td p,#editor th p{orphans:1!important;widows:1!important}"),
                ("body-contents-row-auto", "#editor table>tbody{display:contents!important}#editor tr{break-inside:auto!important}") })
            {
                // Trusted test CSS in the existing same-origin sheet; keep the lab CSP intact.
                var rules = await page.EvaluateAsync<int>("""
                    css => {
                        const sheet=document.styleSheets[0], start=sheet.cssRules.length;
                        for (const rule of css.match(/[^}]+}/g)) sheet.insertRule(rule,sheet.cssRules.length);
                        return start;
                    }
                    """, rule);
                try {
                    await page.Locator("#zoom").SelectOptionAsync("100");
                    var report = await Stable(page);
                    var metrics = await page.EvaluateAsync<JsonElement>("""
                        () => {
                            const root=document.querySelector('#editor [role=textbox]'), table=root.querySelector('table');
                            const clone=root.cloneNode(true); clone.style.width=window.nativeFlowLab.report().geometry.contentWidth+'px';
                            document.querySelector('#measure').append(clone);
                            const rows=[...clone.querySelectorAll('tr')].slice(0,3).map(row=>({height:row.getBoundingClientRect().height,width:row.getBoundingClientRect().width}));
                            clone.remove();
                            return {rows, html:table.outerHTML.slice(0,2000), breakInside:getComputedStyle(table.rows[1]).breakInside,
                                orphans:getComputedStyle(table.rows[1].cells[0].firstElementChild).orphans,
                                firstCells:[...table.rows[1].cells].map(cell=>({width:cell.getBoundingClientRect().width,font:getComputedStyle(cell).font,whiteSpace:getComputedStyle(cell).whiteSpace}))};
                        }
                        """);
                    Assert.Equal(source, await Json(page));
                    variants[name] = new { Report = report, Metrics = metrics };
                } finally { await page.EvaluateAsync("start => { const sheet=document.styleSheets[0]; while(sheet.cssRules.length>start) sheet.deleteRule(sheet.cssRules.length-1); }", rules); }
            }
            using var fixture = JsonDocument.Parse(await page.EvaluateAsync<string>("() => JSON.stringify(window.nativeFlowLab.fixture())"));
            var input = new DraftPdfInput(Guid.NewGuid(), 1, null, source, null, null,
                fixture.RootElement.GetProperty("layout").GetRawText(), null, new Dictionary<Guid, PdfImage>())
                { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 1 };
            string FontData(string file) => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Fonts", file)));
            var fontCss = string.Join('\n', new[] { ("Noto Sans", "NotoSans-Regular.ttf", 400), ("Noto Sans", "NotoSans-Bold.ttf", 700),
                ("Noto Sans Arabic", "NotoSansArabic-Regular.ttf", 400), ("Noto Sans Arabic", "NotoSansArabic-Bold.ttf", 700) }
                .Select(font => $"@font-face{{font-family:'{font.Item1}';font-weight:{font.Item3};src:url(data:font/ttf;base64,{FontData(font.Item2)})}}"));
            var print = await page.Context.NewPageAsync();
            var printHtml = DraftPdfHtml.Prepare(input, fontCss).Html;
            await print.SetContentAsync(printHtml);
            var printMetrics = await print.EvaluateAsync<JsonElement>("""
                async geometryJson => {
                    const geometry=JSON.parse(geometryJson);
                    await document.fonts.ready;
                    const table=document.querySelector('main table');
                    const plain={html:table.outerHTML.slice(0,2000), rows:[...table.rows].slice(0,3).map(row=>({height:row.getBoundingClientRect().height,width:row.getBoundingClientRect().width})),
                        firstCells:[...table.rows[1].cells].map(cell=>({width:cell.getBoundingClientRect().width,font:getComputedStyle(cell).font,whiteSpace:getComputedStyle(cell).whiteSpace}))};
                    const main=document.querySelector('main');
                    Object.assign(main.style,{height:geometry.contentHeight+'px',width:geometry.contentWidth+'px',columnCount:'1',columnWidth:geometry.contentWidth+'px',columnGap:geometry.columnGap+'px',columnFill:'auto'});
                    const columns=()=>({pages:Math.ceil((main.scrollWidth+geometry.columnGap-1)/geometry.pitch),
                        marker18:Math.floor((table.rows[18].cells[0].getBoundingClientRect().left-main.getBoundingClientRect().left+1)/geometry.pitch)+1});
                    const withoutColgroup=columns();
                    const group=document.createElement('colgroup');
                    for(let i=0;i<2;i++){const col=document.createElement('col');col.style.minWidth='25px';group.append(col);}
                    table.prepend(group);
                    const withColgroup=columns();
                    for(const row of table.rows) row.style.breakInside='auto';
                    for(const p of table.querySelectorAll('p')) {p.style.orphans='1';p.style.widows='1';}
                    return {plain,withoutColgroup,withColgroup,withRowAuto:columns()};
                }
                """, (await Stable(page)).GetProperty("geometry").GetRawText());
            var printControls = new Dictionary<string, object>();
            foreach (var (name, css) in new[] { ("print-control", ""), ("cell-avoid", "th,td{break-inside:avoid}"),
                ("row-groups", "tbody{display:contents}tbody>tr{display:table-row-group;break-inside:avoid}") })
            {
                await print.SetContentAsync(printHtml.Replace("</style>", css + "</style>"));
                await print.EvaluateAsync("() => document.fonts.ready.then(() => true)");
                var bytes = await print.PdfAsync(new() {
                    Width = "210mm", Height = "297mm", Margin = new() { Top = "25mm", Right = "20mm", Bottom = "28mm", Left = "20mm" },
                    PrintBackground = true, DisplayHeaderFooter = true, HeaderTemplate = "<span></span>", FooterTemplate = "<span></span>", Tagged = true });
                await File.WriteAllBytesAsync(Path.Combine(output, id + "-" + name + ".pdf"), bytes);
                using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                printControls[name] = new { Pages = pdf.PageCount, Css = css, ProductionRendererChanged = false };
            }
            await print.CloseAsync();
            results[id] = new { Variants = variants, PrintMetrics = printMetrics, PrintControls = printControls };
        }
        await File.WriteAllTextAsync(Path.Combine(output, "unheaded-flow-probe.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        await page.Locator("#zoom").SelectOptionAsync("fit"); await Stable(page);
    }
}
