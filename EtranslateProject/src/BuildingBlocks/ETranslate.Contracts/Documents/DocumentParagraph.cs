using System.Globalization;
using System.Text.Json;

namespace ETranslate.Contracts.Documents;

// Rich v3: paragraph/heading layout, independently of text direction and inline marks.
public static class DocumentParagraph
{
    public static IReadOnlyList<string> Fields { get; } = Array.AsReadOnly(new[]{"textAlign","lineHeight","indentLevel"});
    private static readonly decimal[] Heights = [1m,1.15m,1.5m,1.65m,2m,2.5m,3m];
    public static bool ValidValue(string field, JsonElement value) => Fields.Contains(field) && (value.ValueKind==JsonValueKind.Null || field switch
    {
        "textAlign" => value.ValueKind==JsonValueKind.String && value.GetString() is "left" or "center" or "right" or "justify",
        "lineHeight" => value.ValueKind==JsonValueKind.Number && value.TryGetDecimal(out var height) && Heights.Contains(height),
        "indentLevel" => value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var level) && level is >=0 and <=8,
        _ => false
    });
    public static string Css(JsonElement attrs)
    {
        if(attrs.ValueKind!=JsonValueKind.Object)return "";
        List<string> rules=[];
        foreach(var field in Fields)
        {
            if(!attrs.TryGetProperty(field,out var value)||value.ValueKind==JsonValueKind.Null)continue;
            if(!ValidValue(field,value))throw new ArgumentException("Unsupported paragraph setting.");
            switch(field)
            {
                case "textAlign":rules.Add("text-align:"+value.GetString());break;
                case "lineHeight":
                    var height=value.GetDecimal().ToString("0.##",CultureInfo.InvariantCulture);
                    rules.Add("line-height:"+height);rules.Add("min-height:"+height+"em");break;
                case "indentLevel":rules.Add("margin-inline-start:"+(value.GetInt32()*5).ToString(CultureInfo.InvariantCulture)+"mm");break;
            }
        }
        return string.Join(';',rules);
    }
}
