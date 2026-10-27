using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class TypographyTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"typography-cases.json")));
        foreach(var sample in source.RootElement.EnumerateArray())yield return [sample.GetProperty("name").GetString()!,sample.GetProperty("attrs").GetRawText(),sample.GetProperty("valid").GetBoolean()];
    }
    private static string Body(JsonElement attrs)=>JsonSerializer.Serialize(new{type="doc",content=new[]{new{type="paragraph",content=new[]{new{type="text",text="İstanbul ترجمه",marks=new object[]{new{type="bold"},new{type="typography",attrs}}}}}}});
    [Theory,MemberData(nameof(Cases))]
    public void Client_and_server_share_bounded_typography_cases(string name,string json,bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        using var attrs=JsonDocument.Parse(json);var body=Body(attrs.RootElement);
        Assert.Equal(valid,DocumentTypography.ValidAttributes(attrs.RootElement));
        Assert.Equal(valid,DocumentContentDescriptor.RichV2.TryRead(body,out _));
        Assert.False(DocumentContentDescriptor.RichV1.TryRead(body,out _));Assert.False(DocumentContentDescriptor.Legacy.TryRead(body,out _));
        if(!valid)Assert.Throws<ArgumentException>(()=>DocumentTypography.Css(attrs.RootElement));
    }
    [Fact] public void Upgrade_is_a_new_revision_and_downgrades_or_unknown_marks_cannot_erase_styles()
    {
        var now=DateTimeOffset.UtcNow;var document=TranslationDocument.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),now);
        var old=document.AddDraftRevision(0,"{\"type\":\"doc\",\"content\":[]}",null,Guid.NewGuid(),now,DocumentContentDescriptor.RichFormat,1);
        using var attrs=JsonDocument.Parse("{\"fontFamily\":\"serif\",\"fontSize\":18,\"color\":\"#b91c1c\",\"highlight\":\"#fef08a\"}");
        var body=Body(attrs.RootElement);var styled=document.AddDraftRevision(1,body,"forged",Guid.NewGuid(),now,DocumentContentDescriptor.RichFormat,2);
        Assert.Equal(DocumentContentDescriptor.RichV2,styled.ContentDescriptor);Assert.Equal("İstanbul ترجمه",styled.PlainText);
        Assert.Equal(DocumentContentDescriptor.RichV1,old.ContentDescriptor);Assert.Equal("{\"type\":\"doc\",\"content\":[]}",old.EditorContentJson);
        Assert.Throws<DocumentValidationException>(()=>document.AddDraftRevision(2,body,null,Guid.NewGuid(),now,DocumentContentDescriptor.RichFormat,1));
        var next=document.AddDraftRevision(2,body,null,Guid.NewGuid(),now);Assert.Equal(styled.ContentDescriptor,next.ContentDescriptor);
        Assert.False(DocumentContentDescriptor.RichV2.TryRead(body.Replace("typography","foreignStyle"),out _));
    }
    [Fact] public void Renderer_emits_only_validated_styles_and_v1_never_implicitly_reads_v2()
    {
        using var attrs=JsonDocument.Parse("{\"fontFamily\":\"serif\",\"fontSize\":18,\"color\":\"#b91c1c\",\"highlight\":\"#fef08a\"}");
        var input=new DraftPdfInput(Guid.NewGuid(),1,null,Body(attrs.RootElement),null,null,null,null,new Dictionary<Guid,PdfImage>())
            {ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=2};
        var html=DraftPdfHtml.Prepare(input,"").Html;
        Assert.Contains("font-size:18pt",html);Assert.Contains("color:#b91c1c",html);Assert.Contains("background-color:#fef08a",html);Assert.Contains("Noto Serif",html);
        Assert.Throws<PdfRenderValidationException>(()=>DraftPdfHtml.Prepare(input with{ContentSchemaVersion=1},""));
        Assert.Throws<PdfRenderValidationException>(()=>DraftPdfHtml.Prepare(input with{ContentFormat=null,ContentSchemaVersion=null},""));
    }
}
