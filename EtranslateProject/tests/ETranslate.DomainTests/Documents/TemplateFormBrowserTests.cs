using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalTemplateFormFactAttribute : FactAttribute
{
    public LocalTemplateFormFactAttribute()
    { if (Environment.GetEnvironmentVariable("ETRANSLATE_TEMPLATE_BROWSER_TESTS") != "1") Skip = "Run Verify-TemplateEditing.ps1 on local synthetic fixtures."; }
}
public sealed class TemplateFormBrowserTests
{
    [LocalTemplateFormFact] public async Task Select_create_with_preview_edit_and_reload_in_real_browser()
    {
        var origin = new Uri(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_WEB_URL")!);
        Assert.True(origin.IsLoopback && origin.Scheme == "http");
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_TENANT")!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
        var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(origin, "/Account/Login").ToString());
        await page.Locator("input[type=email]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_EMAIL")!);
        await page.Locator("input[type=password]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_PASSWORD")!);
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.GotoAsync(new Uri(origin, $"/Settings/Templates?tenantId={tenant}").ToString());
        var name = "Browser template " + Guid.NewGuid().ToString("N");
        await page.Locator("#template-name").FillAsync(name); await page.Locator("#header-text").FillAsync("Browser header");
        await page.Locator("#footer-text").FillAsync("Browser footer"); await page.Locator("#starter").FillAsync("Browser starter");
        await page.Locator("#template-logo").SetInputFilesAsync(new FilePayload { Name = "logo.png", MimeType = "image/png", Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII=") });
        Assert.True(await page.Locator("#template-logo-preview").IsVisibleAsync());
        Assert.StartsWith("blob:", await page.Locator("#template-logo-preview").GetAttributeAsync("src"));
        await page.WaitForFunctionAsync("document.getElementById('template-logo-preview').naturalWidth > 0");
        await page.Locator("#template-save").ClickAsync();
        try { await page.WaitForURLAsync(url => url.Contains("templateId="), new() { Timeout = 10000, WaitUntil = WaitUntilState.DOMContentLoaded }); }
        catch (TimeoutException) { Assert.Fail($"Save did not redirect. URL: {page.Url}; page: {await page.Locator("body").InnerTextAsync()}"); }
        var id = await page.Locator("input[name='Form.TemplateId']").InputValueAsync(); Assert.True(Guid.TryParse(id, out _));
        Assert.Equal("2", await page.Locator("input[name='Form.ExpectedRevision']").InputValueAsync());
        Assert.Equal(name, await page.Locator("#template-name").InputValueAsync());
        Assert.True(await page.Locator("#template-name").EvaluateAsync<bool>("element => element.readOnly"));
        Assert.Equal("Browser header", await page.Locator("#header-text").InputValueAsync());
        Assert.Single(await page.Locator(".template-images img").AllAsync());
        await page.Locator("#header-text").FillAsync("Unsaved text must survive cancelled selection");
        await page.Locator("#template-picker").SelectOptionAsync("");
        Assert.Equal(id, await page.Locator("#template-picker").InputValueAsync());
        Assert.Equal("Unsaved text must survive cancelled selection", await page.Locator("#header-text").InputValueAsync());
        await page.Locator("#header-text").FillAsync("Updated browser header"); await page.Locator("#template-save").ClickAsync();
        await page.WaitForFunctionAsync("document.querySelector('input[name=\"Form.ExpectedRevision\"]').value === '3'");
        await page.ReloadAsync(); Assert.Equal("Updated browser header", await page.Locator("#header-text").InputValueAsync());
        await page.Locator("#template-picker").SelectOptionAsync("");
        await page.WaitForFunctionAsync("document.querySelector('input[name=\"Form.TemplateId\"]').value === ''");
        await page.Locator("#template-picker").SelectOptionAsync(id);
        await page.WaitForFunctionAsync("document.querySelector('input[name=\"Form.TemplateId\"]').value !== ''");
        Assert.Equal("Browser footer", await page.Locator("#footer-text").InputValueAsync());
        Assert.Equal("Browser starter", await page.Locator("#starter").InputValueAsync());
        var output = Environment.GetEnvironmentVariable("ETRANSLATE_NAV_QA_PATH")!; Directory.CreateDirectory(output);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "template-edit-desktop.png"), FullPage = true });
        await page.SetViewportSizeAsync(390, 844);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "template-edit-mobile.png"), FullPage = true });
        var overflow = await page.EvaluateAsync<string>("JSON.stringify([...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().width > innerWidth).map(e => ({tag:e.tagName,cls:e.className,id:e.id,width:e.getBoundingClientRect().width})))");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"), overflow);
        Assert.Empty(errors);
    }
}
