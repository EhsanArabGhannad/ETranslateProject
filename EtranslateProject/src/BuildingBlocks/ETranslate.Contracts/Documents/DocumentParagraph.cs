using System.Globalization;
using System.Text.Json;

namespace ETranslate.Contracts.Documents;

// Rich v3 base layout; v5 adds paragraph spacing and first-line indentation.
public static class DocumentParagraph
{
    public static IReadOnlyList<string> AdvancedFields { get; } = Array.AsReadOnly(new[]{"spaceBeforePt","spaceAfterPt","firstLineIndentMm"});
    public static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[]{"textAlign","lineHeight","indentLevel","spaceBeforePt","spaceAfterPt","firstLineIndentMm"});
    private static readonly decimal[] Heights = [1m,1.15m,1.5m,1.65m,2m,2.5m,3m];
    public static bool ValidValue(string field, JsonElement value,int version=3) => Fields.Contains(field) && (!AdvancedFields.Contains(field)||version>=5) && (value.ValueKind==JsonValueKind.Null || field switch
    {
        "textAlign" => value.ValueKind==JsonValueKind.String && value.GetString() is "left" or "center" or "right" or "justify",
        "lineHeight" => value.ValueKind==JsonValueKind.Number && value.TryGetDecimal(out var height) && Heights.Contains(height),
        "indentLevel" => value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var level) && level is >=0 and <=8,
        "spaceBeforePt" or "spaceAfterPt" => value.ValueKind==JsonValueKind.Number && value.TryGetDecimal(out var space) && space is >=0 and <=72 && decimal.Truncate(space)==space,
        "firstLineIndentMm" => value.ValueKind==JsonValueKind.Number && value.TryGetDecimal(out var indent) && indent is >=0 and <=40 && indent%.5m==0,
        _ => false
    });
    public static string Css(JsonElement attrs)
    {
        if(attrs.ValueKind!=JsonValueKind.Object)return "";
        List<string> rules=[];
        foreach(var field in Fields)
        {
            if(!attrs.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)continue;
            if(!ValidValue(field,value,5))throw new ArgumentException("Unsupported paragraph setting.");
            switch(field)
            {
                case "textAlign":rules.Add("text-align:"+value.GetString());break;
                case "lineHeight":
                    var height=value.GetDecimal().ToString("0.##",CultureInfo.InvariantCulture);
                    rules.Add("line-height:"+height);rules.Add("min-height:"+height+"em");break;
                case "indentLevel":rules.Add("margin-inline-start:"+(value.GetInt32()*5).ToString(CultureInfo.InvariantCulture)+"mm");break;
                case "spaceBeforePt":rules.Add("margin-top:"+value.GetDecimal().ToString("0.##",CultureInfo.InvariantCulture)+"pt");break;
                case "spaceAfterPt":rules.Add("margin-bottom:"+value.GetDecimal().ToString("0.##",CultureInfo.InvariantCulture)+"pt");break;
                case "firstLineIndentMm":rules.Add("text-indent:"+value.GetDecimal().ToString("0.##",CultureInfo.InvariantCulture)+"mm");break;
            }
        }
        return string.Join(';',rules);
    }
}
