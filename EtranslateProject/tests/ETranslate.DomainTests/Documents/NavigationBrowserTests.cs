using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalNavigationFactAttribute : FactAttribute
{
    public LocalNavigationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_NAV_BROWSER_TESTS") != "1")
            Skip = "Run Verify-Navigation.ps1 with local synthetic fixtures.";
    }
}

public sealed class NavigationBrowserTests
{
    [LocalNavigationFact]
    public async Task Menu_keeps_tenant_scope_and_template_forms_are_on_settings_only()
    {
        var origin = new Uri(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_WEB_URL")!);
        Assert.True(origin.IsLoopback && origin.Scheme == "http");
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_TENANT")!);
        var other = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_OTHER_TENANT")!);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(origin, "/Account/Login").ToString());
        await page.Locator("input[type=email]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_EMAIL")!);
        await page.Locator("input[type=password]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_PASSWORD")!);
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.GotoAsync(new Uri(origin, $"/Workspace?tenantId={tenant}").ToString());
        var menu = page.Locator("nav.app-navigation");
        Assert.Equal(5, await menu.Locator("a").CountAsync());
        foreach (var anchor in await menu.Locator("a").AllAsync()) Assert.Contains(tenant.ToString(), await anchor.GetAttributeAsync("href"));
        Assert.Equal(0, await page.Locator("#template-name, form.logo-form").CountAsync());
        await menu.GetByRole(AriaRole.Link, new() { Name = "Ayarlar", Exact = true }).ClickAsync();
        Assert.Contains($"tenantId={tenant}", page.Url);
        await page.GetByRole(AriaRole.Link, new() { Name = "Şablon yönetimini aç →", Exact = true }).ClickAsync();
        Assert.True(await page.Locator("#template-name").IsVisibleAsync());
        Assert.Contains($"tenantId={tenant}", page.Url);
        Assert.Equal("page", await menu.GetByRole(AriaRole.Link, new() { Name = "Ayarlar", Exact = true }).GetAttributeAsync("aria-current"));
        // Visual/overflow QA uses this disposable local browser, never the user's browser/profile.
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", "tmp", "navigation-qa"));
        // Caller supplies a workspace path for both normal and isolated build layouts.
        output = Environment.GetEnvironmentVariable("ETRANSLATE_NAV_QA_PATH") ?? output;
        Directory.CreateDirectory(output);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "templates-desktop.png"), FullPage = true });
        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "templates-mobile.png"), FullPage = true });
        await menu.GetByRole(AriaRole.Link, new() { Name = "İşlerim", Exact = true }).ClickAsync();
        Assert.Contains("assignedToMe=true", page.Url); Assert.Contains(tenant.ToString(), page.Url);
        await page.GotoAsync(new Uri(origin, $"/Settings/Templates?tenantId={other}").ToString());
        foreach (var anchor in await menu.Locator("a").AllAsync()) Assert.Contains(other.ToString(), await anchor.GetAttributeAsync("href"));
        Assert.Equal(0, await page.Locator("form.logo-form").CountAsync());
    }
}
