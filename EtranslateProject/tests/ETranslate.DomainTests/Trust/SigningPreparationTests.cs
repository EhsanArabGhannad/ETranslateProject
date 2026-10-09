using ETranslate.Trust.Api.Dependencies;
using ETranslate.Trust.Api.Persistence;
using ETranslate.Trust.Api.Preparations;
using ETranslate.Trust.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Trust;

public sealed class SigningPreparationTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T12:00:00Z");
    private static PreparationArtifact Artifact() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        Guid.NewGuid(), Guid.NewGuid(), 3, null, new string('A', 64), 100, "DraftUnsigned", Guid.NewGuid(), Now, "[]");
    private static SigningPreparation Create(PreparationArtifact? artifact = null, string statement = " Proposed text\nبيان آزمایشی ",
        RequestedSignatureMethod method = RequestedSignatureMethod.EImza) => SigningPreparation.Create(artifact ?? Artifact(),
            "TranslatorOnly", Actor, Now, Guid.NewGuid(), statement, method, null, null, null);

    [Fact] public void Independent_HasOneImmutableProposedStage_NotASignature()
    {
        var artifact = Artifact(); var item = Create(artifact);
        Assert.Equal(SigningPreparationStatus.Prepared, item.Status); Assert.Equal(0, item.Version);
        Assert.Equal("DraftUnsigned", item.ArtifactKind); Assert.Equal(artifact.PdfVersionId, item.PdfVersionId);
        Assert.Equal(artifact.ApprovedByUserId, item.ApprovedByUserId); Assert.Equal(Actor, item.CreatedByUserId);
        Assert.Equal(new string('a', 64), item.PdfSha256);
        var stage = Assert.Single(item.Stages);
        Assert.Equal(1, stage.Order); Assert.Equal(PlannedSignerRole.Translator, stage.Role);
        Assert.Equal(" Proposed text\nبيان آزمایشی ", stage.ProposedStatement);
        Assert.Null(item.CancelledAtUtc); Assert.DoesNotContain("Signed", Enum.GetNames<SigningPreparationStatus>());
        Assert.False(item.Stages is List<PlannedSignatureStage>);
    }
    [Fact] public void Office_HasOrderedStages_AndPreservesBothStatements()
    {
        var translator = Guid.NewGuid(); var office = Guid.NewGuid();
        var item = SigningPreparation.Create(Artifact(), "TranslatorAndTranslationOffice", Actor, Now,
            translator, "Translator proposal", RequestedSignatureMethod.MobilImza, office, "Office proposal", RequestedSignatureMethod.EImza);
        Assert.Equal(2, item.Stages.Count);
        Assert.Collection(item.Stages, first => { Assert.Equal(translator, first.SignerUserId); Assert.Equal(1, first.Order); },
            second => { Assert.Equal(office, second.SignerUserId); Assert.Equal(2, second.Order); Assert.Equal(PlannedSignerRole.TranslationOffice, second.Role); });
    }
    [Fact] public void OnePersonMayHoldBothEligibleRoles_NoUnrequestedDistinctIdentityRule()
    {
        var item = SigningPreparation.Create(Artifact(), "TranslatorAndTranslationOffice", Actor, Now,
            Actor, "Translator proposal", RequestedSignatureMethod.EImza, Actor, "Office proposal", RequestedSignatureMethod.EImza);
        Assert.Equal(2, item.Stages.Count);
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")]
    public void ProposedStatement_IsRequired(string? statement) => Assert.Throws<ArgumentException>(() => Create(statement: statement!));
    [Theory] [InlineData(0)] [InlineData(3)]
    public void UnknownMethod_IsRejected(int method) => Assert.Throws<ArgumentException>(() => Create(method: (RequestedSignatureMethod)method));
    [Fact] public void StatementLimit_AppliesToRawInput_NotTrimmedInput()
    {
        Assert.Equal(4000, Assert.Single(Create(statement: new string('x', 4000)).Stages).ProposedStatement.Length);
        Assert.Throws<ArgumentException>(() => Create(statement: "x" + new string(' ', 4000)));
    }
    [Theory] [InlineData("")] [InlineData("bad")] [InlineData("SignedPdf")]
    public void InvalidArtifact_IsRejected(string value) => Assert.Throws<ArgumentException>(() => Create(Artifact() with { Kind = value }));
    [Theory] [InlineData(0)] [InlineData(26214401)]
    public void PdfMustBeBoundedAndNonempty(long size) => Assert.Throws<ArgumentException>(() => Create(Artifact() with { SizeBytes = size }));
    [Theory] [InlineData("abc")] [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void HashMustBeSha256Hex(string hash) => Assert.Throws<ArgumentException>(() => Create(Artifact() with { Sha256 = hash }));
    [Fact] public void EmptyApprovalIdentity_IsRejected() => Assert.Throws<ArgumentException>(() => Create(Artifact() with { ReviewId = Guid.Empty }));
    [Fact] public void UnsupportedPolicy_IsRejected() => Assert.Throws<ArgumentException>(() => SigningPreparation.Create(Artifact(), "ClientSuppliedPolicy", Actor, Now,
        Actor, "proposal", RequestedSignatureMethod.EImza, null, null, null));
    [Fact] public void Independent_CannotAddOfficeStage() => Assert.Throws<ArgumentException>(() => SigningPreparation.Create(Artifact(), "TranslatorOnly", Actor, Now,
        Actor, "proposal", RequestedSignatureMethod.EImza, Actor, "office", RequestedSignatureMethod.EImza));
    [Fact] public void OfficeStage_IsMandatory() => Assert.Throws<ArgumentException>(() => SigningPreparation.Create(Artifact(), "TranslatorAndTranslationOffice", Actor, Now,
        Actor, "proposal", RequestedSignatureMethod.EImza, null, null, null));
    [Fact] public void Cancellation_PreservesImmutableSnapshotAndRecordsActor()
    {
        var item = Create(); var hash = item.PdfSha256; var stage = Assert.Single(item.Stages); var actor = Guid.NewGuid();
        item.Cancel(0, actor, " Test cancellation ", Now.AddMinutes(1));
        Assert.Equal(SigningPreparationStatus.Cancelled, item.Status); Assert.Equal(1, item.Version);
        Assert.Equal(actor, item.CancelledByUserId); Assert.Equal("Test cancellation", item.CancellationReason);
        Assert.Equal(hash, item.PdfSha256); Assert.Same(stage, Assert.Single(item.Stages));
        Assert.Throws<InvalidOperationException>(() => item.Cancel(1, actor, "again", Now));
    }
    [Fact] public void StaleCancellation_DoesNotMutate() { var item = Create(); Assert.Throws<InvalidOperationException>(() => item.Cancel(1, Actor, "reason", Now)); Assert.Equal(0, item.Version); }
    [Theory] [InlineData("")] [InlineData(" ")]
    public void Cancellation_RequiresReason(string reason) { var item = Create(); Assert.Throws<ArgumentException>(() => item.Cancel(0, Actor, reason, Now)); Assert.Null(item.CancelledAtUtc); }
    [Fact] public void Cancellation_RejectsOversizedRawInput() => Assert.Throws<ArgumentException>(() => Create().Cancel(0, Actor, "x" + new string(' ', 2000), Now));
    [Fact] public void CurrentArtifact_MustMatchReviewPdfAndHash()
    {
        var item = Create(); Assert.True(item.MatchesCurrentArtifact(item.ReviewId, item.PdfVersionId, item.PdfSha256.ToUpperInvariant()));
        Assert.False(item.MatchesCurrentArtifact(Guid.NewGuid(), item.PdfVersionId, item.PdfSha256));
        Assert.False(item.MatchesCurrentArtifact(item.ReviewId, Guid.NewGuid(), item.PdfSha256));
        Assert.False(item.MatchesCurrentArtifact(item.ReviewId, item.PdfVersionId, new string('b', 64)));
    }
    [Fact] public void Provider_IsFailClosed() { var capability = new UnconfiguredSignatureProvider().Current; Assert.False(capability.RealSigningEnabled); Assert.False(capability.EImza); Assert.False(capability.MobilImza); }
    [Theory] [InlineData("Owner", true)] [InlineData("Administrator", true)] [InlineData("Translator", false)] [InlineData("Reviewer", false)]
    public void OnlyManagersCanPrepare(string role, bool manage)
    {
        var tenant = Guid.NewGuid(); var actor = new TrustActor(tenant, Actor, "TranslationOffice", role);
        Assert.True(actor.IsValid(tenant)); Assert.Equal(manage, actor.CanManage); Assert.Equal("TranslatorAndTranslationOffice", actor.SignaturePolicy);
        Assert.False(actor.IsValid(Guid.NewGuid()));
    }
    [Fact] public void SqlModel_EnforcesOnePreparedRequestAndOptimisticCancellation()
    {
        using var db = new TrustDbContext(new DbContextOptionsBuilder<TrustDbContext>().UseSqlServer("Server=localhost;Database=test;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity = db.Model.FindEntityType(typeof(SigningPreparation))!;
        Assert.True(entity.FindProperty(nameof(SigningPreparation.Version))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() == "[Status] = 0");
        var stages = db.Model.FindEntityType(typeof(PlannedSignatureStage))!;
        Assert.Equal(4000, stages.FindProperty(nameof(PlannedSignatureStage.ProposedStatement))!.GetMaxLength());
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(stages.GetForeignKeys()).DeleteBehavior);
    }
}
