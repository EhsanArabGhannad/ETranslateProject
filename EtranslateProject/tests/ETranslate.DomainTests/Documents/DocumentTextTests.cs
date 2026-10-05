using ETranslate.Web.Models;

namespace ETranslate.DomainTests.Documents;

public sealed class DocumentTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("ترجمه\nÇeviri\n<script>alert(1)</script>\n")]
    [InlineData("line\n\nnext")]
    public void PlainTextRoundTripsWithoutHtmlInterpretation(string text)
        => Assert.Equal(text, DocumentText.TryPlainText(DocumentText.FromPlainText(text)));

    [Theory]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"bold\",\"marks\":[{\"type\":\"bold\"}]}]}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"assetId\":\"123\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"textAlign\":\"center\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"custom\":true,\"content\":[]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":null}]}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"type\":42}")]
    public void RichOrUnknownDocumentCannotBeFlattened(string json)
        => Assert.Null(DocumentText.TryPlainText(json));

    [Fact] public void EmptyDocumentIsEditable() => Assert.Equal("", DocumentText.TryPlainText(DocumentText.Empty));
}
