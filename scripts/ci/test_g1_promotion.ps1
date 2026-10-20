param([ValidateSet('Reference','Test')][string]$Mode='Reference')
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$case='START';$connections=[Collections.Generic.List[object]]::new();$dbs=[Collections.Generic.List[string]]::new();$results=[Collections.Generic.List[object]]::new()
function Assert([bool]$ok){if(-not $ok){throw 'ASSERT_STOP'}}
function Exec($c,[string]$sql){$cmd=$c.CreateCommand();try{$cmd.CommandTimeout=120;$cmd.CommandText=$sql;[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}
function Scalar($c,[string]$sql){$cmd=$c.CreateCommand();try{$cmd.CommandTimeout=30;$cmd.CommandText=$sql;return [string]$cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function Code($e){while($null -ne $e){if($e -is [Microsoft.Data.SqlClient.SqlException]){return [int]$e.Number};$e=$e.InnerException};return -99999}
function Refuse($c,[string]$sql,[int[]]$codes){try{Exec $c $sql}catch{$actual=Code $_.Exception;if($actual -notin $codes){$tag=[regex]::Match($_.Exception.Message,'\bG1_[A-Z0-9_]+\b').Value;if(-not $tag){$tag='SUPPRESSED'};$safe=if($actual -in @(52001,52002,52003,52004,52005,52010,52011,52012,52013,52014,52015,52016,52020,52090,52990)){$actual}else{'SUPPRESSED'};Write-Output "RefusalDiagnostic=STOP; Guard=$tag; ProviderCode=$safe;";if($actual -eq 52013){$now=Scalar $c $catalog;$expected=$reference.initial;if($now -match '^[A-Fa-f0-9]{64}$' -and $expected -match '^[A-Fa-f0-9]{64}$'){Write-Output "ContractDiagnostic=STOP; Actual=$now; Expected=$expected;"}}};Assert ($actual -in $codes);return};throw 'EXPECTED_STOP_MISSING'}
function Pass([string]$id){$results.Add(@{case=$id;result='PASS'});Write-Output "PromotionCase=$id; Status=PASS;"}
try{
 $case='Client';Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
 Assert ($env:GITHUB_ACTIONS -eq 'true' -and $b.DataSource -in @('localhost,1433','127.0.0.1,1433') -and $b.InitialCatalog -eq 'master' -and $b.UserID -eq 'sa' -and -not $b.IntegratedSecurity)
 $b['Pooling']=$false;$b['Connect Timeout']=5;$b['ConnectRetryCount']=0
 $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString);$admin.Open()
 $repo=(Resolve-Path "$PSScriptRoot/../..").Path;$p=Join-Path $repo 'docs/release/g1-execution/promotion';$draft=Join-Path $repo 'docs/release/optionc-canonical-sql-draft/candidate'
 $old=Get-Content (Join-Path $PSScriptRoot 'run_full_candidate_migration.ps1') -Raw
 $match=[regex]::Matches($old,"(?s)\`$boot=@'\r?\n(.*?)\r?\n'@");Assert ($match.Count -eq 1);$boot=$match[0].Groups[1].Value
 $catalog=[IO.File]::ReadAllText((Join-Path $p 'CatalogContract.sql'));$history=[IO.File]::ReadAllText((Join-Path $p 'HistoryContract.sql'));$ledger=[IO.File]::ReadAllText((Join-Path $p 'LedgerContract.sql'))
 function OpenDb([string]$name,[bool]$optIn=$true){Assert ($name -match '^(OptionCIsolated_|G1Denied_)[0-9a-f]{32}$');$x=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($b.ConnectionString);$x['Initial Catalog']=$name;$c=[Microsoft.Data.SqlClient.SqlConnection]::new($x.ConnectionString);$c.Open();$connections.Add($c);if($optIn){Exec $c "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;"};return $c}
 function Fixture([string]$prefix='OptionCIsolated_',[bool]$optIn=$true){$name=$prefix+[guid]::NewGuid().ToString('N');$dbs.Add($name);Exec $admin "CREATE DATABASE [$name]";$c=OpenDb $name $optIn;Exec $c $boot;return $c}
 function DraftSql([int]$n,[string]$phase){return [IO.File]::ReadAllText((Join-Path $draft ('1800_{0:000}/{1}.sql' -f $n,$phase)))}
 function Pristine($c){Assert ((Scalar $c $catalog) -eq $reference.initial);Assert ((Scalar $c "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber<>N'1.8.0-010'") -eq '0');Assert ((Scalar $c "SELECT COUNT(*) FROM sys.tables WHERE name=N'FieldVisitMigrationLedger'") -eq '0');Assert ((Scalar $c "SELECT APPLOCK_TEST(N'public',N'FieldVisit.SchemaMigration',N'Exclusive',N'Session')") -eq '1')}
 if($Mode -eq 'Reference'){
  $case='ReferenceBootstrap';$c=Fixture;Assert ((Scalar $c "SELECT N'G1_SCALAR_CONTROL'") -eq 'G1_SCALAR_CONTROL');$h=Scalar $c $history;$initial=Scalar $c $catalog;Assert ($h -match '^[A-Fa-f0-9]{64}$' -and $initial -match '^[A-Fa-f0-9]{64}$');Exec $c $ledger;$emptyLedger=Scalar $c $catalog;Assert ($emptyLedger -match '^[A-Fa-f0-9]{64}$');$steps=@()
  foreach($n in @(11,12,14,15,16,17,18,19,20)){
   $case="Reference$n";Exec $c (DraftSql $n 'Preflight');Exec $c (DraftSql $n 'Up');if($n -eq 14){Exec $c (DraftSql $n 'Backfill')};Exec $c (DraftSql $n 'Verify')
   Assert ((Scalar $c $history) -eq $h);$ch=Scalar $c $catalog;Assert ($ch -match '^[A-Fa-f0-9]{64}$');$steps+=@{id=$n;catalog=$ch};Pass $case
  }
  @{contract='g1-scoped-promotion-reference-v1';originRc='f29affb3299828f45b1f131321779e8bea2f86a8';fixture='SYNTHETIC_SCOPED_MIGRATION_CONTRACT_NOT_UAT_OR_150_BASELINE';initial=$initial;emptyLedger=$emptyLedger;steps=$steps} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $repo 'artifacts/observed-reference.json')
 }else{
  $reference=Get-Content (Join-Path $p 'reference.json') -Raw | ConvertFrom-Json
  $promotion=[IO.File]::ReadAllText((Join-Path $p 'Promotion.sql'));$verify=[IO.File]::ReadAllText((Join-Path $p 'Verify.sql'))
  function BadInitial([string]$id,[string]$change,[int[]]$codes){$script:case=$id;$d=Fixture;if($change){Exec $d $change};$h=Scalar $d $history;Refuse $d $promotion $codes;Assert ((Scalar $d $history) -eq $h);Assert ((Scalar $d "SELECT COUNT(*) FROM sys.tables WHERE name=N'FieldVisitMigrationLedger'") -eq '0');Pass $id}
  function BadFinal([string]$id,[string]$change){$script:case=$id;$d=Fixture;Exec $d $promotion;Exec $d $change;Refuse $d $verify @(52014);Refuse $d $promotion @(52014);Pass $id}
  $case='WrongTarget_NoOfflineOptIn';$d=Fixture 'OptionCIsolated_' $false;Refuse $d $promotion @(52001);Pass $case
  $case='WrongDatabase_WithOfflineOptIn';$d=Fixture 'G1Denied_';Refuse $d $promotion @(52001);Pass $case
  BadInitial 'MissingVersionTable' 'DROP TABLE dbo.SchemaVersions;' @(52010)
  BadInitial 'NullEmptyPredecessor' 'DELETE dbo.SchemaVersions;' @(52011)
  BadInitial 'Missing010Predecessor' "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-009';" @(52011)
  BadInitial 'UnknownPredecessor' "INSERT dbo.SchemaVersions VALUES(N'1.8.0-999',N'Synthetic',DATEADD(day,1,SYSUTCDATETIME()),N'test');" @(52011)
  BadInitial 'Reserved013Refused' "INSERT dbo.SchemaVersions VALUES(N'1.8.0-013',N'Synthetic',DATEADD(day,1,SYSUTCDATETIME()),N'test');" @(52011)
  BadInitial 'ObjectsWithoutMarker' 'CREATE TABLE dbo.LocationMergeDetails(Id int NOT NULL);' @(52013)
  BadInitial 'ChangedImportContract' "ALTER TABLE dbo.ImportBatches DROP CONSTRAINT CK_ImportBatches_Status; ALTER TABLE dbo.ImportBatches ADD CONSTRAINT CK_ImportBatches_Status CHECK(Status IN(N'Previewed'));" @(52013)
  BadInitial 'AmbiguousBackfillRollback' "UPDATE dbo.Locations SET LocationName=N'' WHERE LocationId=10;" @(52090)
  foreach($n in @(11,12,14,15,16,17,18,19,20)){
   $case="Rollback_$n";$d=Fixture;$h=Scalar $d $history;$fault=$promotion.Replace(('-- G1_STEP_{0:000}_END' -f $n),("THROW 52990,N'G1_SYNTHETIC_FAILURE',1;"))
   Assert ($fault -ne $promotion);Refuse $d $fault @(52990);Pristine $d;Assert ((Scalar $d $history) -eq $h);Pass $case
  }
  $case='014FailureBeforeBackfillNoCompletion';$d=Fixture;$fault=$promotion.Replace('-- G1_BACKFILL_BEGIN',"THROW 52990,N'G1_SYNTHETIC_FAILURE',1;");Assert ($fault -ne $promotion);Refuse $d $fault @(52990);Pristine $d;Pass $case
  foreach($resource in @('FieldVisit.SchemaMigration','FieldVisit.OptionC014.Backfill')){
   $case=if($resource -eq 'FieldVisit.SchemaMigration'){'MigrationLockDeniedBeforeDDL'}else{'014BackfillLockDeniedAtomicRollback'}
   $d=Fixture;$peer=OpenDb $d.Database
   Exec $d "DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'$resource',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;IF @r<0 THROW 52991,N'TEST_LOCK_STOP',1;"
   try{Refuse $peer $promotion @(52003)}finally{Exec $d "EXEC sys.sp_releaseapplock @Resource=N'$resource',@LockOwner=N'Session';"}
   Pristine $peer;Pass $case
  }
  foreach($kind in @('Ddl','Metadata')){
   $case="Permission${kind}Denied";$d=Fixture
   Exec $d 'CREATE USER G1Restricted WITHOUT LOGIN; GRANT SELECT ON SCHEMA::dbo TO G1Restricted; GRANT VIEW DEFINITION TO G1Restricted;'
   if($kind -eq 'Ddl'){Exec $d 'DENY CREATE TABLE TO G1Restricted;'}else{Exec $d 'DENY VIEW DEFINITION TO G1Restricted;'}
   Exec $d "EXECUTE AS USER=N'G1Restricted';";try{Refuse $d $promotion @(52005)}finally{Exec $d 'REVERT;'};Pristine $d;Pass $case
  }
  $case='PositiveAtomicChain';$c=Fixture;$before=Scalar $c $history;Exec $c $promotion;Exec $c $verify;Assert ((Scalar $c $history) -eq $before);Pass $case
  $case='ReplayVerifiedAndRefused';Refuse $c $promotion @(52020);Exec $c $verify;Pass $case
  $case='UnknownCommitFreshVerificationNoRetry';$peer=OpenDb $c.Database;Exec $peer $verify;Assert ((Scalar $peer "SELECT COUNT(*) FROM dbo.FieldVisitMigrationLedger") -eq '9');Pass $case
  BadFinal '020MissingMarkerNoRepair' "DELETE dbo.FieldVisitMigrationLedger WHERE VersionNumber=N'1.8.0-020'; DELETE dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-020';"
  BadFinal 'ChecksumDriftRefused' "UPDATE dbo.FieldVisitMigrationLedger SET PayloadSha256=0x00 WHERE VersionNumber=N'1.8.0-020';"
  BadFinal 'PackageDriftRefused' "UPDATE dbo.FieldVisitMigrationLedger SET PackageId=0x00 WHERE VersionNumber=N'1.8.0-015';"
  BadFinal 'MarkerContentDriftRefused' "UPDATE dbo.SchemaVersions SET Description=N'Synthetic drift' WHERE VersionNumber=N'1.8.0-016';"
  BadFinal 'EmptyLedgerRefused' 'DELETE dbo.FieldVisitMigrationLedger;'
  BadFinal 'IndexDriftRefused' 'DROP INDEX IX_B3_ChangeRequests_Requester_SubmittedAt ON dbo.ChangeRequests;'
  BadFinal 'ConstraintDriftRefused' 'ALTER TABLE dbo.ChangeRequests NOCHECK CONSTRAINT CK_B3_ChangeRequests_KnownStatus;'
  BadFinal 'ColumnDriftRefused' 'ALTER TABLE dbo.LocationMergeDetails DROP CONSTRAINT CK_OptionC_MergeDetails_Teams; ALTER TABLE dbo.LocationMergeDetails DROP COLUMN SourceTeamId;'
  BadFinal 'TriggerDriftRefused' 'CREATE TRIGGER dbo.G1SyntheticTrigger ON dbo.ChangeRequests AFTER UPDATE AS BEGIN SET NOCOUNT ON; END;'
  $case='ApplicationPrincipalCannotMutateRegistryOrDdl';$d=Fixture;Exec $d $promotion
  Exec $d 'CREATE USER G1Application WITHOUT LOGIN; GRANT SELECT ON dbo.ChangeRequests TO G1Application; DENY INSERT,UPDATE,DELETE ON dbo.FieldVisitMigrationLedger TO G1Application; DENY INSERT,UPDATE,DELETE ON dbo.SchemaVersions TO G1Application; DENY CREATE TABLE TO G1Application; DENY ALTER ON SCHEMA::dbo TO G1Application;'
  Exec $d "EXECUTE AS USER=N'G1Application';";try{Refuse $d 'DELETE dbo.FieldVisitMigrationLedger;' @(229);Refuse $d "UPDATE dbo.SchemaVersions SET Description=N'Synthetic unauthorized';" @(229);Refuse $d 'CREATE TABLE dbo.G1Unauthorized(Id int);' @(262,2760,229)}finally{Exec $d 'REVERT;'};Exec $d $verify;Pass $case
  $case='AmbientTransactionRefused';$d=Fixture;Exec $d 'BEGIN TRANSACTION;';try{Refuse $d $promotion @(52002)}finally{Exec $d 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'};Pristine $d;Pass $case
  $case='WriteQuiescenceBlocksConcurrentChange';$d=Fixture;$name=$d.Database;$h=Scalar $d $history;$spid=Scalar $d 'SELECT @@SPID';$peer=OpenDb $name
  $cmd=$d.CreateCommand();$cmd.CommandTimeout=120;$cmd.CommandText=$promotion.Replace('-- G1_STEP_014_END',"WAITFOR DELAY '00:00:03';")
  try{
   $task=$cmd.ExecuteNonQueryAsync();$waiting=$false
   for($i=0;$i -lt 200;$i++){if((Scalar $peer "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id=$spid AND wait_type=N'WAITFOR'") -eq '1'){$waiting=$true;break};if($task.IsCompleted){break};Start-Sleep -Milliseconds 20}
   Assert $waiting;Refuse $peer "SET LOCK_TIMEOUT 0; UPDATE dbo.Locations SET MasterNote=N'Synthetic concurrent' WHERE LocationId=10;" @(1222)
   [void]$task.GetAwaiter().GetResult();Exec $peer $verify;Assert ((Scalar $peer $history) -eq $h);Assert ((Scalar $peer "SELECT COALESCE(MasterNote,N'') FROM dbo.Locations WHERE LocationId=10") -eq '');Pass $case
  }finally{$cmd.Dispose()}
  $case='TimeoutDisposeFreshRollbackNoRetry';$d=Fixture;$name=$d.Database;$cmd=$d.CreateCommand();$cmd.CommandTimeout=1;$cmd.CommandText=$promotion.Replace('-- G1_STEP_014_END',"WAITFOR DELAY '00:00:10';")
  try{$failed=$false;try{[void]$cmd.ExecuteNonQuery()}catch{Assert ((Code $_.Exception) -eq -2);$failed=$true};Assert $failed}finally{$cmd.Dispose();$d.Dispose()}
  $peer=OpenDb $name;Pristine $peer;Pass $case
  @{contract='g1-promotion-real-sql-v1';sourceSha=$env:GITHUB_SHA;rcSource='f29affb3299828f45b1f131321779e8bea2f86a8';cases=$results;uatExecuted=$false;automaticRetry=$false} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $repo 'artifacts/promotion-cases.json')
 }
}catch{Write-Output "PromotionCase=$case; Status=STOP; Details=SUPPRESSED;";$line=$_.InvocationInfo.ScriptLineNumber;$code=Code $_.Exception;Write-Output "HarnessDiagnostic=STOP; Line=$line; ProviderCode=$code;";exit 1}
finally{foreach($c in $connections){$c.Dispose()};if($null -ne $admin){foreach($name in $dbs){try{Exec $admin "ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name];"}catch{Write-Output 'Cleanup=STOP; Details=SUPPRESSED;'}};$admin.Dispose()}}
