namespace ETranslate.TranslationWorkflow.Api.TranslationJobs;

public enum TranslationJobStatus
{
    Draft = 1,
    InPreparation = 2,
    AwaitingTranslatorSignature = 3,
    AwaitingOfficeApproval = 4,
    Completed = 5,
    AwaitingNotary = 6,
    Notarized = 7,
    Rejected = 8,
    Canceled = 9
}
