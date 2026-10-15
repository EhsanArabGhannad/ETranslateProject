using Microsoft.Playwright;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Web.Models;
using PdfSharp.Pdf.IO;

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
        await page.WaitForFunctionAsync("document.querySelector('#template-preview-size').textContent.includes('mm')");
        const string initialHeader = "Örnek Translation Office\nنمونهٔ سربرگ فارسی";
        await page.Locator("#template-name").FillAsync(name); await page.Locator("#header-text").FillAsync(initialHeader);
        await page.Locator("#footer-text").FillAsync("Browser footer"); await page.Locator("#starter").FillAsync("Browser starter");
        var pngData = await page.EvaluateAsync<string>("""
            () => { const c = document.createElement('canvas'); c.width = 900; c.height = 100; const g=c.getContext('2d');
                g.fillStyle='#167064'; g.fillRect(0,0,900,100); g.fillStyle='#fff'; g.font='bold 36px sans-serif';
                g.fillText('ETranslate | TEST LETTERHEAD',25,64); return c.toDataURL('image/png').split(',')[1]; }
            """);
        var imageBytes = Convert.FromBase64String(pngData);
        await page.Locator("#template-logo").SetInputFilesAsync(new FilePayload { Name = "logo.png", MimeType = "image/png", Buffer = imageBytes });
        Assert.True(await page.Locator("#template-logo-preview").IsVisibleAsync());
        Assert.StartsWith("blob:", await page.Locator("#template-logo-preview").GetAttributeAsync("src"));
        await page.WaitForFunctionAsync("document.getElementById('template-logo-preview').naturalWidth > 0");
        await page.Locator("details").Filter(new() { Has = page.Locator("#header-line-controls") }).Locator("summary").ClickAsync();
        await page.Locator("#header-line-controls select").Nth(0).SelectOptionAsync("center");
        await page.Locator("#header-line-controls select").Nth(1).SelectOptionAsync("right");
        Assert.Equal("center", await page.Locator("#preview-header p").Nth(0).EvaluateAsync<string>("e => e.style.textAlign"));
        Assert.Equal("right", await page.Locator("#preview-header p").Nth(1).EvaluateAsync<string>("e => e.style.textAlign"));
        await page.Locator("#image-layout").SelectOptionAsync("logo");
        foreach (var alignment in new[] { "left", "center", "right" })
        {
            await page.Locator("#image-alignment").SelectOptionAsync(alignment);
            await page.WaitForFunctionAsync("""
                alignment => { const p=document.querySelector('#preview-header').getBoundingClientRect(), i=document.querySelector('#preview-header img').getBoundingClientRect();
                    return Math.abs(alignment==='left' ? i.left-p.left : alignment==='right' ? i.right-p.right : (i.left+i.right-p.left-p.right)/2)<2; }
                """, alignment);
        }
        await page.Locator("#image-layout").SelectOptionAsync("banner");
        await page.WaitForFunctionAsync("document.querySelector('#preview-header img').getBoundingClientRect().width > document.querySelector('#preview-header').getBoundingClientRect().width * .98");
        await page.Locator("#template-save").ClickAsync();
        try { await page.WaitForURLAsync(url => url.Contains("templateId="), new() { Timeout = 10000, WaitUntil = WaitUntilState.DOMContentLoaded }); }
        catch (TimeoutException) { Assert.Fail($"Save did not redirect. URL: {page.Url}; page: {await page.Locator("body").InnerTextAsync()}"); }
        var id = await page.Locator("input[name='Form.TemplateId']").InputValueAsync(); Assert.True(Guid.TryParse(id, out _));
        Assert.Equal("2", await page.Locator("input[name='Form.ExpectedRevision']").InputValueAsync());
        Assert.Equal(name, await page.Locator("#template-name").InputValueAsync());
        Assert.True(await page.Locator("#template-name").EvaluateAsync<bool>("element => element.readOnly"));
        Assert.Equal(initialHeader, await page.Locator("#header-text").InputValueAsync());
        Assert.Equal("[\"center\",\"right\"]", await page.Locator("#header-alignments").InputValueAsync());
        Assert.Single(await page.Locator(".template-images img").AllAsync());
        const string unsavedHeader = "Unsaved text must survive cancelled selection\nنمونهٔ سربرگ فارسی";
        await page.Locator("#header-text").FillAsync(unsavedHeader);
        await page.Locator("#template-picker").SelectOptionAsync("");
        Assert.Equal(id, await page.Locator("#template-picker").InputValueAsync());
        Assert.Equal(unsavedHeader, await page.Locator("#header-text").InputValueAsync());
        const string updatedHeader = "Updated browser header\nنمونهٔ سربرگ فارسی";
        await page.Locator("#header-text").FillAsync(updatedHeader); await page.Locator("#template-save").ClickAsync();
        await page.WaitForFunctionAsync("document.querySelector('input[name=\"Form.ExpectedRevision\"]').value === '3'");
        await page.ReloadAsync(); Assert.Equal(updatedHeader, await page.Locator("#header-text").InputValueAsync());
        await page.Locator("#template-picker").SelectOptionAsync("");
        await page.WaitForFunctionAsync("document.querySelector('input[name=\"Form.TemplateId\"]').value === ''");
        await page.Locator("#template-picker").SelectOptionAsync(id);
        await page.WaitForFunctionAsync("document.querySelector('input[name=\"Form.TemplateId\"]').value !== ''");
        Assert.Equal("Browser footer", await page.Locator("#footer-text").InputValueAsync());
        Assert.Equal("Browser starter", await page.Locator("#starter").InputValueAsync());
        var output = Environment.GetEnvironmentVariable("ETRANSLATE_NAV_QA_PATH")!; Directory.CreateDirectory(output);
        await page.WaitForFunctionAsync("document.querySelector('#preview-header img')?.complete && document.querySelector('#preview-header img').naturalWidth > 0");
        var headerJson = await page.Locator("#template-header").InputValueAsync();
        var footerJson = await page.Locator("#template-footer").InputValueAsync();
        var assetId = Guid.Parse(System.Text.Json.JsonDocument.Parse(headerJson).RootElement.GetProperty("content").EnumerateArray().Single(n => n.GetProperty("type").GetString() == "image").GetProperty("attrs").GetProperty("assetId").GetString()!);
        using var renderer = new ChromiumDraftPdfRenderer();
        var body = DocumentText.FromPlainText(string.Join('\n', Enumerable.Range(1, 50).Select(i => $"Paragraph {i}: Türkçe çeviri örneği. This unsigned sample checks a repeating banner, independent line alignment and page margins.")));
        var pdf = await renderer.RenderAsync(new(Guid.NewGuid(), 1, Guid.NewGuid(), body, headerJson, footerJson, DocumentPageLayout.Default.ToJson(), null,
            new Dictionary<Guid, PdfImage> { [assetId] = new("image/png", imageBytes) }), CancellationToken.None);
        using (var stream = new MemoryStream(pdf)) using (var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import)) Assert.True(doc.PageCount >= 2);
        await File.WriteAllBytesAsync(Path.Combine(output, "template-presentation-qa.pdf"), pdf);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "template-edit-desktop.png"), FullPage = true });
        await page.SetViewportSizeAsync(390, 844);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "template-edit-mobile.png"), FullPage = true });
        var overflow = await page.EvaluateAsync<string>("JSON.stringify([...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().width > innerWidth).map(e => ({tag:e.tagName,cls:e.className,id:e.id,width:e.getBoundingClientRect().width})))");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"), overflow);
        Assert.Empty(errors);
    }
}
