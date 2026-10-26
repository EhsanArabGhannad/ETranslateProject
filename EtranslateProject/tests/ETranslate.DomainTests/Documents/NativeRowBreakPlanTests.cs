using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed partial class NativePaginationFlowTests
{
    [NativePaginationFlowFact(Timeout = 60000)]
    public async Task Explicit_row_boundaries_are_compared_with_native_print_fragmentation()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_OUTPUT")!);
        Directory.CreateDirectory(output);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block });
        await context.RouteAsync("**/*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        var report = new Dictionary<string, object>();
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_ROOT")!);
        var planner = await File.ReadAllTextAsync(Path.Combine(root, "generated", "table-row-plan.js"));
        object P(string value) => new { type = "paragraph", attrs = new { dir = "ltr" }, content = new[] { new { type = "text", text = value } } };
        object Cell(string value, bool header = false) => new { type = header ? "tableHeader" : "tableCell", content = new[] { P(value) } };
        object Row(object first, object second) => new { type = "tableRow", content = new[] { first, second } };
        string FontData(string file) => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Fonts", file)));
        var fontCss = string.Join('\n', new[] { ("Noto Sans", "NotoSans-Regular.ttf", 400), ("Noto Sans", "NotoSans-Bold.ttf", 700),
            ("Noto Sans Arabic", "NotoSansArabic-Regular.ttf", 400), ("Noto Sans Arabic", "NotoSansArabic-Bold.ttf", 700) }
            .Select(font => $"@font-face{{font-family:'{font.Item1}';font-weight:{font.Item3};src:url(data:font/ttf;base64,{FontData(font.Item2)})}}"));
        foreach (var id in new[] { "unheaded", "mixed", "headed" })
        {
            var rows = new List<object> { Row(Cell("Sıra", id != "unheaded"), Cell("Sentetik açıklama", id == "headed")) };
            for (var i = 1; i <= 46; i++) rows.Add(Row(Cell($"MARK_{i:000}"), Cell(string.Concat(Enumerable.Repeat("İstanbul - جدول ساختگی / Türkçe. ", 3)))));
            var source = JsonSerializer.Serialize(new { type = "doc", content = new object[] {
                new { type = "heading", attrs = new { level = 2, dir = "ltr" }, content = new[] { new { type = "text", text = "SENTETİK BELGE / آزمایش ترجمه" } } },
                new { type = "table", content = rows }, P("TABLE_END_MARKER") } });
            var layout = """{"pageSize":"A4","orientation":"Portrait","marginsMm":{"top":25,"right":20,"bottom":20,"left":20}}""";
            var input = new DraftPdfInput(Guid.NewGuid(), 1, null, source, null, null, layout, null, new Dictionary<Guid, PdfImage>())
                { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 1 };
            var html = DraftPdfHtml.Prepare(input, fontCss).Html;
            await page.SetContentAsync(html);
            await page.EvaluateAsync("() => document.fonts.ready.then(()=>true)");
            var guards = await page.EvaluateAsync<JsonElement>("() => { " + planner + """
                ; const root=document.querySelector('main'),text=root.textContent,style=root.style.cssText;
                const plan=NativeTableRows.measureRowPlan(root,{contentWidth:170*96/25.4,contentHeight:244*96/25.4,columnGap:40});
                // An absent vs. empty style attribute is equivalent; verify the actual CSSOM,
                // including that temporary column geometry has been removed, not HTML byte spelling.
                const afterStyle=root.style.cssText, unchangedText=root.textContent===text;
                const restored=afterStyle===style && unchangedText;
                NativeTableRows.applyRowPlan(root,plan);NativeTableRows.applyRowPlan(root,plan);
                const count=[...root.querySelectorAll('tr')].filter(row=>row.style.breakBefore==='page').length;
                root.querySelector('td p,th p').append(document.createTextNode(' STALE'));
                let rejected=false;try{NativeTableRows.applyRowPlan(root,plan)}catch{rejected=true}
                return {restored,rejected,count,style,afterStyle,unchangedText,expected:plan.tables.reduce((n,table)=>n+table.breakBeforeRows.length,0)};
                }
                """);
            Assert.True(guards.GetProperty("restored").GetBoolean(), guards.GetRawText()); Assert.True(guards.GetProperty("rejected").GetBoolean(), guards.GetRawText());
            Assert.Equal(guards.GetProperty("expected").GetInt32(), guards.GetProperty("count").GetInt32());
            var variants = new Dictionary<string, object>();
            foreach (var mode in new[] { "baseline", "row-plan" })
            {
                await page.SetContentAsync(html);
                await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
                var plan = await page.EvaluateAsync<JsonElement>("""
                    mode => {
                        const root=document.querySelector('main'), width=170*96/25.4, height=244*96/25.4, pitch=width+40;
                        const before=root.getAttribute('style');
                        const headings=[...root.querySelectorAll('thead')];
                        for(const heading of headings) heading.style.breakInside='avoid';
                        Object.assign(root.style,{width:width+'px',height:height+'px',columnCount:'1',columnWidth:width+'px',columnGap:'40px',columnFill:'auto'});
                        const origin=root.getBoundingClientRect().left;
                        const rows=[...root.querySelectorAll('tbody tr')].map((row,index)=>({row,index,page:Math.floor((row.getClientRects()[0].left-origin+1)/pitch)+1,fragments:row.getClientRects().length}));
                        const breaks=rows.filter((row,i)=>i>0 && row.page>rows[i-1].page);
                        const pages=Math.ceil((root.scrollWidth+40-1)/pitch);
                        if(before===null)root.removeAttribute('style');else root.setAttribute('style',before);
                        for(const heading of headings) heading.style.removeProperty('break-inside');
                        if(mode==='row-plan') for(const entry of breaks) entry.row.style.breakBefore='page';
                        return {pages,rows:rows.map(({row,...value})=>({...value,text:row.cells[0].textContent})),breaks:breaks.map(row=>row.index)};
                    }
                    """, mode);
                var bytes = await page.PdfAsync(new() { Width = "210mm", Height = "297mm",
                    Margin = new() { Top = "25mm", Right = "20mm", Bottom = "28mm", Left = "20mm" }, PrintBackground = true,
                    DisplayHeaderFooter = true, HeaderTemplate = "<span></span>", FooterTemplate = "<span></span>", Tagged = true });
                await File.WriteAllBytesAsync(Path.Combine(output, $"row-plan-{id}-{mode}.pdf"), bytes);
                using var stream = new MemoryStream(bytes); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                if (mode == "row-plan") Assert.Equal(plan.GetProperty("pages").GetInt32(), pdf.PageCount);
                variants[mode] = new { Plan = plan, PdfPages = pdf.PageCount };
            }
            report[id] = variants;
        }
        await File.WriteAllTextAsync(Path.Combine(output, "row-plan-probe.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
