using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180LocationOwnershipRulesTests
{
    private static CurrentUserDto Actor(int id,string role,int team=7) =>
        new(id,$"test{id}","Test",null,1,team,"Team",
            new[]{role},new[]{new TeamScopeDto(team,"Team",true)});

    [Fact] public void Visitor_may_create_pending_only_in_effective_team()
    {
        V180LocationOwnershipRules.EnsureDraftCreate(Actor(1,"visitor"),7,new[]{7},false,"Customer");
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftCreate(Actor(1,"visitor"),8,new[]{7},false,"Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftCreate(Actor(1,"visitor"),null,new[]{7},false,"Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftCreate(Actor(1,"visitor"),7,new[]{7},true,"Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftCreate(Actor(1,"visitor"),7,new[]{7},false,"Official"));
    }

    [Fact] public void Visitor_cannot_edit_another_visitors_draft_in_same_team()
    {
        V180LocationOwnershipRules.EnsureDraftUpdate(Actor(1,"visitor"),1,7,1,"Pending",false,new[]{7},7,false,"Customer","Customer");
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(2,"visitor"),1,7,1,"Pending",false,new[]{7},7,false,"Customer","Customer"));
    }

    [Fact] public void Leader_must_manage_active_own_team_and_never_publish_or_change_owner_team()
    {
        V180LocationOwnershipRules.EnsureDraftUpdate(Actor(3,"leader"),1,7,1,"Pending",false,new[]{7},7,false,"Customer","Customer");
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(3,"leader"),1,8,1,"Pending",false,new[]{7},8,false,"Customer","Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(3,"leader"),1,7,1,"Approved",true,new[]{7},7,true,"Customer","Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(3,"leader"),1,7,1,"Pending",false,new[]{7},8,false,"Customer","Customer"));
    }

    [Fact] public void Shared_and_foreign_org_drafts_denied_even_if_visitors_can_read_them()
    {
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(1,"visitor"),1,null,1,"Pending",false,new[]{7},null,false,"Customer","Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(1,"visitor"),null,7,1,"Pending",false,new[]{7},7,false,"Customer","Customer"));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationOwnershipRules.EnsureDraftUpdate(Actor(1,"visitor"),2,7,1,"Pending",false,new[]{7},7,false,"Customer","Customer"));
    }

    [Fact] public void Published_master_write_requires_admin()
    {
        Assert.Throws<UnauthorizedAccessException>(()=>V180LocationOwnershipRules.EnsurePublishedMasterWrite(Actor(1,"visitor")));
        Assert.Throws<UnauthorizedAccessException>(()=>V180LocationOwnershipRules.EnsurePublishedMasterWrite(Actor(2,"leader")));
        V180LocationOwnershipRules.EnsurePublishedMasterWrite(Actor(3,"admin"));
    }
}
