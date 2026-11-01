using System.Text.RegularExpressions;
using ETranslate.Contracts.Documents;
using Microsoft.Playwright;
using Microsoft.AspNetCore.Http;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private static string UploadField(string body) => Regex.Match(body, "name=\"uploadId\"\\r\\n\\r\\n([^\\r\\n]+)").Groups[1].Value;

    [LocalEditorAdapterFact]
    public async Task Acknowledged_source_upload_releases_write_controls_even_while_its_pdf_preview_is_pending()
    {
        await using var fixture = await BrowserFixture.CreateAsync(Initial); fixture.SourceUploadsEnabled = true;
        fixture.ExtraHandler = async request => {
            if (request.Request.Path != "/Workspace/UploadSource") return false;
            request.Response.ContentType = "application/json";
            await request.Response.WriteAsync("{\"sourceFile\":{\"id\":\"" + BrowserFixture.ImageId + "\",\"originalFileName\":\"synthetic.pdf\",\"contentType\":\"application/pdf\"}}"); return true;
        };
        await fixture.OpenAsync(); var page = fixture.Page;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/Workspace/Source?**", async route => { pending.TrySetResult(); await release.Task; try { await route.AbortAsync("failed"); } catch (PlaywrightException) { } });
        try {
            await page.Locator("#source-file").SetInputFilesAsync(new FilePayload { Name = "synthetic.pdf", MimeType = "application/pdf", Buffer = "%PDF-1.7 synthetic only"u8.ToArray() });
            await page.Locator("#source-upload-form button").ClickAsync(); await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#source-upload-form button")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#rich-text [role=textbox]")).ToHaveAttributeAsync("contenteditable", "true");
            await Assertions.Expect(page.Locator("#source-file")).ToHaveValueAsync(""); Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync());
        } finally { release.TrySetResult(); }
        Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
    }

    [LocalEditorAdapterFact]
    public async Task Upload_deadlines_keep_the_selected_file_identity_and_local_body_for_manual_retry()
    {
        foreach (var image in new[] { false, true }) {
            await using var fixture = await BrowserFixture.CreateAsync(Initial); fixture.SourceUploadsEnabled = true;
            fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            await fixture.OpenAsync(); var page = fixture.Page;
            if (image) { await page.Locator("#editor-images > summary").ClickAsync(); await SelectImageCaret(page, 0, 1); }
            var file = page.Locator(image ? "#editor-image-file" : "#source-file");
            var button = page.Locator(image ? "#editor-image-upload" : "#source-upload-form button");
            var status = page.Locator(image ? "#editor-image-status" : "#source-status");
            await file.SetInputFilesAsync(new FilePayload { Name = "synthetic.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
            var source = await page.Locator("#document-json").InputValueAsync();
            await page.Clock.InstallAsync(); var gate = fixture.Enqueue(); await button.ClickAsync();
            await gate.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); var id = UploadField(gate.Body); Assert.True(Guid.TryParse(id, out _));
            await page.Clock.FastForwardAsync(45001);
            await Assertions.Expect(status).ToContainTextAsync("sonucu doğrulanamadı");
            await Assertions.Expect(button).ToBeEnabledAsync(); Assert.Equal(source, await page.Locator("#document-json").InputValueAsync());
            gate.Release.SetResult(503);
            var retry = fixture.Enqueue(); await button.ClickAsync(); await retry.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(id, UploadField(retry.Body)); retry.Release.SetResult(200);
            if (image) await Assertions.Expect(page.Locator("#rich-text img")).ToHaveCountAsync(1);
            else { await Assertions.Expect(page.Locator("#source-select option")).ToHaveCountAsync(2); await Assertions.Expect(button).ToBeEnabledAsync(); }
            Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }

    [LocalEditorAdapterFact]
    public async Task Confirmed_upload_conflicts_and_access_denials_keep_all_editing_locked_without_losing_source()
    {
        foreach (var image in new[] { false, true }) foreach (var status in new[] { 402, 403, 409 }) {
            await using var fixture = await BrowserFixture.CreateAsync(Initial); fixture.SourceUploadsEnabled = true;
            fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10";
            await fixture.OpenAsync(); var page = fixture.Page;
            if (image) { await page.Locator("#editor-images > summary").ClickAsync(); await SelectImageCaret(page, 0, 1); }
            await page.Locator(image ? "#editor-image-file" : "#source-file").SetInputFilesAsync(new FilePayload { Name = "synthetic.png", MimeType = "image/png", Buffer = BodyImageFixture.Png });
            var gate = fixture.Enqueue(); await page.Locator(image ? "#editor-image-upload" : "#source-upload-form button").ClickAsync();
            await gate.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)); gate.Release.SetResult(status);
            await Assertions.Expect(page.Locator("#editor-workspace")).ToHaveAttributeAsync("data-editable", "false"); await AssertFrozenAsync(page);
            await Assertions.Expect(page.Locator("#draft-form button[type=submit]")).ToBeDisabledAsync();
            Assert.Equal(Initial, await page.Locator("#document-json").InputValueAsync()); Assert.Empty(fixture.Errors); Assert.Empty(fixture.Unexpected);
        }
    }
}
