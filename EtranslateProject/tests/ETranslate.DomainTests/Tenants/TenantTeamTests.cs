using ETranslate.IdentityAccess.Api.Identity;
using ETranslate.IdentityAccess.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ETranslate.DomainTests.Tenants;

public sealed class TenantTeamTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
    private static readonly Guid TenantId = Guid.NewGuid();
    private static TenantMembership Member(TenantRole role, Guid? userId = null) => role == TenantRole.Owner
        ? TenantMembership.CreateOwner(TenantId, userId ?? Guid.NewGuid(), Now)
        : TenantMembership.CreateMember(TenantId, userId ?? Guid.NewGuid(), role, Now);

    [Theory] [InlineData(TenantRole.Owner, TenantRole.Administrator, true)] [InlineData(TenantRole.Owner, TenantRole.Translator, true)]
    [InlineData(TenantRole.Owner, TenantRole.Reviewer, true)] [InlineData(TenantRole.Owner, TenantRole.Owner, false)]
    [InlineData(TenantRole.Administrator, TenantRole.Administrator, false)] [InlineData(TenantRole.Administrator, TenantRole.Translator, true)]
    [InlineData(TenantRole.Administrator, TenantRole.Reviewer, true)] [InlineData(TenantRole.Administrator, TenantRole.Owner, false)]
    [InlineData(TenantRole.Translator, TenantRole.Translator, false)] [InlineData(TenantRole.Reviewer, TenantRole.Translator, false)]
    public void Assignment_RespectsPrivilegeCeiling(TenantRole actor, TenantRole target, bool allowed) => Assert.Equal(allowed, TenantTeamPolicy.CanAssign(Member(actor), target));
    [Theory] [InlineData(TenantRole.Owner)] [InlineData(TenantRole.Administrator)]
    public void NobodyCanChangeTheirOwnRole(TenantRole role)
    { var actor = Member(role); Assert.False(TenantTeamPolicy.CanChange(actor, actor, TenantRole.Translator)); }
    [Fact] public void OwnerIsProtectedAtDomainLevel()
    {
        var owner = Member(TenantRole.Owner);
        Assert.Throws<ArgumentException>(() => owner.ChangeRole(TenantRole.Translator));
        Assert.Throws<ArgumentException>(() => owner.SetActive(false));
        Assert.False(TenantTeamPolicy.CanChange(Member(TenantRole.Owner), owner, TenantRole.Administrator));
    }
    [Fact] public void AdministratorCannotManageAnotherAdministratorOrPromoteAnyoneToAdministrator()
    {
        var actor = Member(TenantRole.Administrator);
        Assert.False(TenantTeamPolicy.CanChange(actor, Member(TenantRole.Administrator), TenantRole.Translator));
        Assert.False(TenantTeamPolicy.CanChange(actor, Member(TenantRole.Translator), TenantRole.Administrator));
    }
    [Fact] public void InactiveManagersHaveNoAuthority()
    { var actor = Member(TenantRole.Administrator); actor.SetActive(false); Assert.False(TenantTeamPolicy.CanManage(actor)); Assert.False(TenantTeamPolicy.CanAssign(actor, TenantRole.Translator)); }
    [Fact] public void CrossTenantTargetsAreNotManageable()
    { var target = TenantMembership.CreateMember(Guid.NewGuid(), Guid.NewGuid(), TenantRole.Translator, Now); Assert.False(TenantTeamPolicy.CanChange(Member(TenantRole.Owner), target, TenantRole.Translator)); }
    [Theory] [InlineData(TenantRole.Owner)] [InlineData((TenantRole)0)] [InlineData((TenantRole)99)]
    public void InvalidMemberRolesCannotBeConstructed(TenantRole role) => Assert.Throws<ArgumentException>(() => TenantMembership.CreateMember(TenantId, Guid.NewGuid(), role, Now));
    [Fact] public void SoftDeactivationPreservesIdentityAndAllowsRejoin()
    {
        var member = Member(TenantRole.Translator); var id = member.UserId;
        member.SetActive(false); member.Rejoin(TenantRole.Reviewer);
        Assert.True(member.IsActive); Assert.Equal(id, member.UserId); Assert.Equal(Now, member.JoinedAtUtc); Assert.Equal(TenantRole.Reviewer, member.Role);
        Assert.Throws<InvalidOperationException>(() => member.Rejoin(TenantRole.Translator));
    }
    [Fact] public void TeamVersionIsMonotonicAndRejectsStaleChanges()
    {
        var tenant = Tenant.Create("Office", "office", TenantType.TranslationOffice, Now); tenant.TouchTeam(0);
        Assert.Equal(1, tenant.TeamVersion); Assert.Throws<InvalidOperationException>(() => tenant.TouchTeam(0)); Assert.Equal(1, tenant.TeamVersion);
    }
    private static (TenantInvitation Invitation, string Token) Invite() => TenantInvitation.Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), TenantRole.Translator, Now);
    [Fact] public void TokensAreRandomBoundToAccountAndStoredAsHashesOnly()
    {
        var first = Invite(); var second = Invite(); Assert.Equal(43, first.Token.Length);
        Assert.Equal(64, first.Invitation.TokenHash.Length); Assert.NotEqual(first.Token, first.Invitation.TokenHash);
        Assert.NotEqual(first.Token, second.Token); Assert.True(first.Invitation.Matches(first.Invitation.TargetUserId, first.Token));
        Assert.False(first.Invitation.Matches(Guid.NewGuid(), first.Token)); Assert.False(first.Invitation.Matches(first.Invitation.TargetUserId, second.Token));
        Assert.False(first.Invitation.Matches(first.Invitation.TargetUserId, null)); Assert.False(first.Invitation.Matches(first.Invitation.TargetUserId, new string('?', 43)));
    }
    [Fact] public void InvitationExpiresAfterSevenDays_AndBoundaryIsExclusive()
    {
        var invitation = Invite().Invitation; Assert.Equal(Now.AddDays(7), invitation.ExpiresAtUtc);
        Assert.True(invitation.IsPending(Now.AddDays(7).AddTicks(-1))); Assert.False(invitation.IsPending(Now.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Now.AddDays(7))); Assert.Throws<InvalidOperationException>(() => invitation.Expire(Now));
        invitation.Expire(Now.AddDays(7)); Assert.Equal(TenantInvitationStatus.Expired, invitation.Status);
    }
    [Fact] public void AcceptanceIsSingleUseAndTerminal()
    {
        var invitation = Invite().Invitation; invitation.Accept(Now.AddHours(1));
        Assert.Equal(TenantInvitationStatus.Accepted, invitation.Status); Assert.Equal(Now.AddHours(1), invitation.CompletedAtUtc);
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Now.AddHours(2))); Assert.Throws<InvalidOperationException>(() => invitation.Cancel(Now));
    }
    [Fact] public void CancelledLinkCannotBeAccepted()
    { var invitation = Invite().Invitation; invitation.Cancel(Now); Assert.False(invitation.IsPending(Now)); Assert.Throws<InvalidOperationException>(() => invitation.Accept(Now)); }
    [Fact] public void InvitationsNeverAssignOwner()
    { Assert.Throws<ArgumentException>(() => TenantInvitation.Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), TenantRole.Owner, Now)); }
    [Fact] public void DbSerializesTeamChangesAndEnforcesPendingInvitationUniqueness()
    {
        using var db = new IdentityAccessDbContext(new DbContextOptionsBuilder<IdentityAccessDbContext>().UseSqlServer("Server=test;Database=test;Integrated Security=True").Options);
        Assert.True(db.Model.FindEntityType(typeof(Tenant))!.FindProperty(nameof(Tenant.TeamVersion))!.IsConcurrencyToken);
        Assert.Contains(db.Model.FindEntityType(typeof(TenantInvitation))!.GetIndexes(), index => index.IsUnique && index.GetFilter() == "[Status] = 0");
    }
}
