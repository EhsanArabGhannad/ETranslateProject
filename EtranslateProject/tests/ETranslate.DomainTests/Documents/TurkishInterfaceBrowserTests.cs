using System.Text.Json;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalTurkishInterfaceFactAttribute : FactAttribute
{
    public LocalTurkishInterfaceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_TR_BROWSER_TESTS") != "1") Skip = "Run Verify-TurkishInterface.ps1 with local synthetic fixtures.";
    }
}

public sealed class TurkishInterfaceBrowserTests
{
    [LocalTurkishInterfaceFact]
    public async Task Turkish_ltr_ui_preserves_persian_documents_and_fractional_margins()
    {
        var origin = new Uri(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_WEB_URL")!);
        Assert.True(origin.IsLoopback && origin.Scheme == "http");
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_TENANT")!);
        var job = Guid.Parse(Environment.GetEnvironmentVariable("ETRANSLATE_TR_JOB")!);
        var output = Environment.GetEnvironmentVariable("ETRANSLATE_NAV_QA_PATH")!; Directory.CreateDirectory(output);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        // A Persian browser must not switch the single-language product UI or number contracts.
        await using var context = await browser.NewContextAsync(new() { Locale = "fa-IR", ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync();
        var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(origin, "/Account/Login").ToString());
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "tr");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "ltr");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Çalışma alanına giriş yap", Exact = true })).ToBeVisibleAsync();
        await page.Locator("input[type=email]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_EMAIL")!);
        await page.Locator("input[type=password]").FillAsync(Environment.GetEnvironmentVariable("ETRANSLATE_NAV_PASSWORD")!);
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.GotoAsync(new Uri(origin, $"/Settings/Templates?tenantId={tenant}").ToString());
        var menu = page.Locator("nav.app-navigation");
        Assert.Equal(new[] { "Dosyalar", "İşlerim", "Ekip üyeleri", "Abonelik", "Ayarlar" }, await menu.Locator("a").AllTextContentsAsync());
        await Assertions.Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Ayarlar", Exact = true })).ToHaveAttributeAsync("aria-current", "page");
        const string header = "سربرگ فارسی\nTürkçe üstbilgi";
        const string starter = "ترجمهٔ فارسی حفظ می‌شود\nTürkçe metin korunur";
        await page.Locator("#template-name").FillAsync("Türkçe / قالب آزمایشی");
        await page.Locator("#header-text").FillAsync(header);
        await page.Locator("#footer-text").FillAsync("Türkçe altbilgi");
        await page.Locator("#starter").FillAsync(starter);
        await page.Locator("#footer-placement").SelectOptionAsync("LastPage");
        await page.Locator(".page-layout-fields summary").ClickAsync();
        await page.Locator("#Form_MarginTop").FillAsync("25.5");
        await page.Locator("#Form_MarginRight").FillAsync("15.5");
        await Assertions.Expect(page.Locator("#header-text")).ToHaveAttributeAsync("dir", "auto");
        await Assertions.Expect(page.Locator("#preview-header")).ToHaveAttributeAsync("dir", "auto");
        await Assertions.Expect(page.Locator("#preview-header")).ToContainTextAsync("سربرگ فارسی");
        await page.Locator("#template-save").ClickAsync();
        await Assertions.Expect(page.Locator("#Form_ExpectedRevision")).ToHaveValueAsync("1");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#header-text")).ToHaveValueAsync(header);
        await Assertions.Expect(page.Locator("#starter")).ToHaveValueAsync(starter);
        await Assertions.Expect(page.Locator("#Form_MarginTop")).ToHaveValueAsync("25.5");
        await Assertions.Expect(page.Locator("#Form_MarginRight")).ToHaveValueAsync("15.5");
        using (var layout = JsonDocument.Parse(await page.Locator("#template-layout").InputValueAsync()))
        {
            // The preview narrows this hidden JSON to its own footer policy; margin persistence is checked above and via API by the caller.
            Assert.Equal("LastPage", layout.RootElement.GetProperty("footerPlacement").GetString());
        }
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "turkish-templates-desktop.png"), FullPage = true });
        await CheckMobile(page, output, "templates");
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync(new Uri(origin, $"/Workspace/Editor?tenantId={tenant}&jobId={job}").ToString());
        await Assertions.Expect(page.Locator("#rich-text .ProseMirror")).ToContainTextAsync("ترجمه‌ی آزمایشی");
        await Assertions.Expect(page.Locator("#rich-text .ProseMirror")).ToContainTextAsync("Çeviri metni");
        await Assertions.Expect(page.Locator("button[data-command=rtl]")).ToHaveTextAsync("Sağdan sola");
        var before = await page.Locator("#document-json").InputValueAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "turkish-editor-desktop.png"), FullPage = true });
        await CheckMobile(page, output, "editor");
        Assert.Equal(before, await page.Locator("#document-json").InputValueAsync());
        await page.SetViewportSizeAsync(1440, 1000);
        // Locale/label changes do not alter the saved JSON when resaving a multilingual revision.
        await page.Locator("#draft-form button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#draft-form input[name=expectedRevision]")).ToHaveValueAsync("3");
        Assert.Equal(before, await page.Locator("#document-json").InputValueAsync());
        foreach (var route in new[] { "Subscription", "PurchaseOrders", "Team", "Settings", "Workspace/Assignment", "Workspace/SigningPreparation", "Workspace/DocumentTemplate" })
        {
            var response = await page.GotoAsync(new Uri(origin, $"/{route}?tenantId={tenant}&jobId={job}").ToString());
            Assert.Equal(200, response!.Status);
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "tr");
            Assert.DoesNotMatch("[\\u0600-\\u06ff]", await page.Locator("nav.app-navigation").InnerTextAsync());
        }
        Assert.Empty(errors);
    }

    private static async Task CheckMobile(IPage page, string output, string name)
    {
        foreach (var width in new[] { 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"), $"{name}: horizontal overflow at {width}px");
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"turkish-{name}-{width}.png"), FullPage = true });
        }
    }
}
