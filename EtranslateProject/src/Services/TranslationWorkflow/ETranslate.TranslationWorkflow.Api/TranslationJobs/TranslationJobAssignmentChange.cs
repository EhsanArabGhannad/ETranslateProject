namespace ETranslate.TranslationWorkflow.Api.TranslationJobs;

public sealed class TranslationJobAssignmentChange
{
    private TranslationJobAssignmentChange() { }
    internal TranslationJobAssignmentChange(Guid translationJobId, Guid tenantId, long version, Guid actorUserId,
        Guid? previousTranslatorUserId, Guid? translatorUserId, string? note, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); TranslationJobId = translationJobId; TenantId = tenantId; Version = version;
        ActorUserId = actorUserId; PreviousTranslatorUserId = previousTranslatorUserId; TranslatorUserId = translatorUserId;
        Note = note; OccurredAtUtc = now;
    }
    public Guid Id { get; private init; }
    public Guid TranslationJobId { get; private init; }
    public Guid TenantId { get; private init; }
    public long Version { get; private init; }
    public Guid ActorUserId { get; private init; }
    public Guid? PreviousTranslatorUserId { get; private init; }
    public Guid? TranslatorUserId { get; private init; }
    public string? Note { get; private init; }
    public DateTimeOffset OccurredAtUtc { get; private init; }
}
