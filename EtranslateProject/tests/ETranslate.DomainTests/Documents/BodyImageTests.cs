using System.Buffers.Binary;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class BodyImageTests
{
    private static readonly Guid ImageId = Guid.NewGuid();
    public static IEnumerable<object[]> Cases() { using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "body-image-cases.json"))); foreach (var item in cases.RootElement.EnumerateArray()) yield return [item.GetRawText()]; }
    [Theory, MemberData(nameof(Cases))]
    public void V9_image_contract_is_bounded_and_preserves_older_vocabulary(string json)
    {
        using var item = JsonDocument.Parse(json);
        var attrs = item.RootElement.GetProperty("attrs").Deserialize<Dictionary<string, JsonElement>>()!; attrs["assetId"] = JsonSerializer.SerializeToElement(ImageId);
        var body = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "image", attrs } } });
        Assert.Equal(item.RootElement.GetProperty("valid").GetBoolean(), DocumentContentDescriptor.RichV9.TryRead(body, out _));
        for (var version = 1; version <= 8; version++) Assert.Equal(item.RootElement.GetProperty("legacyValid").GetBoolean(), new DocumentContentDescriptor(DocumentContentDescriptor.RichFormat, version).TryRead(body, out _));
    }
    [Fact]
    public void Image_scope_and_size_render_without_arbitrary_urls_or_relabelling_old_images()
    {
        var body = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "image", attrs = new { assetId = ImageId, assetScope = "document", widthPercent = 75, align = "right" } } } });
        Assert.Equal(new[] { ImageId }, DocumentImageReferences.Parse(body));
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, body, null, null, null, null, new Dictionary<Guid, PdfImage> { [ImageId] = new("image/png", BodyImageFixture.Png) }) { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 9 };
        var html = DraftPdfHtml.Prepare(input, "").Html; Assert.Contains("width:75%", html); Assert.Contains("margin-left:auto;margin-right:0", html); Assert.Contains("data:image/png;base64,", html);
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(input with { ContentSchemaVersion = 8 }, ""));
        var old = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "image", attrs = new { assetId = ImageId } } } });
        Assert.Empty(DocumentImageReferences.Parse(old));
        Assert.Equal(DraftPdfHtml.Prepare(input with { BodyJson = old, ContentSchemaVersion = 8 }, "").Html, DraftPdfHtml.Prepare(input with { BodyJson = old }, "").Html);
    }
    [Fact]
    public void Upload_metadata_has_immutable_ownership_and_dimensions_are_checked_before_decoding()
    {
        Assert.True(DocumentImagePolicy.HasSafeDimensions(BodyImageFixture.Png, "image/png"));
        Assert.True(DocumentImagePolicy.HasSafeDimensions(BodyImageFixture.Jpeg, "image/jpeg"));
        Assert.False(DocumentImagePolicy.HasSafeDimensions(BodyImageFixture.Png, "image/jpeg"));
        Assert.False(DocumentImagePolicy.HasSafeDimensions(BodyImageFixture.Png.AsSpan(0, 12), "image/png"));
        var huge = BodyImageFixture.Png.ToArray(); BinaryPrimitives.WriteInt32BigEndian(huge.AsSpan(16), 10000); BinaryPrimitives.WriteInt32BigEndian(huge.AsSpan(20), 10000);
        Assert.False(DocumentImagePolicy.HasSafeDimensions(huge, "image/png"));
        var owner = Guid.NewGuid(); var image = DocumentImage.Create(ImageId, owner, "sample.png", "image/png", BodyImageFixture.Png.Length, new string('a', 64), "private/image.png", Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(owner, image.DocumentId);
        Assert.Throws<DocumentTemplateValidationException>(() => DocumentImage.Create(ImageId, owner, "x.svg", "image/svg+xml", 10, new string('a', 64), "x", Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
    [Fact]
    public void Upload_touches_document_rowversion_without_creating_a_draft_and_checks_expected_revision()
    {
        var now = DateTimeOffset.UtcNow; var document = TranslationDocument.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        document.RecordImageUpload(0, now); Assert.True(document.UpdatedAtUtc > now); Assert.Equal(0, document.CurrentDraftRevision);
        Assert.Throws<DocumentRevisionConflictException>(() => document.RecordImageUpload(1, now));
    }
}
