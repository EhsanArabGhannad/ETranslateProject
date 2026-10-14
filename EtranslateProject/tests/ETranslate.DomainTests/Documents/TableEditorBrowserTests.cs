using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

// Explicit opt-in: the local script supplies only its newly-created synthetic account/job.
public sealed class LocalTableBrowserFactAttribute : FactAttribute
{
    public LocalTableBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_TABLE_BROWSER_TESTS") != "1")
            Skip = "Run Verify-EditorTables.ps1 with local AppHost and the installed PDF browser.";
    }
}

public sealed class TableEditorBrowserTests
{
    [LocalTableBrowserFact]
    public async Task Table_toolbar_edits_save_reload_and_historical_readonly_work_in_browser()
    {
        var origin = new Uri(Environment.GetEnvironmentVariable("ETRANSLATE_TABLE_WEB_URL")!);
        Assert.True(origin.IsLoopback && origin.Scheme == "http");
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_TABLE_TENANT")!);
        var job = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_TABLE_JOB")!);
        var path = $"/Workspace/Editor?tenantId={tenant}&jobId={job}";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(origin, "/Account/Login").ToString());
        await page.Locator("input[type=email]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_TABLE_EMAIL")!);
        await page.Locator("input[type=password]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_TABLE_PASSWORD")!);
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !new Uri(url).AbsolutePath.Equals("/Account/Login", StringComparison.OrdinalIgnoreCase));
        await page.GotoAsync(new Uri(origin, path).ToString());
        await page.Locator("#rich-region").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal(1, await page.Locator("#rich-text table").CountAsync());
        // A paragraph after the existing table provides a top-level insertion point.
        await page.Locator("#rich-text .ProseMirror > p").Last.ClickAsync();
        await page.Locator("button[data-command=insertTable]").ClickAsync();
        Assert.Equal(2, await page.Locator("#rich-text table").CountAsync());
        var table = page.Locator("#rich-text table").Last;
        Assert.Equal(3, await table.Locator("tr").CountAsync());
        await page.Locator("button[data-command=addRowAfter]").ClickAsync();
        Assert.Equal(4, await table.Locator("tr").CountAsync());
        await page.Locator("button[data-command=addColumnAfter]").ClickAsync();
        Assert.Equal(4, await table.Locator("tr").First.Locator("th,td").CountAsync());
        await page.Locator("button[data-command=deleteColumn]").ClickAsync();
        await page.Locator("button[data-command=deleteRow]").ClickAsync();
        Assert.Equal(3, await table.Locator("tr").CountAsync());
        Assert.Equal(3, await table.Locator("tr").First.Locator("th,td").CountAsync());
        await page.Locator("button[data-command=undo]").ClickAsync();
        // Consecutive commands can share an undo group; exact grouping is not a product promise.
        await page.Locator("button[data-command=redo]").ClickAsync();
        Assert.Equal(2, await page.Locator("#rich-text table").CountAsync());
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("#rich-region").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal(2, await page.Locator("#rich-text table").CountAsync());
        await page.ReloadAsync();
        await page.Locator("#rich-region").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal(2, await page.Locator("#rich-text table").CountAsync());
        Assert.Equal(2, await page.Locator("#preview-body table").CountAsync());
        await page.Locator("#rich-text table").Last.Locator("p").First.ClickAsync();
        await page.Locator("button[data-command=deleteTable]").ClickAsync();
        Assert.Equal(1, await page.Locator("#rich-text table").CountAsync());
        await page.Locator("button[data-command=undo]").ClickAsync();
        Assert.Equal(2, await page.Locator("#rich-text table").CountAsync());
        await page.GotoAsync(new Uri(origin, path + "&revisionNumber=2").ToString());
        await page.Locator("#rich-region").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal(1, await page.Locator("#rich-text table").CountAsync());
        foreach (var command in new[] { "insertTable", "addRowAfter", "addColumnAfter", "deleteRow", "deleteColumn", "deleteTable" })
            Assert.True(await page.Locator($"button[data-command={command}]").IsDisabledAsync());
        Assert.Empty(errors);
    }
}
