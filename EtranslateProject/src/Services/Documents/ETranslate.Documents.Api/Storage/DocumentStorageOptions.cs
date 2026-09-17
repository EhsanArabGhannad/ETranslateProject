namespace ETranslate.Documents.Api.Storage;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    public string RootPath { get; init; } = string.Empty;
}
