using System.Reflection;
using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Endpoints;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Rendering;
using ETranslate.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ETranslate.DomainTests.Documents;

public sealed class DocumentContentVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private const string Body = """{ "type":"doc", "content":[{"type":"paragraph","attrs":{"dir":"rtl"},"content":[{"type":"text","text":"ترجمه با نیم‌فاصله İstanbul 123","marks":[{"type":"bold"}]}]}] }""";
    private const string Opaque = """{ "vendor": "original", "content": [{"unknown":true}], "style": "retain exactly" }""";
    private static TranslationDocument Document(Guid? tenant = null) => TranslationDocument.Create(tenant ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
    private static DocumentTemplate Template(Guid tenant, string body = Body, string? format = null, int? version = null)
        => DocumentTemplate.Create(tenant, "Fixture", null, body, "{\"type\":\"doc\",\"content\":[]}", null, "{}", null,
            Guid.NewGuid(), Now, format, version);

    [Fact] public void Legacy_content_is_not_relabelled_or_rewritten_and_new_rich_revisions_are_explicit()
    {
        var document = Document();
        var legacy = document.AddDraftRevision(0, Opaque, "legacy projection", Guid.NewGuid(), Now);
        Assert.Null(legacy.ContentFormat); Assert.Null(legacy.ContentSchemaVersion); Assert.Equal(Opaque, legacy.EditorContentJson);
        var rich = document.AddDraftRevision(1, Body, "forged projection", Guid.NewGuid(), Now,
            DocumentContentDescriptor.RichFormat, 1);
        Assert.Equal(DocumentContentDescriptor.RichV1, rich.ContentDescriptor);
        Assert.Equal(Body, rich.EditorContentJson); Assert.Equal("ترجمه با نیم‌فاصله İstanbul 123", rich.PlainText);
        Assert.Equal(Opaque, legacy.EditorContentJson); Assert.Null(legacy.ContentFormat);
        // An old request omitting both fields inherits the current declaration; no downgrade loophole.
        var inherited = document.AddDraftRevision(2, Body, null, Guid.NewGuid(), Now);
        Assert.Equal(rich.ContentDescriptor, inherited.ContentDescriptor);
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(3, Opaque, null, Guid.NewGuid(), Now));
        Assert.Equal(3, document.CurrentDraftRevision);
    }

    [Theory]
    [InlineData("etranslate.rich-document", 7)] [InlineData("sfdt", 1)]
    [InlineData(null, 1)] [InlineData("etranslate.rich-document", null)]
    [InlineData("", 1)] [InlineData("etranslate.rich-document", 0)] [InlineData("etranslate.legacy-json", 0)]
    public void Unsupported_or_partial_write_metadata_is_rejected_before_state_changes(string? format, int? version)
    {
        var document = Document();
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(0, Body, null, Guid.NewGuid(), Now, format, version));
        Assert.Empty(document.DraftRevisions); Assert.Equal(0, document.CurrentDraftRevision);
        Assert.Throws<DocumentTemplateValidationException>(() => Template(document.TenantId, Body, format, version));
    }

    [Theory] [InlineData("{}")] [InlineData("[]")] [InlineData("{\"type\":\"doc\",\"nativeState\":true}")] [InlineData(null)]
    public void A_rich_declaration_does_not_allow_opaque_or_invalid_content(string? json)
    {
        var document = Document();
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(0, json!, null, Guid.NewGuid(), Now, DocumentContentDescriptor.RichFormat, 1));
        Assert.Empty(document.DraftRevisions);
    }

    [Fact] public void Declared_rich_projection_limit_is_enforced_after_extraction()
    {
        var document = Document();
        var json = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = new string('x', 1_000_001) } } } } });
        var error = Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(0, json, null, Guid.NewGuid(), Now, DocumentContentDescriptor.RichFormat, 1));
        Assert.Contains(nameof(DocumentDraftRevision.PlainText), error.Errors.Keys);
    }

    [Fact] public void Detached_aggregate_requires_exact_current_metadata_and_preserves_it_when_supplied()
    {
        var document = Document();
        var original = document.AddDraftRevision(0, Body, null, Guid.NewGuid(), Now, DocumentContentDescriptor.RichFormat, 1);
        var detached = (TranslationDocument)Activator.CreateInstance(typeof(TranslationDocument), nonPublic: true)!;
        Set(detached, nameof(detached.Id), document.Id); Set(detached, nameof(detached.CurrentDraftRevision), 1);
        Assert.Throws<DocumentValidationException>(() => detached.AddDraftRevision(1, Body, null, Guid.NewGuid(), Now));
        var foreign = Document().AddDraftRevision(0, Body, null, Guid.NewGuid(), Now);
        Assert.Throws<DocumentValidationException>(() => detached.AddDraftRevision(1, Body, null, Guid.NewGuid(), Now, previousRevision: foreign));
        var next = detached.AddDraftRevision(1, Body, null, Guid.NewGuid(), Now, previousRevision: original);
        Assert.Equal(DocumentContentDescriptor.RichV1, next.ContentDescriptor);
    }

    [Fact] public void Future_content_cannot_be_downgraded_by_omitting_metadata_or_claiming_v1()
    {
        var document = Document(); var original = document.AddDraftRevision(0, Body, null, Guid.NewGuid(), Now, DocumentContentDescriptor.RichFormat, 1);
        // Simulate a revision read from a database written by a future release, not a supported write path.
        Set(original, nameof(original.ContentSchemaVersion), 7);
        Assert.False(original.ContentDescriptor.TryRead(Body, out _));
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(1, Body, null, Guid.NewGuid(), Now));
        Assert.Throws<DocumentValidationException>(() => document.AddDraftRevision(1, Body, null, Guid.NewGuid(), Now, DocumentContentDescriptor.RichFormat, 1));
        Assert.Equal(Body, original.EditorContentJson); Assert.Equal(1, document.CurrentDraftRevision);
    }

    [Fact] public void Template_versions_and_body_metadata_follow_the_correct_immutable_snapshot()
    {
        var document = Document(); var template = Template(document.TenantId, format: DocumentContentDescriptor.RichFormat, version: 1);
        var first = document.ApplyTemplate(template, template.Revisions.Single(), Guid.NewGuid(), Now);
        Assert.Equal(DocumentContentDescriptor.RichV1, first.ContentDescriptor);
        var other = Template(document.TenantId); // legacy template must not downgrade the rich body
        var changed = document.ChangeTemplate(1, other, other.Revisions.Single(), first, Guid.NewGuid(), Now);
        Assert.Equal(first.ContentDescriptor, changed.ContentDescriptor); Assert.Equal(Body, changed.EditorContentJson);
        Assert.Equal(template.Revisions.Single().Id, first.TemplateRevisionId); Assert.Equal(other.Revisions.Single().Id, changed.TemplateRevisionId);
        Assert.True(other.Revisions.Single().ContentDescriptor.IsLegacy);
        var nextTemplate = template.AddRevision(1, Body, "{\"type\":\"header\",\"content\":[]}", null, "{}", null, Guid.NewGuid(), Now);
        Assert.Equal(DocumentContentDescriptor.RichV1, nextTemplate.ContentDescriptor);
        Assert.Throws<DocumentTemplateValidationException>(() => template.AddRevision(2, Opaque, null, null, "{}", null, Guid.NewGuid(), Now));
        Set(nextTemplate, nameof(nextTemplate.ContentSchemaVersion), 7);
        Assert.Throws<DocumentTemplateValidationException>(() => template.AddRevision(2, Body, null, null, "{}", null, Guid.NewGuid(), Now));
        Assert.Throws<DocumentValidationException>(() => Document(document.TenantId).ApplyTemplate(template, nextTemplate, Guid.NewGuid(), Now));
    }

    [Fact] public void Rich_template_regions_are_validated_without_restricting_legacy_opaque_storage()
    {
        var legacy = Template(Guid.NewGuid(), Opaque); Assert.Equal(Opaque, legacy.Revisions.Single().EditorContentJson);
        Assert.Throws<DocumentTemplateValidationException>(() => DocumentTemplate.Create(Guid.NewGuid(), "Fixture", null, Body,
            Opaque, null, "{}", null, Guid.NewGuid(), Now, DocumentContentDescriptor.RichFormat, 1));
    }

    [Fact] public void Known_v1_declaration_keeps_existing_prepared_html_identical_but_unknown_versions_fail_closed()
    {
        var input = new DraftPdfInput(Guid.NewGuid(), 1, null, Body, null, null, null, null, new Dictionary<Guid, PdfImage>());
        var legacyHtml = DraftPdfHtml.Prepare(input, "").Html;
        var rich = input with { ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 1 };
        Assert.Equal(legacyHtml, DraftPdfHtml.Prepare(rich, "").Html);
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(rich with { ContentSchemaVersion = 7 }, ""));
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.Prepare(rich with { TemplateContentFormat = "sfdt", TemplateContentSchemaVersion = 1 }, ""));
        Assert.Throws<PdfRenderValidationException>(() => DraftPdfHtml.ImageReferences(null, "doc", new("sfdt", 1)));
        // No PDF file is generated in this test; only deterministic preparation/dispatch is checked.
    }

    [Fact] public void Web_models_never_project_or_enable_future_content_even_when_the_payload_looks_like_v1()
    {
        var draft = JsonSerializer.Deserialize<DraftView>("""{"RevisionNumber":1,"EditorContentJson":"{}","PlainText":null}""")!;
        Assert.True(draft.ContentDescriptor.IsLegacy);
        var editor = new EditorView { EditorContentJson = Body, ContentFormat = DocumentContentDescriptor.RichFormat, ContentSchemaVersion = 7 };
        Assert.False(editor.ContentIsSupported);
        var template = new TemplateRevision(Guid.NewGuid(), Guid.NewGuid(), 1, Body, null, null, "{}", null, DocumentContentDescriptor.RichFormat, 7);
        var detail = new TemplateDetail(template.TemplateId, "Fixture", null, true, 1, template);
        var projection = TemplateProjection.From(detail, Guid.NewGuid(), out _);
        Assert.False(projection.BodyEditable); Assert.False(projection.HeaderEditable); Assert.False(projection.CanAppendLogo);
        var settings = new SettingsPageView { Selected = detail, WriteStatus = ETranslate.SubscriptionAccess.SubscriptionWriteStatus.Allowed,
            Tenant = new(Guid.NewGuid(), "Fixture", "TranslationOffice", "Owner") };
        Assert.True(settings.CanWrite); Assert.False(settings.CanSave);
    }

    [Fact] public void API_requests_remain_backward_compatible_without_a_vendor_envelope()
    {
        var request = JsonSerializer.Deserialize<CreateDraftRevisionRequest>("""{"ExpectedCurrentRevision":0,"EditorContentJson":"{}","PlainText":null}""")!;
        Assert.Null(request.ContentFormat); Assert.Null(request.ContentSchemaVersion); Assert.Equal("{}", request.EditorContentJson);
    }

    [Fact] public void Migration_is_additive_nullable_and_contains_no_content_backfill_or_archive_rewrite()
    {
        // Only build the SQL Server model and migration script; this address is never contacted.
        using var db = new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=NotConnected;Integrated Security=True;Encrypt=True").Options);
        var migrations = db.GetService<IMigrationsAssembly>();
        var id = Assert.Single(migrations.Migrations.Keys, id => id.EndsWith("_AddDocumentContentFormats", StringComparison.Ordinal));
        var migration = migrations.CreateMigration(migrations.Migrations[id], db.Database.ProviderName!);
        Assert.Equal(6, migration.UpOperations.Count);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(4, columns.Length); Assert.All(columns, column => {
            Assert.True(column.IsNullable); Assert.Null(column.DefaultValue); Assert.Null(column.DefaultValueSql);
            Assert.Contains(column.Table, new[] { "draft_revisions", "document_template_revisions" });
        });
        Assert.Equal(2, migration.UpOperations.OfType<AddCheckConstraintOperation>().Count());
        Assert.False(db.Database.HasPendingModelChanges());
        var previous = migrations.Migrations.Keys.Where(key => string.CompareOrdinal(key, id) < 0).Order().Last();
        var sql = db.GetService<IMigrator>().GenerateScript(previous, id);
        Assert.DoesNotContain("UPDATE [documents]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EditorContentJson", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pdf_versions", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);
}
