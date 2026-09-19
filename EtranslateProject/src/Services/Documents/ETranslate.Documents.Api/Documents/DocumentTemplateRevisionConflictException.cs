namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentTemplateRevisionConflictException(
    int expectedRevision,
    int actualRevision) : Exception(
        $"Expected template revision {expectedRevision}, but the current revision is {actualRevision}.")
{
    public int ExpectedRevision { get; } = expectedRevision;
    public int ActualRevision { get; } = actualRevision;
}
