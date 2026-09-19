namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentTemplateValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("Document template validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
