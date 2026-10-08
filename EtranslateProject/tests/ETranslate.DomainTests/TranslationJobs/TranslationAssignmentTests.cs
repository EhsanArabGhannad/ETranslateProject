using ETranslate.TranslationWorkflow.Api.Authorization;
using ETranslate.TranslationWorkflow.Api.Persistence;
using ETranslate.TranslationWorkflow.Api.TranslationJobs;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.TranslationJobs;

public sealed class TranslationAssignmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T15:00:00Z");
    private static TranslationJob Job() => TranslationJob.Create(Guid.NewGuid(), Guid.NewGuid(), TenantProviderType.TranslationOffice,
        "Passport", "fa", "tr", NotaryRequirement.NotRequired, null, null, null, Now);
    [Theory] [InlineData("Owner", true)] [InlineData("Administrator", true)] [InlineData("Translator", false)]
    [InlineData("Reviewer", false)] [InlineData("Unknown", false)]
    public void OnlyManagersCanAssign(string role, bool allowed) => Assert.Equal(allowed,
        new TenantActor(Guid.NewGuid(), Guid.NewGuid(), TenantProviderType.TranslationOffice, role).CanAssignTranslators);
    [Fact] public void ExistingJobsStartUnassigned()
    { var job = Job(); Assert.Null(job.AssignedTranslatorUserId); Assert.Equal(0, job.AssignmentVersion); Assert.Null(job.AssignmentChangedAtUtc); }
    [Fact] public void AssignmentPreservesCreationIdentityAndRecordsActualManager()
    {
        var job = Job(); var creator = job.CreatedByUserId; var actor = Guid.NewGuid(); var target = Guid.NewGuid();
        var change = job.AssignTranslator(target, actor, 0, "  First assignment  ", Now.AddHours(1));
        Assert.Equal(target, job.AssignedTranslatorUserId); Assert.Equal(1, job.AssignmentVersion);
        Assert.Equal(actor, job.AssignmentChangedByUserId); Assert.Equal(creator, job.CreatedByUserId);
        Assert.Equal(Now, job.CreatedAtUtc); Assert.Equal(Now.AddHours(1), job.AssignmentChangedAtUtc);
        Assert.Equal("First assignment", change.Note); Assert.Null(change.PreviousTranslatorUserId);
        Assert.Equal(job.TenantId, change.TenantId); Assert.Equal(job.Id, change.TranslationJobId);
        Assert.Equal(SignaturePolicy.TranslatorAndTranslationOffice, job.SignaturePolicy); Assert.Equal(TranslationJobStatus.Draft, job.Status);
    }
    [Fact] public void ReassignmentAndUnassignmentHaveMonotonicHistory()
    {
        var job = Job(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var actor = Guid.NewGuid();
        var first = job.AssignTranslator(a, actor, 0, null, Now);
        var second = job.AssignTranslator(b, actor, 1, null, Now.AddMinutes(1));
        var third = job.AssignTranslator(null, actor, 2, "  ", Now.AddMinutes(2));
        Assert.Equal(a, second.PreviousTranslatorUserId); Assert.Equal(b, third.PreviousTranslatorUserId);
        Assert.Null(third.TranslatorUserId); Assert.Null(third.Note); Assert.Null(job.AssignedTranslatorUserId);
        Assert.Equal(3, job.AssignmentVersion); Assert.NotEqual(first.Id, second.Id);
    }
    [Fact] public void StaleAndDuplicateAssignmentsDoNotMutate()
    {
        var job = Job(); var actor = Guid.NewGuid(); var target = Guid.NewGuid(); job.AssignTranslator(target, actor, 0, null, Now);
        Assert.Throws<InvalidOperationException>(() => job.AssignTranslator(Guid.NewGuid(), actor, 0, null, Now));
        Assert.Throws<InvalidOperationException>(() => job.AssignTranslator(target, actor, 1, null, Now));
        Assert.Equal(target, job.AssignedTranslatorUserId); Assert.Equal(1, job.AssignmentVersion);
    }
    [Fact] public void InvalidIdentifiersAndLongNotesAreRejected()
    {
        var job = Job(); var actor = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => job.AssignTranslator(Guid.NewGuid(), Guid.Empty, 0, null, Now));
        Assert.Throws<ArgumentException>(() => job.AssignTranslator(Guid.Empty, actor, 0, null, Now));
        Assert.Throws<ArgumentException>(() => job.AssignTranslator(Guid.NewGuid(), actor, 0, new string('a', 501), Now));
        Assert.Throws<ArgumentException>(() => job.AssignTranslator(Guid.NewGuid(), actor, 0, new string(' ', 501), Now));
        Assert.Equal(0, job.AssignmentVersion); Assert.Null(job.AssignedTranslatorUserId);
    }
    [Fact] public void MetadataEditsPreserveAssignment()
    {
        var job = Job(); var target = Guid.NewGuid(); job.AssignTranslator(target, Guid.NewGuid(), 0, null, Now);
        job.UpdateDraft("Updated", "fa", "tr", NotaryRequirement.NotRequired, null, null, null, Now.AddHours(2));
        Assert.Equal(target, job.AssignedTranslatorUserId); Assert.Equal(1, job.AssignmentVersion);
    }
    [Fact] public void EfModelProtectsAssignmentVersionAndUniqueHistory()
    {
        using var db = new TranslationWorkflowDbContext(new DbContextOptionsBuilder<TranslationWorkflowDbContext>().UseSqlServer("Server=test;Database=test;Integrated Security=True").Options);
        Assert.True(db.Model.FindEntityType(typeof(TranslationJob))!.FindProperty(nameof(TranslationJob.AssignmentVersion))!.IsConcurrencyToken);
        Assert.Contains(db.Model.FindEntityType(typeof(TranslationJobAssignmentChange))!.GetIndexes(), item => item.IsUnique);
    }
}
