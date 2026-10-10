using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180LocationNoteWriteRulesTests
{
    [Fact]
    public void Verified_member_can_edit_only_their_own_team_location_note()
        => V180LocationNoteWriteRules.RequireOwnTeamNote(1,1,7,7,10,10,true);

    [Theory]
    [InlineData(1,1,7,7,11,10,true)] // Same-team peer's location
    [InlineData(1,1,null,7,10,10,true)] // Readable shared/global location
    [InlineData(1,null,7,7,10,10,true)] // Global org-less location
    [InlineData(1,2,7,7,10,10,true)] // Foreign organization
    [InlineData(1,1,8,7,10,10,true)] // Foreign team
    [InlineData(null,1,7,7,10,10,true)] // Unbound actor organization
    [InlineData(1,1,7,7,10,10,false)] // Expired/missing membership or assignment
    [InlineData(1,1,7,7,null,10,true)] // Unowned/unknown creator
    public void Read_access_or_unverified_membership_cannot_be_used_as_write_access(
        int? actorOrg,int? locationOrg,int? locationTeam,int requestedTeam,
        int? createdBy,int currentUser,bool verifiedMembership)
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationNoteWriteRules.RequireOwnTeamNote(
                actorOrg,locationOrg,locationTeam,requestedTeam,createdBy,currentUser,verifiedMembership));
    }
}
