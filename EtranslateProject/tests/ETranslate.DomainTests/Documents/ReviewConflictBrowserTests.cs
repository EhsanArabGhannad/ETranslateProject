using ETranslate.Contracts.Documents;
using Microsoft.Playwright;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    [LocalEditorAdapterFact]
    public async Task Confirmed_review_conflicts_lock_controls_but_transient_failure_preserves_retry_for_locked_and_editable_views()
    {
        foreach (var (status, editable, rejected) in new[] { (409, true, true), (402, true, true), (403, true, true), (503, true, false), (503, false, false) })
        {
            await using var fixture = await BrowserFixture.CreateAsync(Initial, editable);
            fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            await fixture.OpenAsync(); var page = fixture.Page;
            var original = await page.Locator("#document-json").InputValueAsync(); var response = fixture.Enqueue();
            await page.Locator(".review-form button").ClickAsync();
            await response.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(page.Locator(".review-form button")).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "false");
            response.Release.SetResult(status);
            await Assertions.Expect(page.Locator("#review-status")).ToContainTextAsync("Synthetic review failure");
            if (rejected) await Assertions.Expect(page.Locator(".review-form button")).ToBeDisabledAsync();
            else await Assertions.Expect(page.Locator(".review-form button")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", editable && !rejected ? "true" : "false");
            await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", editable && !rejected ? "true" : "false");
            Assert.Equal(original, await page.Locator("#document-json").InputValueAsync());
            if (!rejected)
            {
                var retry = fixture.Enqueue(); await page.Locator(".review-form button").ClickAsync();
                await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); retry.Release.SetResult(503);
                await Assertions.Expect(page.Locator(".review-form button")).ToBeEnabledAsync();
            }
            Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }
}
