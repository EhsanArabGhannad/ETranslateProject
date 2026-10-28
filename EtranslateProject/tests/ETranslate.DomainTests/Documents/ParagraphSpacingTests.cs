using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class ParagraphSpacingTests
{
    public static IEnumerable<object[]> Cases(){using var file=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"paragraph-spacing-cases.json")));foreach(var sample in file.RootElement.EnumerateArray())yield return [sample.GetProperty("name").GetString()!,sample.GetProperty("attrs").GetRawText(),sample.GetProperty("valid").GetBoolean()];}
    private static string Body(JsonElement attrs)=>JsonSerializer.Serialize(new{type="doc",content=new[]{new{type="paragraph",attrs,content=new[]{new{type="text",text="İstanbul ترجمه"}}}}});
    [Theory,MemberData(nameof(Cases))]
    public void V5_bounded_spacing_keeps_all_previous_vocabularies_strict(string name,string json,bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));using var attrs=JsonDocument.Parse(json);var body=Body(attrs.RootElement);
        Assert.Equal(valid,DocumentContentDescriptor.RichV5.TryRead(body,out _));
        foreach(var old in new[]{DocumentContentDescriptor.Legacy,DocumentContentDescriptor.RichV1,DocumentContentDescriptor.RichV2,DocumentContentDescriptor.RichV3,DocumentContentDescriptor.RichV4})Assert.False(old.TryRead(body,out _));
    }
    [Fact] public void New_spacing_revision_preserves_old_content_and_prevents_downgrades()
    {
        var actor=Guid.NewGuid();var now=DateTimeOffset.UtcNow;var document=TranslationDocument.Create(Guid.NewGuid(),Guid.NewGuid(),actor,now);
        var old=document.AddDraftRevision(0,"{\"type\":\"doc\",\"content\":[]}",null,actor,now,DocumentContentDescriptor.RichFormat,4);
        using var attrs=JsonDocument.Parse("{\"spaceBeforePt\":12,\"spaceAfterPt\":6,\"firstLineIndentMm\":12.5}");var body=Body(attrs.RootElement);
        var current=document.AddDraftRevision(1,body,"forged",actor,now,DocumentContentDescriptor.RichFormat,5);
        Assert.Equal("İstanbul ترجمه",current.PlainText);Assert.Equal(DocumentContentDescriptor.RichV4,old.ContentDescriptor);Assert.Equal("{\"type\":\"doc\",\"content\":[]}",old.EditorContentJson);
        foreach(var version in new[]{1,2,3,4})Assert.Throws<DocumentValidationException>(()=>document.AddDraftRevision(2,body,null,actor,now,DocumentContentDescriptor.RichFormat,version));
        Assert.Equal(DocumentContentDescriptor.RichV5,document.AddDraftRevision(2,body,null,actor,now).ContentDescriptor);
    }
    [Fact] public void Print_uses_bounded_values_and_old_html_stays_identical_when_no_new_settings_exist()
    {
        using var attrs=JsonDocument.Parse("{\"spaceBeforePt\":12,\"spaceAfterPt\":0,\"firstLineIndentMm\":12.5,\"dir\":\"rtl\"}");
        var input=new DraftPdfInput(Guid.NewGuid(),1,null,Body(attrs.RootElement),null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=5};
        Assert.Contains("margin-top:12pt;margin-bottom:0pt;text-indent:12.5mm",DraftPdfHtml.Prepare(input,"").Html);
        Assert.Throws<PdfRenderValidationException>(()=>DraftPdfHtml.Prepare(input with{ContentSchemaVersion=4},""));
        const string old="{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"indentLevel\":2},\"content\":[{\"type\":\"text\",\"text\":\"old\"}]}]}";
        Assert.Equal(DraftPdfHtml.Prepare(input with{BodyJson=old,ContentSchemaVersion=4},"").Html,DraftPdfHtml.Prepare(input with{BodyJson=old},"").Html);
    }
}
