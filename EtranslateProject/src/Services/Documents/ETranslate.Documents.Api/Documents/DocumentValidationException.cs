namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("Document validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
