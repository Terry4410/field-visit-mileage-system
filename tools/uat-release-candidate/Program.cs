using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldVisit.Infrastructure;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

var stage="INIT";
Process? app=null;
try
{
 var output=Environment.GetEnvironmentVariable("RELEASE_OUTPUT")!;
 var apiRoot=Environment.GetEnvironmentVariable("RELEASE_API_ROOT")!;
 var password=Environment.GetEnvironmentVariable("CLEAN_INSTALL_SQL_PASSWORD")!;
 var admin=new SqlConnectionStringBuilder {DataSource="localhost,1433",InitialCatalog="OptionCIsolated_FieldVisitV180CleanInstall",UserID="sa",Password=password,Encrypt=false,TrustServerCertificate=true,Pooling=false}.ConnectionString;
 stage="FINAL_CAPABILITIES";
 await using(var sql=new SqlConnection(admin))
 {
  await sql.OpenAsync();
  await using(var command=new SqlCommand(File.ReadAllText(Path.Combine(output,"60_final_functional_capabilities.sql")),sql){CommandTimeout=30}) await command.ExecuteNonQueryAsync();
  Console.WriteLine("UAT_RC=SOURCE_018_020_CAPABILITY_SQL_PASS");
  stage="SCHEMA_OBJECT_MANIFEST";
  var catalog=V180UatReleaseTrust.CatalogJson(sql);
  File.WriteAllText(Path.Combine(output,"schema-contract.json"),catalog);
  stage="READONLY_PREFLIGHT_TRANSACTION_BINDING";
  using(var transaction=sql.BeginTransaction(System.Data.IsolationLevel.Serializable)) {
   if(V180UatReleaseTrust.CatalogJson(sql,transaction)!=catalog)throw new InvalidOperationException("SCHEMA_TRANSACTION_MANIFEST_DRIFT");
   var priorMode=Environment.GetEnvironmentVariable("OPTIONC_EXECUTION_MODE");
   try {Environment.SetEnvironmentVariable("OPTIONC_EXECUTION_MODE","IsolatedSqlTest");V180OptionCExecutionPolicy.RequireTransactionTarget(sql,transaction);Environment.SetEnvironmentVariable("OPTIONC_EXECUTION_MODE","Uat");ExpectDenied(()=>V180OptionCExecutionPolicy.RequireTransactionTarget(sql,transaction));}
   finally {Environment.SetEnvironmentVariable("OPTIONC_EXECUTION_MODE",priorMode);}
   transaction.Rollback();
  }
  Console.WriteLine("UAT_RC=READONLY_PREFLIGHT_SAME_TRANSACTION_AND_WRONG_TARGET_DENIAL_PASS");
  stage="API_FRONTEND_SCHEMA_BINDING";
  var binding=new {candidateSourceSha=UatReleaseIdentity.SourceSha,apiSha256=V180UatReleaseTrust.ApiHash(apiRoot),frontendSha256=V180UatReleaseTrust.Hash(File.ReadAllBytes(Path.Combine(output,"frontend-release-candidate.zip"))),schemaSha256=V180UatReleaseTrust.Hash(Encoding.UTF8.GetBytes(catalog))};
  File.WriteAllText(Path.Combine(apiRoot,"release-contract.json"),JsonSerializer.Serialize(binding));
  File.WriteAllText(Path.Combine(output,"release-contract.json"),JsonSerializer.Serialize(binding));
 }
 Console.WriteLine("UAT_RC=FINAL_018_020_SOURCE_PREDICATES_CAPABILITIES_PASS");
 stage="TRUST_CONTRACT";
 var now=DateTimeOffset.UtcNow;
 var hash=new string('a',64);var key=RandomNumberGenerator.GetBytes(32);
 var facts=new V180UatReleaseTrust.Facts(UatReleaseIdentity.SourceSha,hash,hash,hash,hash,"runtime",V180UatReleaseTrust.Server,V180UatReleaseTrust.Database,5,true,true,V180UatReleaseTrust.Features);
 var attestation=new V180UatReleaseTrust.Attestation(facts.CandidateSourceSha,hash,hash,hash,hash,"runtime",facts.Server,facts.Database,facts.Features,now.AddMinutes(-1),now.AddHours(1));
 var payload=JsonSerializer.SerializeToUtf8Bytes(attestation,new JsonSerializerOptions(JsonSerializerDefaults.Web));
 var signature=Convert.ToHexString(HMACSHA256.HashData(key,payload));
 V180UatReleaseTrust.Validate(payload,signature,key,facts,now);
 var negatives=new [] { facts with {Server="unknown"},facts with {Database="production"},facts with {EngineEdition=3},facts with {EncryptedCertificateVerified=false},facts with {NonOwnerNoDdl=false},facts with {ApiSha256=new string('b',64)},facts with {FrontendSha256=new string('b',64)},facts with {SchemaSha256=new string('b',64)},facts with {PrincipalSidSha256=new string('b',64)},facts with {PrincipalName="dbo"},facts with {CandidateSourceSha=new string('b',40)},facts with {Features="all"}};
 foreach(var bad in negatives) ExpectDenied(()=>V180UatReleaseTrust.Validate(payload,signature,key,bad,now));
 ExpectDenied(()=>V180UatReleaseTrust.Validate(payload,signature,key,facts,now.AddHours(2)));
 ExpectDenied(()=>V180UatReleaseTrust.Validate(payload,new string('0',64),key,facts,now));
 ExpectDenied(()=>V180UatReleaseTrust.Validate(payload,signature,RandomNumberGenerator.GetBytes(32),facts,now));
 Console.WriteLine("UAT_RC=SIGNED_TRUST_CONTRACT_POSITIVE_AND_15_DENIALS_PASS");
 stage="ACCEPTED_FIXTURE_SETUP";
 var actors=await CleanInstallRoleProbe.SeedAsync(admin);
 var fixture=await CleanInstallBusinessProbe.SeedAsync(admin,actors);
 var extraActors=new List<CleanInstallRoleProbe.Actor>();
 await using(var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(admin).Options)) {
  foreach(var extraRole in new[]{"admin","leader"}) {
  var original=await db.Users.SingleAsync(x=>x.UserId==fixture.Admin.UserId);var nowSeed=DateTime.UtcNow;var since=new DateOnly(2020,1,1);
  var account="RC-P-"+Guid.NewGuid().ToString("N")[..10];
  var u=new User {OrganizationId=original.OrganizationId,TeamId=fixture.TeamId,EmployeeNo=account,DisplayName="Independent Publisher",IsActive=true,CreatedAt=nowSeed};db.Users.Add(u);await db.SaveChangesAsync();
  var employment=new Employment {OrganizationId=original.OrganizationId!.Value,PersonId=u.UserId,EmployeeNo=account,LegacyUserId=u.UserId,HireDate=since,SourceType="Synthetic"};db.Employments.Add(employment);await db.SaveChangesAsync();
  var role=await db.Roles.SingleAsync(x=>x.RoleCode==extraRole);
  db.UserIdentityProfiles.Add(new UserIdentityProfile {UserId=u.UserId,UserType=UserTypes.Internal,UserCode=account,IdentityProvider="Demo",EmploymentId=employment.EmploymentId,CreatedAt=nowSeed});
  db.UserEmploymentPeriods.Add(new UserEmploymentPeriod {UserId=u.UserId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=since,SourceType="Synthetic",CreatedAt=nowSeed});
  db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod {EmploymentId=employment.EmploymentId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=since,SourceType="Synthetic"});
  db.UserRoleAssignments.Add(new UserRoleAssignment {UserId=u.UserId,RoleId=role.RoleId,EffectiveFrom=since,CreatedAt=nowSeed});db.UserRoles.Add(new UserRole {UserId=u.UserId,RoleId=role.RoleId,AssignedAt=nowSeed});
  db.UserTeamAssignments.Add(new UserTeamAssignment {UserId=u.UserId,TeamId=fixture.TeamId,IsPrimary=true,EffectiveFrom=since,CreatedAt=nowSeed});
  db.UserTeamScopes.Add(new UserTeamScope {UserId=u.UserId,TeamId=fixture.TeamId,IsPrimary=true,IsActive=true,AssignedAt=nowSeed});
  db.TeamMemberships.Add(new TeamMembership {EmploymentId=employment.EmploymentId,TeamId=fixture.TeamId,IsPrimary=true,EffectiveFrom=since});
  if(extraRole=="leader") {var assignment=await db.TeamLeaderAssignments.SingleAsync(x=>x.TeamId==fixture.TeamId);var todaySeed=DateOnly.FromDateTime(nowSeed);db.TeamLeaderDelegations.Add(new TeamLeaderDelegation{TeamLeaderAssignmentId=assignment.TeamLeaderAssignmentId,DelegateEmploymentId=employment.EmploymentId,EffectiveFrom=todaySeed,EffectiveTo=todaySeed.AddDays(2),Reason="Synthetic independent Excel review",CreatedAt=nowSeed,CreatedByUserId=fixture.Admin.UserId});}
  await db.SaveChangesAsync();extraActors.Add(new(extraRole=="admin"?"publisher":"reviewLeader",u.UserId,account));
  }
 }
 var demo=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 var runtimePass="Aa7!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 await using(var sql=new SqlConnection(admin))
 {
  await sql.OpenAsync();
  await using var cmd=new SqlCommand($"""
    CREATE LOGIN [release_runtime] WITH PASSWORD=N'{runtimePass}', CHECK_POLICY=ON;
    CREATE USER [release_runtime] FOR LOGIN [release_runtime];
    GRANT CONNECT TO [release_runtime];
    DECLARE @permissions nvarchar(max)=N'';
    SELECT @permissions=@permissions+N'GRANT SELECT,INSERT,UPDATE,DELETE,VIEW DEFINITION ON OBJECT::dbo.'+QUOTENAME(name)+N' TO [release_runtime];' FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo');
    EXEC sys.sp_executesql @permissions;
    DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.SchemaVersions TO [release_runtime];
    DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.SchemaMigrationDataBaselines TO [release_runtime];
    """,sql){CommandTimeout=10}; await cmd.ExecuteNonQueryAsync();
 }
 var runtime=new SqlConnectionStringBuilder(admin){UserID="release_runtime",Password=runtimePass};
 var start=new ProcessStartInfo("dotnet"){WorkingDirectory=apiRoot,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.Combine(apiRoot,"FieldVisit.Api.dll"));
 foreach(var pair in new Dictionary<string,string> {
  ["ASPNETCORE_URLS"]="http://127.0.0.1:18376",["ASPNETCORE_ENVIRONMENT"]="Production",["DOTNET_ENVIRONMENT"]="Production",
  ["ConnectionStrings__DefaultConnection"]=runtime.ConnectionString,["Auth__Mode"]="Demo",["Auth__DemoPassword"]=demo,["Auth__JwtKey"]=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
  ["BackgroundJobs__WorkerEnabled"]="false",["BackgroundJobs__RecoverOnStartup"]="false",["OptionC__Candidate__Enabled"]="true",["OPTIONC_EXECUTION_MODE"]="IsolatedSqlTest",
  ["Providers__Route"]="Google",["GoogleMaps__ApiKey"]="SYNTHETIC_OFFLINE",["GoogleMaps__RoutesBaseUrl"]="http://127.0.0.1:1",["Logging__LogLevel__Default"]="Critical"}) start.Environment[pair.Key]=pair.Value;
 app=Process.Start(start) ?? throw new InvalidOperationException("API_START"); app.OutputDataReceived+=(_,_)=>{};app.ErrorDataReceived+=(_,_)=>{};app.BeginOutputReadLine();app.BeginErrorReadLine();
 stage="BROWSER_SQL";
 using(var client=new HttpClient()){bool ready=false;for(var i=0;i<80;i++){try{if((await client.GetAsync("http://127.0.0.1:18376/health/ready")).IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}if(!ready)throw new InvalidOperationException("API_READY");}
 stage="BROWSER_FIXTURE_LOOKUP";
 string teamCode;
 await using(var lookup=new SqlConnection(admin)) {
  await lookup.OpenAsync();
  await using var teamLookup=new SqlCommand("SELECT TeamCode FROM dbo.Teams WHERE TeamId=@id",lookup);teamLookup.Parameters.AddWithValue("@id",fixture.TeamId);teamCode=(string)(await teamLookup.ExecuteScalarAsync())!;
 }
 var image=Environment.GetEnvironmentVariable("RELEASE_BROWSER_IMAGE");
 var node=new ProcessStartInfo(string.IsNullOrEmpty(image)?(Environment.GetEnvironmentVariable("NODE_BINARY")??"node"):"docker"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
 if(!string.IsNullOrEmpty(image)) {
  var workspace=Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")!;var temporary=Environment.GetEnvironmentVariable("RUNNER_TEMP")!;
  foreach(var argument in new[]{"run","--rm","--memory","2g","--network","host","--ipc","host","-v",workspace+":"+workspace,"-v",temporary+":"+temporary,"-w",workspace,"-e","RELEASE_FIXTURE","-e","RC_SOURCE_ROOT","-e","RELEASE_OUTPUT","-e","GITHUB_SHA",image,"node"})node.ArgumentList.Add(argument);
 }
 node.ArgumentList.Add("--input-type=module");node.ArgumentList.Add("-e");
 node.ArgumentList.Add("import {pathToFileURL} from 'node:url';console.log('UAT_RC_BROWSER_BOOT');import(pathToFileURL(process.argv[1]).href).catch(e=>{const kind=['SyntaxError','TypeError','ReferenceError','Error'].includes(e.name)?e.name.toUpperCase():'SUPPRESSED';console.log('UAT_RC_BROWSER_FAIL_IMPORT_'+kind);process.exitCode=1;});");
 node.ArgumentList.Add(Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")??Directory.GetCurrentDirectory(),"tools/uat-release-candidate/browser.mjs"));
 node.Environment["RELEASE_FIXTURE"]=JsonSerializer.Serialize(new {actors=actors.Concat(extraActors).ToDictionary(x=>x.Role,x=>new{account=x.Account,id=x.UserId}),password=demo,teamCode,fixture.TeamId,fixture.OtherTeamId,fixture.LocationId});
 stage="BROWSER_PROCESS";
 using(var browser=Process.Start(node)??throw new InvalidOperationException("BROWSER_START"))
 {
  var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
  using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(5));
  try{await browser.WaitForExitAsync(deadline.Token);}catch{browser.Kill(true);throw;}
  var result=(await stdout).Trim();var privateError=await stderr;
  if(browser.ExitCode!=0){
   Console.WriteLine("UAT_RC_BROWSER_EXIT_"+browser.ExitCode);
   Console.WriteLine("UAT_RC_BROWSER_STREAM_LENGTHS_"+result.Length+"_"+privateError.Length);
   foreach(var kind in new[]{"SyntaxError","TypeError","ReferenceError","RangeError","FatalError"})if(privateError.Contains(kind,StringComparison.Ordinal))Console.WriteLine("UAT_RC_BROWSER_CLASS_"+kind.ToUpperInvariant());
   foreach(var known in new[]{"ERR_MODULE_NOT_FOUND","MODULE_NOT_FOUND","ERR_INVALID_ARG_TYPE","EADDRINUSE","ENOENT","EACCES","ECONNREFUSED","ERR_HTTP_HEADERS_SENT","TimeoutError","AssertionError","unhandled","locator","expect","toBe","test","browser","XServer","dependencies"}) if(privateError.Contains(known,StringComparison.Ordinal))Console.WriteLine("UAT_RC_BROWSER_RUNTIME_"+known);
   foreach(System.Text.RegularExpressions.Match marker in System.Text.RegularExpressions.Regex.Matches(result,@"UAT_RC_BROWSER_[A-Z0-9_]+"))Console.WriteLine(marker.Value);
   var code=System.Text.RegularExpressions.Regex.Match(result,@"UAT_RC_BROWSER_FAIL_[A-Z0-9_]+");
   Console.WriteLine(code.Success?code.Value:privateError.Contains("Cannot find module",StringComparison.Ordinal)?"UAT_RC_BROWSER_FAIL_BOOT_MODULE":privateError.Contains("Executable doesn't exist",StringComparison.Ordinal)?"UAT_RC_BROWSER_FAIL_BOOT_ENGINE":"UAT_RC_BROWSER_FAIL_BOOT_SUPPRESSED");
   throw new InvalidOperationException("BROWSER_FAILURE");
  }
  var evidence=JsonDocument.Parse(result.Split('\n').Last(line=>line.TrimStart().StartsWith('{')));File.WriteAllText(Path.Combine(output,"browser-evidence.json"),evidence.RootElement.GetRawText());
 }
 stage="SQL_POSTCONDITIONS";
 await using(var sql=new SqlConnection(admin))
 {
  await sql.OpenAsync();
  await using var cmd=new SqlCommand("""
   SELECT CASE WHEN
    EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents WHERE EventType=N'DirectLowRiskApplied')
    AND EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE Action=N'LocationLowRiskDirectApplied')
    AND EXISTS(SELECT 1 FROM dbo.ChangeRequests WHERE OperationCode=N'UpdatePublishedLocation' AND RiskCode=N'Low' AND Status=N'Applied' AND ReviewedByUserId IS NULL)
    AND EXISTS(SELECT 1 FROM dbo.ImportBatchItems WHERE Status=N'SubmittedForReview')
    AND (SELECT COUNT(*) FROM dbo.SchemaVersions)=3
    AND NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N'1.8.0-0%')
    THEN 1 ELSE 0 END;
   """,sql){CommandTimeout=10};
  if(Convert.ToInt32(await cmd.ExecuteScalarAsync())!=1)throw new InvalidOperationException("FEATURE_SQL_POSTCONDITION");
 }
 Console.WriteLine("UAT_RC=BROWSER_THREE_ROLE_LOWRISK_EXCEL_AND_SQL_POSTCONDITIONS_PASS");
 return 0;
}
catch(Exception e)
{
 Console.WriteLine("UAT_RC=FAIL_AT_"+stage);
 if(e is SqlException sql) Console.WriteLine("UAT_RC=SQL_NUMBER_"+Math.Abs(sql.Number));
 if(e is Microsoft.EntityFrameworkCore.DbUpdateException {InnerException:SqlException nested}) Console.WriteLine("UAT_RC=SQL_NUMBER_"+Math.Abs(nested.Number));
 if(e is InvalidOperationException && System.Text.RegularExpressions.Regex.IsMatch(e.Message,@"\A(?:SCHEMA|OPTIONC_UAT)_[A-Z0-9_]{1,60}\z")) Console.WriteLine("UAT_RC="+e.Message);
 Console.WriteLine("UAT_RC=ERROR_CLASS_"+(e is JsonException?"JSON":e is FileNotFoundException?"MISSING_FILE":e is KeyNotFoundException?"JSON_PROPERTY":e is InvalidOperationException?"CONTRACT":"SUPPRESSED"));
 return 1;
}
finally{if(app is not null&&!app.HasExited){app.Kill(true);app.WaitForExit(5000);}}
static void ExpectDenied(Action action){try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("TRUST_ACCEPTED_INVALID_PROOF");}
