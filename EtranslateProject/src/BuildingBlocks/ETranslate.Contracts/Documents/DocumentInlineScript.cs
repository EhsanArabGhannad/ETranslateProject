namespace ETranslate.Contracts.Documents;

public static class DocumentInlineScript
{
    public static bool IsScript(string? type) => type is "superscript" or "subscript";
    public static int RenderOrder(string? type) => type=="typography"?2:IsScript(type)?1:0;
    public static string Css(string type) => "font-size:.75em;line-height:0;vertical-align:baseline;position:relative;top:" + (type switch
    {
        "superscript" => "-.5em", "subscript" => ".25em", _ => throw new ArgumentException("Unsupported inline script.")
    });
}
