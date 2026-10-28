using ETranslate.Contracts.Documents;

namespace ETranslate.Web.Models;

public static class RichDocument
{
    public static bool TryGetPlainText(string json, out string? plainText)
        => DocumentContentDescriptor.RichV8.TryRead(json, out plainText);
}
