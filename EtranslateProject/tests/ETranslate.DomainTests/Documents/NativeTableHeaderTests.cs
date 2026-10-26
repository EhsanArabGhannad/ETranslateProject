using System.Text.Json;
using ETranslate.Contracts.Documents;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class NativePaginationFlowTests
{
    [NativePaginationFlowFact(Timeout = 30000)]
    public async Task Native_table_header_mechanisms_are_probed_without_product_changes()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_FLOW_OUTPUT")!);
        Directory.CreateDirectory(output);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block,
            ViewportSize = new() { Width = 1400, Height = 1000 } });
        await context.RouteAsync("**/*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.SetContentAsync("""
            <!doctype html><html><head><meta charset="utf-8"><style>
            *{box-sizing:border-box}body{margin:20px;font:14px Arial;color:#193a39}section{margin-bottom:30px}
            .flow{width:280px;height:240px;columns:1;column-width:280px;column-fill:auto;column-gap:24px}
            table{width:100%;border-collapse:collapse;table-layout:fixed}tr{break-inside:avoid;height:40px}
            th,td{padding:6px;border:1px solid #567869}th{background:#dcebe2}p{margin:0}
            .row-display>tr:first-child{display:table-header-group}
            .proper-head-avoid>thead{break-inside:avoid}
            .body-row-display>tbody>tr:first-child,.body-contents>tbody>tr:first-child{display:table-header-group}
            .body-contents>tbody{display:contents}
            </style></head><body><h1>Native table header control probe - synthetic only</h1></body></html>
            """);
        var data = await page.EvaluateAsync<string>("""
            () => {
                const result = {};
                for (const mode of ['proper-head','proper-head-avoid','row-display','row-groups','body-row-display','body-contents']) {
                    const section = document.createElement('section'), title = document.createElement('h2'); title.textContent = mode;
                    const flow = document.createElement('div'); flow.className = 'flow';
                    const table = document.createElement('table'); table.className = mode;
                    for (let i = 0; i < 22; i++) {
                        const row = document.createElement('tr'), cell = document.createElement(i === 0 ? 'th' : 'td');
                        const p = document.createElement('p'); p.textContent = i === 0 ? 'TITLE' : `ROW_${String(i).padStart(3,'0')}`;
                        cell.append(p); row.append(cell);
                        if (mode.startsWith('body-')) {
                            if (i === 0) table.append(document.createElement('tbody'));
                            table.firstElementChild.append(row);
                        }
                        else if (mode === 'row-display') table.append(row);
                        else if (i === 0 || mode === 'row-groups') { const group = document.createElement(i === 0 ? 'thead' : 'tbody'); group.append(row); table.append(group); }
                        else table.lastElementChild.append(row);
                    }
                    // Proper control has one THEAD and one TBODY, not body rows accidentally in THEAD.
                    if (mode.startsWith('proper-head')) {
                        const body = document.createElement('tbody');
                        for (const row of [...table.tHead.rows].slice(1)) body.append(row);
                        table.append(body);
                    }
                    flow.append(table); section.append(title,flow); document.body.append(section);
                    const range = document.createRange(); range.selectNodeContents(table.querySelector('th p'));
                    const origin = flow.getBoundingClientRect();
                    result[mode] = {
                        headerTextRects:[...range.getClientRects()].map(r => ({x:r.left-origin.left,y:r.top-origin.top,width:r.width,height:r.height})),
                        headerGroupRects:[...table.firstElementChild.getClientRects()].map(r => ({x:r.left-origin.left,y:r.top-origin.top,width:r.width,height:r.height})),
                        rows:[...table.querySelectorAll('td')].map(cell => { const r=cell.getBoundingClientRect(); return {text:cell.textContent,x:r.left-origin.left,y:r.top-origin.top}; })
                    };
                }
                return JSON.stringify(result);
            }
            """);
        using var report = JsonDocument.Parse(data);
        await File.WriteAllTextAsync(Path.Combine(output, "native-header-probe.json"), JsonSerializer.Serialize(report.RootElement, new JsonSerializerOptions { WriteIndented = true }));
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "native-header-probe.png"), FullPage = true });
        foreach (var mode in report.RootElement.EnumerateObject()) Assert.Equal(21, mode.Value.GetProperty("rows").GetArrayLength());
        foreach (var mode in new[] { "proper-head-avoid", "row-display", "body-contents" })
            Assert.True(report.RootElement.GetProperty(mode).GetProperty("headerTextRects").GetArrayLength() > 1);
    }

    private static async Task<object> VerifyTableEditing(IPage page, string output)
    {
        await page.EvaluateAsync("() => window.nativeFlowLab.load('long-table')");
        var initial = await Stable(page); var source = await Json(page);
        var textbox = page.Locator("#editor [role=textbox]");
        await textbox.EvaluateAsync("e => window.originalTableEditor = e");
        var rows = await textbox.Locator("tr").CountAsync();
        var secondMarker = initial.GetProperty("markers").EnumerateObject().First(p => p.Name.StartsWith("MARK_") && p.Value.GetInt32() == 2).Name;
        var bodyCell = textbox.Locator("td p").Filter(new() { HasText = secondMarker }).First;
        await page.EvaluateAsync("() => window.nativeFlowLab.jump(2)");
        await bodyCell.ClickAsync(); await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync(" CELL_EDIT آزمون");
        await Stable(page);
        Assert.True(DocumentContentJson.TryGetPlainText(await Json(page), out var bodyPlain));
        Assert.Contains(secondMarker + " CELL_EDIT آزمون", bodyPlain);
        await page.Keyboard.PressAsync("Control+z"); await Stable(page); Assert.Equal(source, await Json(page));
        await page.Keyboard.PressAsync("Control+Shift+z"); await Stable(page); Assert.Contains("CELL_EDIT", await Json(page));
        await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); await Stable(page); Assert.Equal(source, await Json(page));

        await page.EvaluateAsync("() => window.nativeFlowLab.jump(1)");
        await textbox.Locator("th p").First.ClickAsync(); await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync("_HEADER_EDIT");
        var editedHeader = await Json(page); Assert.Contains("Sıra_HEADER_EDIT", editedHeader);
        AssertTableHeaders(await Stable(page));
        await page.EvaluateAsync("() => window.nativeFlowLab.jump(2)");
        await Shot(page, output, "table-header-edited-page2");
        await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); await Stable(page); Assert.Equal(source, await Json(page));

        // A native repeated fragment is the same logical header, not a copied editable row.
        // Real pointer hit testing must target that header and never an unrelated body cell.
        await page.EvaluateAsync("() => window.nativeFlowLab.jump(2)");
        var click = await page.EvaluateAsync<double[]>("""
            () => {
                const r = document.querySelector('#editor [role=textbox]').getBoundingClientRect();
                const first = document.querySelector('#editor th').getBoundingClientRect();
                const g = window.nativeFlowLab.report().geometry, scale = window.nativeFlowLab.report().zoom;
                return [r.left + (g.pitch + 20) * scale, r.top + first.height / 2];
            }
            """);
        await page.Mouse.ClickAsync((float)click[0], (float)click[1]);
        await page.Keyboard.PressAsync("End"); await page.Keyboard.InsertTextAsync("_REPEATED_EDIT"); await Stable(page);
        using (var changed = JsonDocument.Parse(await Json(page))) {
            var table = changed.RootElement.GetProperty("content").EnumerateArray().Single(node => node.GetProperty("type").GetString() == "table");
            Assert.Contains("Sıra_REPEATED_EDIT", string.Concat(table.GetProperty("content")[0].GetProperty("content")[0]
                .GetProperty("content")[0].GetProperty("content").EnumerateArray().Select(node => node.GetProperty("text").GetString())));
        }
        await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); await Stable(page); Assert.Equal(source, await Json(page));

        // Exercise the actual toolbar route, not an immediate test hook after a synthetic End.
        // The latter can see DOM selection before the engine state has observed it.
        await bodyCell.ClickAsync(); await page.Locator("[data-command=addRowAfter]").ClickAsync();
        Assert.Equal(rows + 1, await textbox.Locator("tr").CountAsync()); AssertTableHeaders(await Stable(page));
        await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); await Stable(page); Assert.Equal(source, await Json(page));
        await bodyCell.ClickAsync(); await page.Locator("[data-command=addColumnAfter]").ClickAsync();
        Assert.Equal(3, await textbox.Locator("tr").First.Locator("th,td").CountAsync()); AssertTableHeaders(await Stable(page));
        await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); await Stable(page); Assert.Equal(source, await Json(page));
        await page.EvaluateAsync("() => window.nativeFlowLab.jump(1)");
        await textbox.Locator("th p").First.ClickAsync(); await page.Locator("[data-command=deleteRow]").ClickAsync();
        var withoutHeader = await Stable(page);
        Assert.False(withoutHeader.GetProperty("tables")[0].GetProperty("hasHeader").GetBoolean());
        Assert.Equal(0, withoutHeader.GetProperty("tables")[0].GetProperty("headerFragments").GetInt32());
        await page.EvaluateAsync("() => window.nativeFlowLab.undo()"); AssertTableHeaders(await Stable(page)); Assert.Equal(source, await Json(page));

        var selection = await page.EvaluateAsync<string>("""
            () => {
                const root = document.querySelector('#editor [role=textbox]'); const range = document.createRange(); range.selectNodeContents(root);
                const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range); const text = selection.toString(); selection.removeAllRanges();
                return text;
            }
            """);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(selection, "Sıra"));
        Assert.Equal(46, System.Text.RegularExpressions.Regex.Matches(selection, "MARK_\\d{3}").Count);
        foreach (var zoom in new[] { "75", "125", "fit" }) {
            await page.Locator("#zoom").SelectOptionAsync(zoom); AssertTableHeaders(await Stable(page)); Assert.Equal(source, await Json(page));
        }
        await page.Locator("#readonly").ClickAsync(); await Stable(page);
        await page.EvaluateAsync("() => window.nativeFlowLab.jump(2)");
        await bodyCell.ClickAsync(); await page.Keyboard.InsertTextAsync("MUST_NOT_EDIT");
        Assert.Equal(source, await Json(page));
        await page.Locator("#readonly").ClickAsync(); await Stable(page);
        Assert.True(await textbox.EvaluateAsync<bool>("e => e === window.originalTableEditor"));
        return new { BodyCellEditUndoRedo = true, OriginalHeaderEdit = true, RepeatedHeaderHitTestAndEdit = true,
            RowAndColumnCommands = true, HeaderDeleteUndo = true, SelectedHeaderOccursOnce = true,
            ZoomAndReadonlyPreserveJson = true, SameEditorDom = true };
    }
    private static void AssertTableHeaders(JsonElement report)
    {
        foreach (var table in report.GetProperty("tables").EnumerateArray()) {
            Assert.True(table.GetProperty("repetitionMatches").GetBoolean(), table.GetRawText());
            Assert.Equal(1, table.GetProperty("bodyGroups").GetInt32());
        }
    }
}
