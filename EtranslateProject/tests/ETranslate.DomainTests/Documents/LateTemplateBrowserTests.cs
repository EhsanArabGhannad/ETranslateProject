using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalLateTemplateFactAttribute : FactAttribute
{
    public LocalLateTemplateFactAttribute() { if (Environment.GetEnvironmentVariable("ETRANSLATE_LATE_BROWSER_TESTS") != "1") Skip = "Local synthetic late-template browser fixtures only."; }
}
public sealed class LateTemplateBrowserTests
{
    [LocalLateTemplateFact] public async Task Separate_tab_preserves_unsaved_text_and_history_uses_its_own_template()
    {
        var origin = new Uri(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_WEB_URL")!); Assert.True(origin.IsLoopback && origin.Scheme == "http");
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_TENANT")!);
        var job = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_LATE_JOB")!);
        var target = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_LATE_TARGET")!);
        var oldTemplate = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_LATE_OLD_TEMPLATE")!);
        using var pw = await Playwright.CreateAsync(); await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
        var errors = new List<string>(); context.Page += (_, p) => p.PageError += (_, e) => errors.Add(e);
        await page.GotoAsync(new Uri(origin, "/Account/Login").ToString());
        await page.Locator("input[type=email]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_EMAIL")!);
        await page.Locator("input[type=password]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_PASSWORD")!);
        await page.Locator("form button[type=submit]").ClickAsync();
        var editorUrl = new Uri(origin, $"/Workspace/Editor?tenantId={tenant}&jobId={job}").ToString();
        await page.GotoAsync(editorUrl);
        await page.Locator("#rich-text .ProseMirror").FillAsync("Unsaved work must survive a template change in another tab");
        var popupTask = context.WaitForPageAsync(); await page.Locator("#change-template-link").ClickAsync(); var popup = await popupTask;
        await popup.Locator("#template-select").SelectOptionAsync(target.ToString());
        await popup.Locator("#document-template-change-form button").ClickAsync();
        await Assertions.Expect(popup.Locator("input[name=expectedRevision]")).ToHaveValueAsync("6");
        Assert.Contains("Unsaved work", await page.Locator("#rich-text .ProseMirror").InnerTextAsync());
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false");
        Assert.Contains("Unsaved work", await page.Locator("#rich-text .ProseMirror").InnerTextAsync());
        var history = await context.NewPageAsync();
        await history.GotoAsync(editorUrl + "&revisionNumber=3");
        Assert.Equal(oldTemplate.ToString(), await history.Locator("#editor-workspace").GetAttributeAsync("data-template-id"));
        Assert.Equal(0, await history.Locator("#change-template-link").CountAsync());
        await history.GotoAsync(editorUrl + "&revisionNumber=4");
        Assert.Equal(target.ToString(), await history.Locator("#editor-workspace").GetAttributeAsync("data-template-id"));
        await history.GotoAsync(editorUrl + "&revisionNumber=1");
        Assert.Equal("", await history.Locator("#editor-workspace").GetAttributeAsync("data-template-id"));
        var output = Environment.GetEnvironmentVariable("ETRANSLATE_NAV_QA_PATH")!; Directory.CreateDirectory(output);
        await popup.ScreenshotAsync(new() { Path = Path.Combine(output, "late-template-desktop.png"), FullPage = true });
        await popup.SetViewportSizeAsync(390,844);
        Assert.True(await popup.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        await popup.ScreenshotAsync(new() { Path = Path.Combine(output, "late-template-mobile.png"), FullPage = true });
        Assert.Empty(errors);
    }
}
