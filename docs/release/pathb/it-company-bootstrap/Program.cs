using System.Data;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

static void Check(bool ok,string tag){if(!ok)throw new InvalidOperationException(tag);Console.WriteLine(tag+"=PASS");}
static async Task Refuse(Func<Task> action,string tag){try{await action();}catch(UnauthorizedAccessException){Console.WriteLine(tag+"=PASS");return;}throw new InvalidOperationException(tag);}
static async Task RefuseSql(Func<Task> action,int expected,string tag){try{await action();}catch(SqlException e) when(e.Number==expected || (expected==229 && e.Number==230)){Console.WriteLine(tag+"=PASS SQL_ERROR="+e.Number);return;}throw new InvalidOperationException(tag);}
static async Task<object?> Scalar(SqlConnection c,string sql){await using var q=new SqlCommand(sql,c);return await q.ExecuteScalarAsync();}
static async Task Exec(SqlConnection c,string sql){await using var q=new SqlCommand(sql,c);await q.ExecuteNonQueryAsync();}
var ct=CancellationToken.None;
var adminConn=Environment.GetEnvironmentVariable("FV_TEST_DBA")??throw new Exception("missing");
var runConn=Environment.GetEnvironmentVariable("FV_TEST_RUNTIME")??throw new Exception("missing");
var source=Environment.GetEnvironmentVariable("FV_BOOTSTRAP_SQL")??throw new Exception("missing");
var tenant=Guid.NewGuid();var oid=Guid.NewGuid();var today=BusinessTime.Today;
await using var dba=new SqlConnection(adminConn);await dba.OpenAsync();
Check(dba.Database=="FieldVisitCompanyPeopleTest" && dba.DataSource=="127.0.0.1,1433","DISPOSABLE_DBA_TARGET");
var server=Convert.ToString(await Scalar(dba,"SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName'))"))!;
async Task<(int user,int org)> Bootstrap(string target){
 await using var q=new SqlCommand(await File.ReadAllTextAsync(source),dba);
 foreach(var (key,value) in new (string,string)[]{("ServerName",server),("DatabaseName",target),("OrganizationCode","ITTEST"),("OrganizationName","Synthetic IT Test"),("EmployeeNo","ITFIRST001"),("DisplayName","Synthetic First Admin"),("Email","itfirst@example.invalid")})
  q.Parameters.Add("@"+key,SqlDbType.NVarChar,-1).Value=value;
 q.Parameters.Add("@TenantId",SqlDbType.UniqueIdentifier).Value=tenant;
 q.Parameters.Add("@ObjectId",SqlDbType.UniqueIdentifier).Value=oid;
 q.Parameters.Add("@HireDate",SqlDbType.Date).Value=today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
 await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())throw new Exception("bootstrap result missing");
 return(r.GetInt32(0),r.GetInt32(1));
}
await RefuseSql(async()=>{await Bootstrap("WrongDatabase");},58100,"BOOTSTRAP_WRONG_TARGET_DENIED");
Check(Convert.ToInt32(await Scalar(dba,"SELECT COUNT(*) FROM dbo.Users"))==0,"WRONG_TARGET_NO_USER_CREATED");
var (adminId,orgId)=await Bootstrap(dba.Database);
await RefuseSql(async()=>{await Bootstrap(dba.Database);},58104,"BOOTSTRAP_REPEAT_DENIED");
Check(Convert.ToInt32(await Scalar(dba,"SELECT COUNT(*) FROM dbo.Users"))==1,"BOOTSTRAP_EXACTLY_ONE_ADMIN_USER");
var opts=new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(runConn).Options;
await using var db=new AppDbContext(opts);
var repo=new UserRepository(db);var access=new V170AccessControl(db);
var auth=new AuthService(repo,new FakeToken(),access);
var logged=await auth.LoginEntraAsync(new EntraLoginIdentity(tenant,oid,null),false,ct);
var admin=logged.User;
Check(admin.UserId==adminId && admin.OrganizationId==orgId && admin.Roles.SequenceEqual(new[]{"admin"}) && admin.TeamIds.Count==0,"FIRST_ADMIN_REAL_REPOSITORY_ROLE_AND_ELIGIBILITY");
await Refuse(async()=>{await auth.LoginEntraAsync(new EntraLoginIdentity(Guid.NewGuid(),oid,"itfirst@example.invalid"),false,ct);},"WRONG_TENANT_EMAIL_FALLBACK_DENIED");
await Refuse(async()=>{await auth.LoginEntraAsync(new EntraLoginIdentity(tenant,Guid.NewGuid(),"itfirst@example.invalid"),false,ct);},"WRONG_OBJECT_EMAIL_FALLBACK_DENIED");
async Task VerifyHrEligibilityOnly(){
 await Exec(dba,$"UPDATE dbo.Users SET IsActive=0 WHERE UserId={adminId}");
 var existingPolicy=await auth.LoginEntraAsync(new EntraLoginIdentity(tenant,oid,null),false,ct);
 Check(existingPolicy.User.UserId==adminId,"INTERNAL_ACTIVE_HR_IS_SOURCE_OF_TRUTH_AS_DESIGNED");
 await Exec(dba,$"UPDATE dbo.Users SET IsActive=1 WHERE UserId={adminId}");
 await Exec(dba,$"UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Leave' WHERE EmploymentId=(SELECT EmploymentId FROM dbo.UserIdentityProfiles WHERE UserId={adminId});");
 await Refuse(async()=>{await auth.LoginEntraAsync(new EntraLoginIdentity(tenant,oid,null),false,ct);},"HR_LEAVE_LOGIN_DENIED");
 await Exec(dba,$"UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Terminated' WHERE EmploymentId=(SELECT EmploymentId FROM dbo.UserIdentityProfiles WHERE UserId={adminId});");
 await Refuse(async()=>{await auth.LoginEntraAsync(new EntraLoginIdentity(tenant,oid,null),false,ct);},"HR_TERMINATED_LOGIN_DENIED");
 Console.WriteLine("HR_ELIGIBILITY_ACTUAL_SQL2022_PASS");
}
if(Environment.GetEnvironmentVariable("FV_TEST_SCOPE")=="HR_ELIGIBILITY_ONLY"){
 await VerifyHrEligibilityOnly();
 return;
}
var writer=new V170PeopleAdminWriter(db);
var newId=await writer.CreateInternalUserAsync(admin,new CreateInternalUserRequest("ITSYN002","Synthetic Staff","staff@example.invalid",today.AddDays(-1),null,"Active",today.AddDays(-1)),ct);
var newProfile=await repo.GetProfileAsync(newId,ct);
Check(newProfile is not null && newProfile.Roles.Count==0 && newProfile.OrganizationId==orgId,"RESTRICTED_RUNTIME_HR_CREATE_NO_AUTO_ROLE");
await Exec(dba,$"INSERT dbo.Teams(OrganizationId,TeamCode,TeamName,IsActive,CreatedAt) VALUES({orgId},N'T1',N'Synthetic One',1,SYSUTCDATETIME()),({orgId},N'T2',N'Synthetic Two',1,SYSUTCDATETIME());");
var team1=Convert.ToInt32(await Scalar(dba,"SELECT TeamId FROM dbo.Teams WHERE TeamCode=N'T1'"));
var team2=Convert.ToInt32(await Scalar(dba,"SELECT TeamId FROM dbo.Teams WHERE TeamCode=N'T2'"));
var teams=new V180TeamMembershipCommandService(db,new FixedUser(admin));
var roles=new V180InternalRoleCommandService(db,new FixedUser(admin));
await teams.UpdateAsync(newId,new V180ReplaceTeamMembershipsRequest(new[]{new InternalTeamAssignmentInput(team1,true)},today),ct);
await roles.UpdateAsync(newId,new V180InternalRoleAccessRequest(new[]{"visitor"},today),ct);
await teams.UpdateAsync(newId,new V180ReplaceTeamMembershipsRequest(new[]{new InternalTeamAssignmentInput(team2,true)},today),ct);
await roles.UpdateAsync(newId,new V180InternalRoleAccessRequest(new[]{"leader"},today),ct);
var final=await repo.GetProfileAsync(newId,ct)??throw new Exception("profile missing");
Check(final.Roles.SequenceEqual(new[]{"leader"}) && final.TeamIds.SequenceEqual(new[]{team2}),"RESTRICTED_RUNTIME_TEAM_AND_ROLE_REPLACEMENT");
Check(await access.HasEffectiveRoleAsync(newId,"Leader",ct) && !await access.HasEffectiveRoleAsync(newId,"Visitor",ct),"REVOKED_VISITOR_ROLE_NOT_EFFECTIVE");
await Refuse(async()=>await new V180InternalRoleCommandService(db,new FixedUser(final)).UpdateAsync(adminId,new V180InternalRoleAccessRequest(new[]{"admin"},today),ct),"NONADMIN_ROLE_WRITE_DENIED");
await Refuse(async()=>await new V180TeamMembershipCommandService(db,new FixedUser(final)).UpdateAsync(adminId,new V180ReplaceTeamMembershipsRequest(new[]{new InternalTeamAssignmentInput(team1,true)},today),ct),"NONADMIN_TEAM_WRITE_DENIED");
await using var runtime=new SqlConnection(runConn);await runtime.OpenAsync();
Check(Convert.ToInt32(await Scalar(runtime,"SELECT IS_ROLEMEMBER(N'db_owner')+IS_ROLEMEMBER(N'db_datareader')+IS_ROLEMEMBER(N'db_datawriter')"))==0,"NO_BROAD_DB_ROLE");
await RefuseSql(async()=>{await Exec(runtime,"SELECT NewValues FROM dbo.AuditLogs");},229,"AUDIT_PAYLOAD_READ_DENIED");
await RefuseSql(async()=>{await Exec(runtime,"DELETE FROM dbo.AuditLogs WHERE 1=0");},229,"AUDIT_DELETE_DENIED");
await RefuseSql(async()=>{await Exec(runtime,"UPDATE dbo.SchemaVersions SET VersionNumber=VersionNumber WHERE 1=0");},229,"SCHEMA_LEDGER_WRITE_DENIED");
await RefuseSql(async()=>{await Exec(runtime,"UPDATE dbo.UserIdentityProfiles SET EntraObjectId=NULL WHERE 1=0");},229,"UNREVIEWED_IDENTITY_REBIND_DENIED");
await RefuseSql(async()=>{await Exec(runtime,"DELETE FROM dbo.Users WHERE 1=0");},229,"PERSON_DELETE_DENIED");
await VerifyHrEligibilityOnly();
Console.WriteLine("BOOTSTRAP_PEOPLE_SUBSET_ACTUAL_SQL2022_PASS");
sealed class FakeToken:ITokenService{public (string Token,DateTime ExpiresAtUtc) Create(CurrentUserDto user)=>("test-only-no-real-token",DateTime.UtcNow.AddMinutes(1));}
sealed class FixedUser(CurrentUserDto u):ICurrentUserService{public CurrentUserDto GetRequired()=>u;}
