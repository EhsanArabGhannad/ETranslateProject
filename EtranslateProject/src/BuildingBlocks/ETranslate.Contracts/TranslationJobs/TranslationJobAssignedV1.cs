namespace ETranslate.Contracts.TranslationJobs;

public sealed record TranslationJobAssignedV1(Guid EventId, Guid TenantId, Guid TranslationJobId, long AssignmentVersion,
    Guid ActorUserId, Guid? PreviousTranslatorUserId, Guid? TranslatorUserId, DateTimeOffset OccurredAtUtc);
