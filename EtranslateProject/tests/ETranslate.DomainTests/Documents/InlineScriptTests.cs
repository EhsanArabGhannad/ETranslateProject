using System.Text.Json;
using ETranslate.Contracts.Documents;
using ETranslate.Documents.Api.Documents;
using ETranslate.Documents.Api.Rendering;

namespace ETranslate.DomainTests.Documents;

public sealed class InlineScriptTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var file=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"inline-script-cases.json")));
        foreach(var sample in file.RootElement.EnumerateArray())yield return [sample.GetProperty("name").GetString()!,sample.GetProperty("marks").GetRawText(),sample.GetProperty("valid").GetBoolean()];
    }
    private static string Body(string marks)=>"{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"dir\":\"rtl\"},\"content\":[{\"type\":\"text\",\"text\":\"۲ İstanbul\",\"marks\":"+marks+"}]}]}";
    [Theory,MemberData(nameof(Cases))]
    public void V4_accepts_only_bounded_nonconflicting_script_marks(string name,string marks,bool valid)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));var body=Body(marks);
        Assert.Equal(valid,DocumentContentDescriptor.RichV4.TryRead(body,out _));
        foreach(var older in new[]{DocumentContentDescriptor.Legacy,DocumentContentDescriptor.RichV1,DocumentContentDescriptor.RichV2,DocumentContentDescriptor.RichV3})Assert.False(older.TryRead(body,out _));
    }
    [Fact] public void Upgrade_keeps_archived_content_and_prevents_downgrade()
    {
        var now=DateTimeOffset.UtcNow;var actor=Guid.NewGuid();var document=TranslationDocument.Create(Guid.NewGuid(),Guid.NewGuid(),actor,now);
        var old=document.AddDraftRevision(0,"{\"type\":\"doc\",\"content\":[]}",null,actor,now,DocumentContentDescriptor.RichFormat,3);
        var current=document.AddDraftRevision(1,Body("[{\"type\":\"superscript\"},{\"type\":\"strike\"}]"),"forged",actor,now,DocumentContentDescriptor.RichFormat,4);
        Assert.Equal("۲ İstanbul",current.PlainText);Assert.Equal(DocumentContentDescriptor.RichV3,old.ContentDescriptor);
        Assert.Equal(DocumentContentDescriptor.RichV4,current.ContentDescriptor);
        Assert.Equal(DocumentContentDescriptor.RichV4,document.AddDraftRevision(2,current.EditorContentJson,null,actor,now).ContentDescriptor);
        foreach(var version in new[]{1,2,3})Assert.Throws<DocumentValidationException>(()=>document.AddDraftRevision(3,current.EditorContentJson,null,actor,now,DocumentContentDescriptor.RichFormat,version));
    }
    [Fact] public void Print_nests_scripts_inside_typography_for_either_input_order_and_retains_legacy_strike()
    {
        foreach(var marks in new[]{"[{\"type\":\"typography\",\"attrs\":{\"fontSize\":18}},{\"type\":\"superscript\"}]","[{\"type\":\"superscript\"},{\"type\":\"typography\",\"attrs\":{\"fontSize\":18}}]"})
        {
            var input=new DraftPdfInput(Guid.NewGuid(),1,null,Body(marks),null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=4};
            Assert.Contains("<span style=\"font-size:18pt\"><sup style=\""+DocumentInlineScript.Css("superscript"),DraftPdfHtml.Prepare(input,"").Html);
            Assert.Throws<PdfRenderValidationException>(()=>DraftPdfHtml.Prepare(input with{ContentSchemaVersion=3},""));
        }
        var legacy=Body("[{\"type\":\"strike\"}]");Assert.True(DocumentContentDescriptor.RichV1.TryRead(legacy,out _));
        var colored=Body("[{\"type\":\"typography\",\"attrs\":{\"color\":\"#15803d\"}},{\"type\":\"strike\"}]");
        var oldInput=new DraftPdfInput(Guid.NewGuid(),1,null,colored,null,null,null,null,new Dictionary<Guid,PdfImage>()){ContentFormat=DocumentContentDescriptor.RichFormat,ContentSchemaVersion=3};
        Assert.Contains("<s><span style=\"color:#15803d\">",DraftPdfHtml.Prepare(oldInput,"").Html);
        Assert.Contains("<span style=\"color:#15803d\"><s>",DraftPdfHtml.Prepare(oldInput with{ContentSchemaVersion=4},"").Html);
        Assert.Throws<ArgumentException>(()=>DocumentInlineScript.Css("unknown"));
    }
}
