using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class ParagraphTests
{
    public static IEnumerable<object[]> Cases(){using var file=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"paragraph-cases.json")));foreach(var sample in file.RootElement.EnumerateArray())yield return [sample.GetProperty("name").GetString()!,sample.GetProperty("attrs").GetRawText(),sample.GetProperty("valid").GetBoolean()];}
    private static string Body(JsonElement attrs)=>JsonSerializer.Serialize(new{type="doc",content=new[]{new{type="paragraph",attrs,content=new[]{new{type="text",text="İstanbul ترجمه"}}}}});
    [Theory,MemberData(nameof(Cases))] public void V3_uses_shared_bounded_paragraph_values_while_older_versions_remain_strict(string name,string json,bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        using var attrs=JsonDocument.Parse(json);var body=Body(attrs.RootElement);
        Assert.Equal(valid,DocumentContentDescriptor.RichV3.TryRead(body,out _));
        Assert.False(DocumentContentDescriptor.RichV2.TryRead(body,out _));Assert.False(DocumentContentDescriptor.RichV1.TryRead(body,out _));Assert.False(DocumentContentDescriptor.Legacy.TryRead(body,out _));
    }
    [Fact] public void New_paragraph_revision_preserves_old_json_and_prevents_downgrades()
    {
        var now=DateTimeOffset.UtcNow;var actor=Guid.NewGuid();var document=TranslationDocument.Create(Guid.NewGuid(),Guid.NewGuid(),actor,now);
        var old=document.AddDraftRevision(0,"{\"type\":\"doc\",\"content\":[]}",null,actor,now,DocumentContentDescriptor.RichFormat,2);
        using var attrs=JsonDocument.Parse("{\"textAlign\":\"justify\",\"lineHeight\":2,\"indentLevel\":2}");var json=Body(attrs.RootElement);
        var current=document.AddDraftRevision(1,json,"forged",actor,now,DocumentContentDescriptor.RichFormat,3);
        Assert.Equal(DocumentContentDescriptor.RichV3,current.ContentDescriptor);Assert.Equal("İstanbul ترجمه",current.PlainText);Assert.Equal(DocumentContentDescriptor.RichV2,old.ContentDescriptor);
        Assert.Throws<DocumentValidationException>(()=>document.AddDraftRevision(2,json,null,actor,now,DocumentContentDescriptor.RichFormat,2));
        Assert.Throws<DocumentValidationException>(()=>document.AddDraftRevision(2,json,null,actor,now,DocumentContentDescriptor.RichFormat,1));
        Assert.Equal(DocumentContentDescriptor.RichV3,document.AddDraftRevision(2,json,null,actor,now).ContentDescriptor);
    }
    [Fact] public void Print_styles_keep_alignment_indentation_and_spacing_independent_of_direction()
    {
        using var attrs=JsonDocument.Parse("{\"textAlign\":\"left\",\"lineHeight\":1.5,\"indentLevel\":2}");var css=DocumentParagraph.Css(attrs.RootElement);
        Assert.Contains("text-align:left",css);Assert.Contains("line-height:1.5",css);Assert.Contains("margin-inline-start:10mm",css);Assert.DoesNotContain("direction",css);
        var input=new DraftPdfInput(Guid.NewGuid(),1,null,Body(attrs.RootElement),null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=3};
        Assert.Contains(css,DraftPdfHtml.Prepare(input,"").Html);
        Assert.Throws<PdfRenderValidationException>(()=>DraftPdfHtml.Prepare(input with{ContentSchemaVersion=2},""));
    }
}
