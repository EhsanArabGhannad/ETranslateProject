using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Persistence;
using ETranslate.Documents.Api.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Documents;

public sealed class RowPlannerVersionTests
{
    [Fact]
    public void New_renderer_creates_another_identity_without_rewriting_archived_pdf_metadata()
    {
        var document=Guid.NewGuid();var draft=Guid.NewGuid();var actor=Guid.NewGuid();
        var old=new DocumentPdfVersion(document,draft,1,null,"draft-html-v10-footer-policy-pw1.63.0-noto-pdfsharp6.2.4","old.pdf",100,new string('a',64),actor,DateTimeOffset.UnixEpoch);
        var current=new DocumentPdfVersion(document,draft,1,null,DraftPdfPolicy.RendererVersion,"new.pdf",200,new string('b',64),actor,DateTimeOffset.UnixEpoch);
        Assert.NotEqual(old.Id,current.Id);Assert.Equal(old.DraftRevisionId,current.DraftRevisionId);
        Assert.NotEqual(old.RendererVersion,current.RendererVersion);Assert.Equal("old.pdf",old.StorageKey);Assert.Equal(new string('a',64),old.Sha256);
        Assert.Contains("v11-row-plan",DraftPdfPolicy.RendererVersion);Assert.Equal("signing-preview-v2-letterhead",SigningPreviewPolicy.RendererVersion);
        // Read only EF metadata; no SQL connection. Cache uniqueness must permit both versions.
        using var db=new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer("Server=localhost;Database=test;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity=db.Model.FindEntityType(typeof(DocumentPdfVersion))!;
        Assert.Contains(entity.GetIndexes(),index=>index.IsUnique && index.Properties.Select(property=>property.Name).SequenceEqual(["DraftRevisionId","RendererVersion"]));
        Assert.All(typeof(DocumentPdfVersion).GetProperties(),property=>Assert.False(property.SetMethod?.IsPublic ?? false));
    }
    [Fact]
    public void Row_planner_is_a_packaged_application_resource_not_a_user_script_or_runtime_dependency()
    {
        using var stream=typeof(DocumentRowPlanner).Assembly.GetManifestResourceStream("ETranslate.Documents.RowPlan");Assert.NotNull(stream);
        using var reader=new StreamReader(stream);var script=reader.ReadToEnd();
        Assert.Contains("source-sha256:",script);Assert.Contains("NativeTableRows",script);Assert.DoesNotContain("fetch(",script);
        Assert.DoesNotContain("@tiptap",script);Assert.DoesNotContain("XMLHttpRequest",script);
    }
}
