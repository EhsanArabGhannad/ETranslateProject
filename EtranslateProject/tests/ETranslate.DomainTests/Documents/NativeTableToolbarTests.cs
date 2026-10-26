using System.Text.Json;
using System.Text.Json.Nodes;
using ETranslate.Contracts.Documents;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class NativePaginationFlowTests
{
    private static JsonArray TableRows(JsonNode doc) => doc["content"]!.AsArray().Single(node => node!["type"]!.GetValue<string>() == "table")!["content"]!.AsArray();
    private static string NodeText(JsonNode? node) => node?["type"]?.GetValue<string>() == "text" ? node["text"]!.GetValue<string>()
        : node?["content"] is JsonArray children ? string.Concat(children.Select(NodeText)) : "";

    private static async Task<object> VerifyDirectTableToolbar(IPage page, string output)
    {
        var results = new List<object>();
        foreach (var id in new[] { "long-table", "table-no-header", "table-mixed-header" })
        {
            await page.EvaluateAsync("id => window.nativeFlowLab.load(id)", id);
            var report = await Stable(page); var source = await Json(page);
            var canonical = JsonNode.Parse(source)!.ToJsonString();
            var marker = report.GetProperty("markers").EnumerateObject().First(p => p.Name.StartsWith("MARK_") && p.Value.GetInt32() == 2).Name;
            var sourceRows = TableRows(JsonNode.Parse(source)!);
            var index = sourceRows.Select((node, i) => (node, i)).Single(pair => NodeText(pair.node).Contains(marker)).i;
            var cell = page.Locator("#editor td p").Filter(new() { HasText = marker }).First;
            foreach (var command in new[] { "addRowAfter", "addColumnAfter", "deleteRow", "deleteColumn", "deleteTable" })
            {
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    await page.EvaluateAsync("() => window.nativeFlowLab.jump(2)");
                    // Real cell -> toolbar clicks. No End, caret injection, selection hook or sleep between them.
                    await cell.ClickAsync(); await page.Locator($"[data-command={command}]").ClickAsync();
                    await Stable(page); var changed = await Json(page);
                    Assert.True(DocumentContentJson.TryGetPlainText(changed, out _));
                    var actual = JsonNode.Parse(changed)!; var expected = JsonNode.Parse(source)!;
                    if (command == "addRowAfter") {
                        var rows = TableRows(actual); Assert.Equal(sourceRows.Count + 1, rows.Count);
                        Assert.Equal("", NodeText(rows[index + 1])); rows.RemoveAt(index + 1);
                    } else if (command == "addColumnAfter") {
                        foreach (var row in TableRows(actual)) {
                            var cells = row!["content"]!.AsArray(); Assert.Equal(3, cells.Count);
                            Assert.Equal("", NodeText(cells[1])); cells.RemoveAt(1);
                        }
                    } else if (command == "deleteRow") TableRows(expected).RemoveAt(index);
                    else if (command == "deleteColumn") foreach (var row in TableRows(expected)) row!["content"]!.AsArray().RemoveAt(0);
                    else expected["content"]!.AsArray().RemoveAt(1);
                    Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
                    await page.Locator("[data-command=undo]").ClickAsync(); await Stable(page); Assert.Equal(canonical, JsonNode.Parse(await Json(page))!.ToJsonString());
                    await page.Locator("[data-command=redo]").ClickAsync(); await Stable(page); Assert.Equal(changed, await Json(page));
                    await page.Locator("[data-command=undo]").ClickAsync(); await Stable(page); Assert.Equal(source, await Json(page));
                }
            }
            await page.EvaluateAsync("() => window.nativeFlowLab.jump(1)");
            await page.Locator("#editor tr").First.Locator("p").First.ClickAsync();
            await page.Locator("[data-command=deleteRow]").ClickAsync(); await Stable(page);
            var removed = JsonNode.Parse(await Json(page))!; var withoutFirst = JsonNode.Parse(source)!; TableRows(withoutFirst).RemoveAt(0);
            Assert.Equal(withoutFirst.ToJsonString(), removed.ToJsonString());
            Assert.False((await Stable(page)).GetProperty("tables")[0].GetProperty("hasHeader").GetBoolean());
            await page.Locator("[data-command=undo]").ClickAsync(); AssertTableHeaders(await Stable(page)); Assert.Equal(source, await Json(page));
            await page.Locator("#readonly").ClickAsync(); await Stable(page);
            foreach (var command in new[] { "addRowAfter", "addColumnAfter", "deleteRow", "deleteColumn", "deleteTable" })
                await Assertions.Expect(page.Locator($"[data-command={command}]")).ToBeDisabledAsync();
            await page.Locator("#readonly").ClickAsync(); await Stable(page);
            results.Add(new { Sample = id, PageTwoMarker = marker, RepeatsPerCommand = 2, Commands = 5, ExactTargetAndUndoRedo = true, FirstRowDeleteUndo = true, ReadonlyDisabled = true });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "direct-table-toolbar.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return results;
    }
}
