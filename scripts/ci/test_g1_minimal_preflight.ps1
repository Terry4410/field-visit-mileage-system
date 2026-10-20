# Disposable SQL2022 only. Accepted collector/SQL are copied, never edited in place.
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$case='Client';$admin=$null;$db=$null;$temp=$null;$setup=$null;$script:failed=$false
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$results=[Collections.Generic.List[object]]::new()
function Assert([bool]$ok){if(-not $ok){throw 'G1_TEST_STOP'}}
function Exec($c,[string]$sql){$q=$c.CreateCommand();try{$q.CommandTimeout=90;$q.CommandText=$sql;[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}}
function Rows($c,[string]$sql,[string]$requirements='',[string]$reference=''){
 $q=$c.CreateCommand();$r=$null
 try{
  $q.CommandTimeout=30;$q.CommandText=$sql
  if($requirements){$p=$q.Parameters.Add('@requirements',[Data.SqlDbType]::NVarChar,-1);$p.Value=$requirements}
  if($reference){$p=$q.Parameters.Add('@reference',[Data.SqlDbType]::NVarChar,-1);$p.Value=$reference}
  $r=$q.ExecuteReader();Assert ($r.FieldCount -eq 2 -and $r.GetName(0) -ceq 'code' -and $r.GetName(1) -ceq 'status')
  $rows=@();while($r.Read()){$rows+=@{code=$r.GetString(0);status=$r.GetString(1)}};Assert (-not $r.NextResult());return $rows
 }finally{if($null -ne $r){$r.Dispose()};$q.Dispose()}
}
function Status($rows,[string]$code){$m=@($rows|Where-Object{$_.code -ceq $code});Assert ($m.Count -eq 1);return $m[0].status}
function Pass([string]$id){$results.Add(@{case=$id;result='PASS'});Write-Output "PreflightCase=$id; Status=PASS;"}
function NewConnection{
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($script:base.ConnectionString)
 $b['Initial Catalog']=$script:db;$b['TrustServerCertificate']=$false;$b['Encrypt']=$false
 return [Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
}
function Rehash([string]$directory){
 $m=Get-Content (Join-Path $directory 'manifest.json') -Raw|ConvertFrom-Json
 foreach($p in $m.files.PSObject.Properties){$p.Value=(Get-FileHash (Join-Path $directory $p.Name) -Algorithm SHA256).Hash.ToLowerInvariant()}
 $m|ConvertTo-Json -Depth 15|Set-Content (Join-Path $directory 'manifest.json') -Encoding utf8
 return (Get-FileHash (Join-Path $directory 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
}
function TestCopy([string]$id,[string]$replaceCatalog=''){
 Assert ($id -cmatch '^[A-Za-z0-9_]+$')
 $d=Join-Path $script:temp $id;New-Item -ItemType Directory $d|Out-Null
 Copy-Item (Join-Path $script:package '*') $d
 $collector=[IO.File]::ReadAllText((Join-Path $d 'collect.ps1'))
 # EXACTLY labelled test-only substitutions. No path can connect beyond loopback.
 $collector=$collector.Replace("tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433",'localhost,1433').Replace('db-fieldvisit-uat',$script:db).Replace("`$b['Encrypt']=`$true","`$b['Encrypt']=`$false")
 $needle='   $reader=$cmd.ExecuteReader()'
 Assert ($collector.Contains($needle))
 $trace=Join-Path $d 'trace.txt'
 $collector=$collector.Replace($needle,"   [IO.File]::AppendAllText('$trace',`$file+[Environment]::NewLine)`n"+$needle)
 [IO.File]::WriteAllText((Join-Path $d 'collect.ps1'),$collector)
 $identity=[IO.File]::ReadAllText((Join-Path $d '01-target.sql'))
 # Table/query behavior remains SQL2022; Azure identity comparison becomes a
 # fixture-local literal comparison. This is NOT proof of Azure target identity.
 $identity=$identity.Replace('db-fieldvisit-uat',$script:db).Replace("CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))", "N'sql-fieldvisit-jpe-uat'").Replace("CONVERT(int,SERVERPROPERTY('EngineEdition'))=5","CONVERT(int,SERVERPROPERTY('EngineEdition'))=3")
 [IO.File]::WriteAllText((Join-Path $d '01-target.sql'),$identity)
 if($replaceCatalog){[IO.File]::WriteAllText((Join-Path $d '03-catalog.sql'),$replaceCatalog)}
 $pin=Rehash $d
 return @{directory=$d;trace=$trace;pin=$pin;evidence=(Join-Path $d 'evidence.json')}
}
function RunCopy($copy){
 $c=NewConnection;$global:LASTEXITCODE=0
 try{
  $output=@(& (Join-Path $copy.directory 'collect.ps1') -ExecuteAuthorized -ApplicationConnection $c -ExpectedManifestSha256 $copy.pin -EvidenceFile $copy.evidence 2>&1)
  $exit=$LASTEXITCODE
  $text=$output -join "`n"
  Assert (-not $text.Contains('G1_SYNTHETIC_PRIVATE_CANARY'))
  Assert (-not $text.Contains($script:base.Password))
  Assert (-not $text.Contains('Server=') -and -not $text.Contains('System.Management.Automation'))
  return @{exit=$exit;output=$text;trace=if(Test-Path $copy.trace){@(Get-Content $copy.trace)}else{@()}}
 }finally{$c.Dispose()}
}
try{
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
 Assert ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_REPOSITORY -eq 'Terry4410/field-visit-mileage-system')
 $base=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
 Assert ($base.DataSource -ceq 'localhost,1433' -and $base.InitialCatalog -ceq 'master' -and $base.UserID -ceq 'sa')
 $base['Pooling']=$false;$base['ConnectRetryCount']=0
 $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($base.ConnectionString);$admin.Open()
 $repo=(Resolve-Path "$PSScriptRoot/../..").Path
 $package=Join-Path $repo 'docs/release/g1-minimal-preflight'
 Assert ((Get-FileHash (Join-Path $package 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -eq '63c81e16359aa82112d4f19f6e6eb3cdcaeece136e93196b11f12731cda3947b')
 $temp=Join-Path $env:RUNNER_TEMP ('g1-preflight-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $temp|Out-Null
 $db='OptionCIsolated_'+[guid]::NewGuid().ToString('N');Assert ($db -cmatch '^OptionCIsolated_[0-9a-f]{32}$')
 Exec $admin "CREATE DATABASE [$db]"
 $setup=NewConnection;$setup.Open()
 $old=[IO.File]::ReadAllText((Join-Path $repo 'scripts/ci/run_full_candidate_migration.ps1'))
 $matches=[regex]::Matches($old,"(?s)\`$boot=@'\r?\n(.*?)\r?\n'@");Assert ($matches.Count -eq 1);Exec $setup $matches[0].Groups[1].Value
 $requirements=[IO.File]::ReadAllText((Join-Path $package 'requirements.json'));$reference=[IO.File]::ReadAllText((Join-Path $package 'necessary-reference.json'));$catalog=[IO.File]::ReadAllText((Join-Path $package '03-catalog.sql'))
 $case='NoSqlCmdExpansion';foreach($f in @('01-target.sql','02-marker.sql','03-catalog.sql','04-permissions.sql')){Assert (-not ([IO.File]::ReadAllText((Join-Path $package $f)) -match '\$\('))};Pass $case
 $case='UnmodifiedTargetSqlRejectsDisposable';$rows=Rows $setup ([IO.File]::ReadAllText((Join-Path $package '01-target.sql')));Assert ((Status $rows 'TARGET') -ceq 'FAIL');Pass $case
 $case='UnmodifiedCollectorRejectsLoopbackBeforeOpen';$c=NewConnection;$global:LASTEXITCODE=0
 $output=@(& (Join-Path $package 'collect.ps1') -ExecuteAuthorized -ApplicationConnection $c -ExpectedManifestSha256 '63c81e16359aa82112d4f19f6e6eb3cdcaeece136e93196b11f12731cda3947b' -EvidenceFile (Join-Path $temp 'forbidden.json') 2>&1)
 Assert ($LASTEXITCODE -ne 0 -and $c.State -eq [Data.ConnectionState]::Closed -and -not (Test-Path (Join-Path $temp 'forbidden.json')));$c.Dispose();Pass $case
 $case='Collector010FindingsStillCollectPermissions';$copy=TestCopy $case;$run=RunCopy $copy
 Assert ($run.exit -eq 0 -and ($run.trace -join ',') -ceq '01-target.sql,02-marker.sql,03-catalog.sql,04-permissions.sql')
 $e=Get-Content $copy.evidence -Raw|ConvertFrom-Json;Assert ($e.groups.Count -eq 4)
 Assert ((Status $e.groups[2] 'REQUIRED_COLUMNS') -ceq 'FAIL' -and (Status $e.groups[2] 'COMPLETE_FACET_EQUIVALENCE') -ceq 'UNKNOWN')
 Assert ((Status $e.groups[3] 'API_PRINCIPAL_BINDING') -ceq 'UNKNOWN');Pass $case
 $case='MarkerStopBeforeCatalog';Exec $setup "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-009'";$copy=TestCopy $case;$run=RunCopy $copy
 Assert ($run.exit -eq 0 -and ($run.trace -join ',') -ceq '01-target.sql,02-marker.sql');Exec $setup "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-010'";Pass $case
 $case='MissingVersionObjectStops';Exec $setup 'EXEC sys.sp_rename N''dbo.SchemaVersions'',N''G1HeldVersions'';';$copy=TestCopy $case;$run=RunCopy $copy
 Assert ($run.exit -eq 0 -and ($run.trace -join ',') -ceq '01-target.sql');Exec $setup 'EXEC sys.sp_rename N''dbo.G1HeldVersions'',N''SchemaVersions'';';Pass $case
 foreach($fault in @(
  @{id='MalformedResultStops';sql="SELECT N'G1_UNREVIEWED' AS code,N'PASS' AS status";classification=''},
  @{id='UnexpectedResultSetStops';sql="SELECT N'COMPLETE_FACET_EQUIVALENCE' AS code,N'UNKNOWN' AS status; SELECT N'PRIVATE' AS extra";classification=''},
  @{id='SqlSyntaxSanitized';sql='SELECT FROM;';classification='SqlSyntaxOrCompatibility'},
  @{id='UnknownProviderSuppressed';sql="THROW 55555,N'G1_SYNTHETIC_PRIVATE_CANARY',1;";classification='UnclassifiedSqlFailure'},
  @{id='TimeoutStops';sql="WAITFOR DELAY '00:00:03'; SELECT N'PRIVATE' AS code,N'PASS' AS status";classification='TimeoutOrTransient'}
 )){
  $case=$fault.id;$copy=TestCopy $case $fault.sql
  if($case -ceq 'TimeoutStops'){
   $f=Join-Path $copy.directory 'collect.ps1';$t=[IO.File]::ReadAllText($f).Replace('$cmd.CommandTimeout=30','$cmd.CommandTimeout=1');[IO.File]::WriteAllText($f,$t);$copy.pin=Rehash $copy.directory
  }
  $run=RunCopy $copy;Assert ($run.exit -ne 0 -and -not (Test-Path $copy.evidence) -and ($run.trace -join ',') -ceq '01-target.sql,02-marker.sql,03-catalog.sql')
  if($fault.classification){Assert ($run.output.Contains('Classification='+$fault.classification))}
  if($case -ceq 'UnknownProviderSuppressed'){Assert ($run.output.Contains('ProviderCode=Suppressed'))}
  Pass $case
 }
 $case='HashMismatchBeforeSql';$copy=TestCopy $case;$copy.pin='0'*64;$run=RunCopy $copy;Assert ($run.exit -ne 0 -and $run.trace.Count -eq 0);Pass $case
 # Only fixture setup executes original candidate phases, not the accepted suite.
 $draft=Join-Path $repo 'docs/release/optionc-canonical-sql-draft/candidate'
 foreach($n in @(11,12,14,15,16,17,18,19,20)){
  $dir=Join-Path $draft ('1800_{0:000}' -f $n)
  Exec $setup ([IO.File]::ReadAllText((Join-Path $dir 'Preflight.sql')))
  Exec $setup ([IO.File]::ReadAllText((Join-Path $dir 'Up.sql')))
  if($n -eq 14){Exec $setup ([IO.File]::ReadAllText((Join-Path $dir 'Backfill.sql')))}
 }
 $case='RealReferenceParameterFacetsAndKeys';$rows=Rows $setup $catalog $requirements $reference
 foreach($code in @('REQUIRED_COLUMNS','SCOPED_COLUMN_FACETS','SCOPED_FK_MAPPINGS')){Assert ((Status $rows $code) -ceq 'PASS')}
 Assert ((Status $rows 'SCOPED_INDEX_KEYS') -in @('PASS','UNKNOWN') -and (Status $rows 'SCOPED_CHECK_DEFINITIONS') -in @('PASS','UNKNOWN'))
 Assert ((Status $rows 'COMPLETE_FACET_EQUIVALENCE') -ceq 'UNKNOWN');Pass $case
 $case='DisabledConstraintFails';Exec $setup 'BEGIN TRANSACTION;ALTER TABLE dbo.ChangeRequests NOCHECK CONSTRAINT CK_B3_ChangeRequests_KnownStatus;'
 try{$rows=Rows $setup $catalog $requirements $reference;Assert ((Status $rows 'SCOPED_CHECK_DEFINITIONS') -ceq 'FAIL' -and (Status $rows 'TRUSTED_RELATIONSHIPS') -ceq 'FAIL')}finally{Exec $setup 'ROLLBACK TRANSACTION;'};Pass $case
 $case='MissingIndexFails';Exec $setup 'BEGIN TRANSACTION;DROP INDEX UX_B3_ChangeRequests_RequestPublicId ON dbo.ChangeRequests;'
 try{$rows=Rows $setup $catalog $requirements $reference;Assert ((Status $rows 'SCOPED_INDEX_KEYS') -ceq 'FAIL')}finally{Exec $setup 'ROLLBACK TRANSACTION;'};Pass $case
 $case='ColumnFacetDriftFails';Exec $setup 'BEGIN TRANSACTION;ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DetailsJson;ALTER TABLE dbo.ChangeRequestEvents ALTER COLUMN DetailsJson nvarchar(1000) NULL;'
 try{$rows=Rows $setup $catalog $requirements $reference;Assert ((Status $rows 'SCOPED_COLUMN_FACETS') -ceq 'FAIL')}finally{Exec $setup 'ROLLBACK TRANSACTION;'};Pass $case
 $case='RestrictedMetadataStops';Exec $setup 'CREATE USER G1SyntheticRestricted WITHOUT LOGIN;GRANT SELECT ON SCHEMA::dbo TO G1SyntheticRestricted;DENY VIEW DEFINITION TO G1SyntheticRestricted;EXECUTE AS USER=N''G1SyntheticRestricted'';'
 try{$rows=Rows $setup ([IO.File]::ReadAllText((Join-Path $package '01-target.sql')));Assert ((Status $rows 'VISIBILITY') -ceq 'UNKNOWN')}finally{Exec $setup 'REVERT;'};Pass $case
 $case='DeniedDdlRegistryObservedConnectedOnly';Exec $setup 'GRANT VIEW DEFINITION TO G1SyntheticRestricted;DENY CONTROL,ALTER,CREATE TABLE TO G1SyntheticRestricted;DENY ALTER ON SCHEMA::dbo TO G1SyntheticRestricted;DENY INSERT,UPDATE,DELETE ON dbo.SchemaVersions TO G1SyntheticRestricted;EXECUTE AS USER=N''G1SyntheticRestricted'';'
 try{
  $rows=Rows $setup ([IO.File]::ReadAllText((Join-Path $package '04-permissions.sql')))
  foreach($code in @('NO_DATABASE_CONTROL','NO_DATABASE_ALTER','NO_CREATE_TABLE','NO_SCHEMA_ALTER','NO_REGISTRY_INSERT','NO_REGISTRY_UPDATE','NO_REGISTRY_DELETE')){Assert ((Status $rows $code) -ceq 'PASS')}
  Assert ((Status $rows 'API_PRINCIPAL_BINDING') -ceq 'UNKNOWN')
 }finally{Exec $setup 'REVERT;'};Pass $case
 $case='Complete';Assert ($results.Count -eq 18)
}catch{
 Write-Output "PreflightCase=$case; Status=STOP; Details=SUPPRESSED;"
 # Diagnostic line number only, never exception text or SQL metadata.
 Write-Output "PreflightHarnessLine=$($_.InvocationInfo.ScriptLineNumber);"
 $script:failed=$true
}finally{
 if($null -ne $setup){$setup.Dispose()}
 if($null -ne $admin){
  if($db -cmatch '^OptionCIsolated_[0-9a-f]{32}$'){
   try{Exec $admin "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE [$db]"}catch{Write-Output 'PreflightCleanup=STOP; Details=SUPPRESSED;';$script:failed=$true}
  };$admin.Dispose()
 }
 if($temp){Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue}
 $artifact=Join-Path $repo 'artifacts';New-Item -ItemType Directory -Force $artifact|Out-Null
 @{contract='g1-minimal-preflight-disposable-v1';source=$env:GITHUB_SHA;base='88c21921860888f531a0962867f916c307452bbc';cases=$results.ToArray();passed=($case -ceq 'Complete' -and -not $script:failed);provider='SQL_SERVER_2022_DEVELOPER_LOCAL';testOnlyTargetAndTimeoutDerivatives=$true;apiPrincipalBinding='UNKNOWN';liveSql=$false;acceptedSuiteRerun=$false}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $artifact 'preflight-sql.json') -Encoding utf8
}
if($script:failed){exit 1}
