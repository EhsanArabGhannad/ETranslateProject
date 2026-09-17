namespace ETranslate.Documents.Api.Documents;

public sealed class DocumentRevisionConflictException(
    int expectedRevision,
    int actualRevision) : Exception($"Expected revision {expectedRevision}, but the current revision is {actualRevision}.")
{
    public int ExpectedRevision { get; } = expectedRevision;
    public int ActualRevision { get; } = actualRevision;
}
