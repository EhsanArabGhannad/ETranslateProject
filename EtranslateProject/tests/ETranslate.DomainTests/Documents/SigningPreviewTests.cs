using System.Net;
using System.Net.Http.Json;
using ETranslate.Contracts.Trust;
using ETranslate.Documents.Api.Authorization;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Rendering;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ETranslate.DomainTests.Documents;

public sealed class SigningPreviewTests
{
    private static SigningPreviewSnapshotV1 Plan() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Prepared", 0, "TranslatorOnly", Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 1, null, new string('a', 64), 100,
        "DraftUnsigned", Guid.NewGuid(), DateTimeOffset.UtcNow, [new(1, "Translator", Guid.NewGuid(), "  متن پیشنهادی\n<script>not executed</script>  ", "EImza")]);
    private static bool Valid(SigningPreviewSnapshotV1 plan) => SigningPreviewPolicy.ValidSnapshot(plan, plan.TenantId, plan.TranslationJobId, plan.DocumentId, plan.Id);
    [Fact] public void SnapshotRequiresExactScope()
    {
        var p = Plan(); Assert.True(Valid(p));
        Assert.False(SigningPreviewPolicy.ValidSnapshot(p, Guid.NewGuid(), p.TranslationJobId, p.DocumentId, p.Id));
        Assert.False(SigningPreviewPolicy.ValidSnapshot(p, p.TenantId, Guid.NewGuid(), p.DocumentId, p.Id));
        Assert.False(SigningPreviewPolicy.ValidSnapshot(p, p.TenantId, p.TranslationJobId, Guid.NewGuid(), p.Id));
        Assert.False(SigningPreviewPolicy.ValidSnapshot(p, p.TenantId, p.TranslationJobId, p.DocumentId, Guid.NewGuid()));
        Assert.False(Valid(p with { ReviewId = Guid.Empty })); Assert.False(Valid(p with { CreatedByUserId = Guid.Empty }));
    }
    [Theory] [InlineData("Cancelled", 1, false)] [InlineData("Signed", 0, false)] [InlineData("Prepared", 1, false)] [InlineData("Prepared", 0, true)]
    public void OnlyPreparedSnapshotCanGenerate(string status, long version, bool expected)
        => Assert.Equal(expected, SigningPreviewPolicy.CanGenerate(Plan() with { Status = status, Version = version }));
    [Fact] public void PoliciesRequireTheirOrderedStages()
    {
        var p = Plan(); Assert.False(Valid(p with { SignaturePolicy = "TranslatorAndTranslationOffice" }));
        var office = p with { SignaturePolicy = "TranslatorAndTranslationOffice", Stages = [p.Stages[0], new(2, "TranslationOffice", Guid.NewGuid(), "Office proposal", "MobilImza")] };
        Assert.True(Valid(office)); Assert.False(Valid(office with { Stages = office.Stages.Reverse().ToArray() }));
        Assert.False(Valid(p with { Stages = office.Stages })); Assert.False(Valid(p with { Stages = null! }));
    }
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("bad")]
    public void UnknownMethodIsRejected(string method)
    { var p = Plan(); Assert.False(Valid(p with { Stages = [p.Stages[0] with { RequestedMethod = method }] })); }
    [Fact] public void RawStatementLimitsPreserveWhitespace()
    {
        var p = Plan(); Assert.True(Valid(p with { Stages = [p.Stages[0] with { ProposedStatement = new string('x', 4000) }] }));
        Assert.False(Valid(p with { Stages = [p.Stages[0] with { ProposedStatement = "x" + new string(' ', 4000) }] }));
        Assert.False(Valid(p with { Stages = [p.Stages[0] with { ProposedStatement = " " }] }));
    }
    [Theory] [InlineData(0)] [InlineData(26214401)]
    public void SourceSizeIsBounded(long bytes) => Assert.False(Valid(Plan() with { PdfSizeBytes = bytes }));
    [Fact] public void HashAndArtifactKindAreAuthoritative()
    {
        Assert.False(Valid(Plan() with { PdfSha256 = "spoofed" }));
        Assert.False(Valid(Plan() with { PdfSha256 = new string('z', 64) }));
        Assert.False(Valid(Plan() with { ArtifactKind = "Signed" }));
    }
    [Fact] public void HtmlIsEncodedAndExplicitlyNotConsent()
    {
        var p = Plan(); var html = SigningPreviewRenderer.AppendixHtml(p, Guid.NewGuid());
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains(WebUtility.HtmlEncode(p.Stages[0].ProposedStatement), html);
        Assert.Contains("NOT ACCEPTED", html); Assert.Contains("NOT a final signable PDF", html);
        Assert.Contains("account ID", html); Assert.Contains("not this preview's hash", html);
        Assert.Contains("Later cancellation", html); Assert.Contains("numbered separately", html);
    }
    [Fact] public void CancelledPlanCannotRenderAppendix() => Assert.Throws<PdfRenderValidationException>(() =>
        SigningPreviewRenderer.AppendixHtml(Plan() with { Status = "Cancelled", Version = 1 }, Guid.NewGuid()));
    private static byte[] Pdf(int count, double width = 400)
    {
        using var doc = new PdfDocument(); for (var n = 0; n < count; n++) { var page = doc.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(width); }
        using var output = new MemoryStream(); doc.Save(output, false); return output.ToArray();
    }
    [Fact] public void ImportPreservesOriginalPagesAndBytes()
    {
        var original = Pdf(2, 420); var before = original.ToArray(); var combined = SigningPreviewRenderer.Combine(original, Pdf(3, 500));
        Assert.Equal(before, original);
        using var stream = new MemoryStream(combined); using var result = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(5, result.PageCount); Assert.Equal(420, result.Pages[0].Width.Point); Assert.Equal(420, result.Pages[1].Width.Point);
        Assert.Equal(500, result.Pages[2].Width.Point); Assert.Contains("UNSIGNED", result.Info.Title);
    }
    [Fact] public void CombinedLimitIncludesAllPages() => Assert.Throws<PdfRenderValidationException>(() => SigningPreviewRenderer.Combine(Pdf(49), Pdf(2)));
    [Fact] public void CorruptPdfIsRejected() => Assert.Throws<PdfRenderValidationException>(() => SigningPreviewRenderer.Combine([1, 2, 3], Pdf(1)));
    [Fact] public void PreviewMetadataIsImmutableAndSeparateFromSourcePdf()
    {
        var p = Plan(); var item = new DocumentSigningPreview(Guid.NewGuid(), p.DocumentId, p.Id, p.ReviewId, p.PdfVersionId,
            p.PdfSha256, SigningPreviewPolicy.RendererVersion, "{}", "test.pdf", 200, new string('B', 64), p.CreatedByUserId, p.CreatedAtUtc);
        Assert.Equal(new string('b', 64), item.Sha256); Assert.NotEqual(item.Sha256, item.SourcePdfSha256);
        Assert.All(typeof(DocumentSigningPreview).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
        using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer("Server=localhost;Database=test;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity = db.Model.FindEntityType(typeof(DocumentSigningPreview))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(prop => prop.Name).SequenceEqual(["PreparationId", "RendererVersion"]));
        Assert.Equal(3, entity.GetForeignKeys().Count()); Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }
    [Theory] [InlineData(401, 401)] [InlineData(403, 403)] [InlineData(404, 404)] [InlineData(500, 503)] [InlineData(429, 503)]
    public async Task TrustFailuresFailClosed(int backend, int expected)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)backend))) { BaseAddress = new Uri("http://trust") };
        var p = Plan(); var result = await new SigningPlanClient(http).ReadAsync(p.TenantId, p.TranslationJobId, p.DocumentId, p.Id, "Bearer test-token", default);
        Assert.Equal(expected, result.Status); Assert.Null(result.Value);
    }
    [Fact] public async Task SnapshotLookupForwardsOnlyServerAuthorization()
    {
        var p = Plan();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Assert.EndsWith($"/{p.Id}/preview-snapshot", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(p) };
        })) { BaseAddress = new Uri("http://trust") };
        var client = new SigningPlanClient(http);
        Assert.Equal(401, (await client.ReadAsync(p.TenantId, p.TranslationJobId, p.DocumentId, p.Id, null, default)).Status);
        Assert.Equal(p.Id, (await client.ReadAsync(p.TenantId, p.TranslationJobId, p.DocumentId, p.Id, "Bearer test-token", default)).Value!.Id);
    }
    [Fact] public async Task MalformedSnapshotDoesNotBecomeSuccess()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("not-json") })) { BaseAddress = new Uri("http://trust") };
        Assert.Equal(503, (await new SigningPlanClient(http).ReadAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Bearer test", default)).Status);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
}
