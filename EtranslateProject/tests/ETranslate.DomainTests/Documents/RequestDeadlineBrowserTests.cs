using ETranslate.Contracts.Documents;
using Microsoft.Playwright;
using Microsoft.AspNetCore.Http;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    [LocalEditorAdapterFact]
    public async Task Malformed_review_response_is_unconfirmed_without_exposing_parser_details_or_losing_content()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial);
        fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
        fixture.ExtraHandler = async context => {
            if (context.Request.Method != "POST" || context.Request.Path != "/Workspace/Review") return false;
            context.Response.ContentType = "application/json"; await context.Response.WriteAsync("{"); return true;
        };
        await fixture.OpenAsync(); var page = fixture.Page; var source = await page.Locator("#document-json").InputValueAsync();
        await page.Locator(".review-form button").ClickAsync();
        await Assertions.Expect(page.Locator("#review-status")).ToContainTextAsync("sonucu doğrulanamadı");
        await Assertions.Expect(page.Locator(".review-form button")).ToBeEnabledAsync();
        Assert.Equal(source, await page.Locator("#document-json").InputValueAsync()); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Pending_save_and_review_requests_have_a_deadline_and_retain_content_for_manual_retry()
    {
        foreach (var review in new[] { false, true })
        {
            await using var fixture = await BrowserFixture.CreateAsync(Initial);
            fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            await fixture.OpenAsync(); var page = fixture.Page;
            if (!review) { await page.Locator("#rich-text [role=textbox]").PressAsync("Control+End"); await page.Keyboard.InsertTextAsync(" DEADLINE_UNSAVED"); }
            var source = await page.Locator("#document-json").InputValueAsync();
            await page.Clock.InstallAsync(); var gate = fixture.Enqueue();
            var button = page.Locator(review ? ".review-form button" : "#draft-form button[type=submit]");
            await button.ClickAsync(); await gate.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(button).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false");
            await page.Clock.FastForwardAsync(45_001);
            await Assertions.Expect(page.Locator(review ? "#review-status" : "#save-state")).ToContainTextAsync("sonucu doğrulanamadı");
            await Assertions.Expect(button).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
            Assert.Equal(source, await page.Locator("#document-json").InputValueAsync());
            if (!review) { await page.Locator("#save-state").ScrollIntoViewIfNeededAsync(); await fixture.ScreenshotAsync("request-deadline.png"); }
            gate.Release.TrySetResult(503);
            var retry = fixture.Enqueue(); await button.ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); retry.Release.SetResult(503);
            if (review) await Assertions.Expect(page.Locator("#review-status")).ToContainTextAsync("Synthetic review failure");
            else await Assertions.Expect(page.Locator("#save-state")).ToContainTextAsync("Kayıt doğrulanamadı");
            Assert.Equal(source, await page.Locator("#document-json").InputValueAsync()); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }
}
