using Xunit;
using FieldVisit.Application;
namespace FieldVisit.Application.Tests;
public sealed class V160Tests
{
    [Fact] public void PagedResult_Computes_TotalPages(){var r=new PagedResult<int>([1,2],1,50,101);Assert.Equal(3,r.TotalPages);}
    [Fact] public void TeamIds_FallsBack_To_PrimaryTeam(){var u=new CurrentUserDto(1,"E1","User",null,1,9,"T9",["leader"]);Assert.Equal(new[]{9},u.TeamIds);}
    [Fact] public void TeamIds_Uses_MultipleScopes(){var u=new CurrentUserDto(1,"E1","User",null,1,9,"T9",["leader"],[new(9,"T9",true),new(10,"T10",false)]);Assert.Equal(new[]{9,10},u.TeamIds);}
    [Fact] public void BusinessTime_Returns_Date(){Assert.True(BusinessTime.Today.Year>=2026);}
}
