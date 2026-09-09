namespace ETranslate.TranslationWorkflow.Api.TranslationJobs;

public sealed class TranslationJobValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception("Translation job validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
