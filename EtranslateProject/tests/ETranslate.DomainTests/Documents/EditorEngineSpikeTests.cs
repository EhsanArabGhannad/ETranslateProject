using System.Diagnostics;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Rendering;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class LocalEditorEngineSpikeFactAttribute : FactAttribute
{
    public LocalEditorEngineSpikeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_SPIKE_TESTS") != "1")
            Skip = "Run Verify-EditorEngineStep1.ps1; isolated synthetic browser/PDF research only.";
    }
}

public sealed class EditorEngineSpikeTests
{
    [LocalEditorEngineSpikeFact]
    public async Task Baseline_gaps_and_bounded_page_measurements_are_reported_without_production_writes()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_SPIKE_ROOT")!);
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_SPIKE_OUTPUT")!);
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_SPIKE_NODE")!)
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("serve.mjs"); start.ArgumentList.Add("--port"); start.ArgumentList.Add("0");
        using var server = Process.Start(start)!;
        try
        {
            var line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using var ready = JsonDocument.Parse(line!);
            var origin = new Uri(ready.RootElement.GetProperty("url").GetString()!);
            Assert.True(origin.IsLoopback && origin.Scheme == "http");
            using var client = new HttpClient();
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(new Uri(origin, "/samples.js"))).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(new Uri(origin, "/generated/fonts/../../../../appsettings.json"))).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await client.PostAsync(origin, new StringContent("not saved"))).StatusCode);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
            await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false, ServiceWorkers = ServiceWorkerPolicy.Block });
            var external = new List<string>();
            await context.RouteAsync("**/*", async route =>
            {
                if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
                { external.Add(route.Request.Url); await route.AbortAsync(); }
                else await route.ContinueAsync();
            });
            var page = await context.NewPageAsync(); var errors = new List<string>();
            page.PageError += (_, error) => errors.Add(error);
            await page.GotoAsync(origin.ToString());
            await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("JSON: korundu");
            var baseline = await page.EvaluateAsync<JsonElement>("() => window.editorStep1.report()");
            Assert.True(baseline.GetProperty("productionCompatible").GetBoolean());
            Assert.True(baseline.GetProperty("jsonUnchanged").GetBoolean());
            Assert.True(baseline.GetProperty("previewTextPreserved").GetBoolean());
            Assert.True(baseline.GetProperty("pages").GetInt32() > 1);
            var json = await page.EvaluateAsync<string>("() => JSON.stringify(window.editorStep1.json())");
            Assert.True(DocumentContentJson.TryGetPlainText(json, out var plain));
            Assert.Contains("بررسی", plain); Assert.Contains("İstanbul", plain);
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "baseline-desktop.png"), FullPage = true });
            await page.Locator("#zoom").SelectOptionAsync("0.75");
            Assert.Equal(baseline.GetProperty("pages").GetInt32(), (await page.EvaluateAsync<JsonElement>("() => window.editorStep1.reflow()")).GetProperty("pages").GetInt32());
            await page.Locator("#zoom").SelectOptionAsync("1");
            await page.SetViewportSizeAsync(390, 844);
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "baseline-mobile.png"), FullPage = true });
            await page.SetViewportSizeAsync(1440, 1000);
            var word = await page.EvaluateAsync<JsonElement>("() => window.editorStep1.loadSample('word')");
            Assert.True(word.GetProperty("productionCompatible").GetBoolean());
            Assert.Equal(6, word.GetProperty("styleGaps").GetArrayLength());
            var wordJson = await page.EvaluateAsync<string>("() => JSON.stringify(window.editorStep1.json())");
            Assert.True(DocumentContentJson.TryGetPlainText(wordJson, out var wordText));
            Assert.Contains("متن فارسی ساختگی", wordText); Assert.Contains("WORD_STYLE_SAMPLE", wordText);
            Assert.Contains("\"bold\"", wordJson); Assert.Contains("\"italic\"", wordJson);
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "word-style-gaps.png"), FullPage = true });
            var merged = await page.EvaluateAsync<JsonElement>("() => window.editorStep1.loadSample('merged')");
            Assert.False(merged.GetProperty("productionCompatible").GetBoolean());
            var mergedJson = await page.EvaluateAsync<string>("() => JSON.stringify(window.editorStep1.json())");
            Assert.Contains("\"colspan\":2", mergedJson);
            Assert.False(DocumentContentJson.TryGetPlainText(mergedJson, out _));
            var oversized = await page.EvaluateAsync<JsonElement>("() => window.editorStep1.loadSample('oversized')");
            Assert.Equal("oversized-block", oversized.GetProperty("issue").GetString());
            Assert.True(oversized.GetProperty("jsonUnchanged").GetBoolean());
            Assert.Equal(JsonValueKind.Null, oversized.GetProperty("pages").ValueKind);
            Assert.Contains("OVERSIZED_BLOCK", await page.Locator("#editor").InnerTextAsync());
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "bounded-overflow.png"), FullPage = true });
            Assert.Empty(errors); Assert.Empty(external);

            // Production renderer, synthetic document only. Never touches a tenant, database, archive or API.
            using var renderer = new ChromiumDraftPdfRenderer();
            var bytes = await renderer.RenderAsync(new(Guid.NewGuid(), 1, null, json, null, null, DocumentPageLayout.Default.ToJson(), null,
                new Dictionary<Guid, PdfImage>()), CancellationToken.None);
            int pdfPages;
            using (var stream = new MemoryStream(bytes)) using (var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import)) pdfPages = pdf.PageCount;
            Assert.True(pdfPages > 1);
            await Assert.ThrowsAsync<PdfRenderValidationException>(() => renderer.RenderAsync(new(Guid.NewGuid(), 1, null, mergedJson, null, null,
                DocumentPageLayout.Default.ToJson(), null, new Dictionary<Guid, PdfImage>()), CancellationToken.None));
            await File.WriteAllBytesAsync(Path.Combine(output, "baseline-reference.pdf"), bytes);
            var report = new { LocalOnly = true, PaidEnginesInstalled = false, RealWordClipboardTested = false, RealDocxTested = false,
                Baseline = baseline, WordLikeHtml = word, MergedTable = merged, OversizedBlock = oversized,
                PdfPages = pdfPages, WholeBlockPreviewPages = baseline.GetProperty("pages").GetInt32(),
                PageCountsMatchForThisFixture = pdfPages == baseline.GetProperty("pages").GetInt32(),
                ExactBoundaryParityProven = false, ProductionFilesChangedByLab = false };
            await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); } }
    }
}
