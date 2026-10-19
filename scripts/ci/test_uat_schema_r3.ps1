Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1
. (Join-Path $PSScriptRoot 'uat_schema_r3.ps1')
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:SCHEMA_TEST_SQL_SERVER -cne 'localhost,1433' -or
    $env:SCHEMA_TEST_SQL_DATABASE -cne 'master') { throw 'STOP: runner-local fixture required.' }
$root = Split-Path (Split-Path $PSScriptRoot)
$admin = @{
  ServerInstance='localhost,1433'; Database='master'; Username='sa'; Password=$env:SCHEMA_TEST_SQL_PASSWORD
  TrustServerCertificate=$true; ConnectionTimeout=5; QueryTimeout=15
  AbortOnError=$true; OutputSqlErrors=$false; ErrorAction='Stop'
}
function Fixture-Sql([string]$Query) {
  try { $null = SqlServer\Invoke-Sqlcmd @admin -Query $Query -OutputAs DataSet }
  catch { throw 'STOP: disposable fixture operation failed; details suppressed.' }
}
Fixture-Sql 'CREATE DATABASE R3Fixture;'
$admin.Database='R3Fixture'
Fixture-Sql @'
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL,AppliedAt datetime2 NOT NULL);
INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',SYSUTCDATETIME());
CREATE TABLE dbo.VisitTrips(VisitTripId bigint PRIMARY KEY);
CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint PRIMARY KEY);
CREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint PRIMARY KEY);
CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint PRIMARY KEY);
INSERT dbo.VisitTrips VALUES(123456789);
CREATE LOGIN R3Restricted WITH PASSWORD='Disposable_Restricted_2026!Only';
CREATE USER R3Restricted FOR LOGIN R3Restricted;
GRANT SELECT ON dbo.SchemaVersions TO R3Restricted;
GRANT VIEW DEFINITION TO R3Restricted;
'@
try {
  $identity = SqlServer\Invoke-Sqlcmd @admin -Query "SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) AS ActualServer" -OutputAs DataSet
  $expected = [string]$identity.Tables[0].Rows[0][0]
} catch { throw 'STOP: fixture identity unavailable.' }
$connection = @{ServerInstance='localhost,1433';Database='R3Fixture';Username='sa';Password=$env:SCHEMA_TEST_SQL_PASSWORD}
$script:calls = [System.Collections.Generic.List[string]]::new()
$script:before = $null
# Instrumentation counts calls; all results/errors come from the real SQL provider.
function Invoke-Sqlcmd {
 param($ServerInstance,$Database,$Username,$Password,$AccessToken,$ApplicationIntent,$ConnectionTimeout,$QueryTimeout,
       $AbortOnError,$OutputSqlErrors,$ErrorAction,$WarningAction,$Verbose,$Debug,$TrustServerCertificate,$InputFile,$Variable,$OutputAs)
 $arguments=@{}+$PSBoundParameters
 [void]$script:calls.Add([IO.Path]::GetFileNameWithoutExtension($InputFile))
 if ($null -ne $script:before) { & $script:before $script:calls.Count }
 SqlServer\Invoke-Sqlcmd @arguments
}
$manifestRelative='scripts/ci/uat_schema_r3.hashes.json'
$manifestHash=(Get-FileHash (Join-Path $root $manifestRelative)).Hash.ToLowerInvariant()
$script:passed=0
function Run-Case([string]$Name,[int]$Calls,[string]$StopStage,[string]$Classification,[hashtable]$Overrides=@{}) {
 $script:calls.Clear()
 $argsR3=@{Connection=$connection;SourceSha=$env:GITHUB_SHA;ExpectedServer=$expected;ExpectedDatabase='R3Fixture';Mode='Disposable';Root=$root;ManifestHash=$manifestHash}
 foreach($key in $Overrides.Keys){$argsR3[$key]=$Overrides[$key]}
 $caught=$false
 $records=@(try { Invoke-R3Diagnostic @argsR3 } catch { $caught=$true; 'SANITIZED_TERMINATION' })
 $text=$records -join "`n"
 # Print only already-sanitized stage lines, including when a harness assertion fails.
 foreach($record in $records){
   if($record -cmatch '^Stage=(IdentityVersion|Catalog|HistoricalAccess|HistoricalAggregates); Status=(PASS|STOP); (Classification=[A-Za-z]+; ProviderCode=(Suppressed|-?[0-9]+); )?SourceSha=[0-9a-f]{40}; SqlSha256=[0-9a-f]{64};$'){Write-Output $record}
 }
 if($text -match 'Classification=(UnclassifiedSqlFailure|UnknownSqlFailure); ProviderCode=(?!Suppressed;)'){throw "STOP: unsupported provider suppression failed: $Name"}

 if ($script:calls.Count -ne $Calls) { throw "STOP: call count mismatch: $Name" }
 $order=@('IdentityVersion','Catalog','HistoricalAccess','HistoricalAggregates')
 for($i=0;$i -lt $Calls;$i++){if($script:calls[$i] -cne $order[$i]){throw "STOP: order mismatch: $Name"}}
 if ($StopStage) {
   if (-not $caught -or $text -notmatch "Stage=$StopStage; Status=STOP; Classification=$Classification;") { throw "STOP: expected sanitized stop missing: $Name" }
   if (@($records | Where-Object {$_ -match 'Status=PASS'}).Count -ne [Math]::Max(0,$Calls-1)) {
     # A preflight failure invokes no SQL and emits zero PASS lines.
     throw "STOP: unexpected PASS count: $Name"
   }
 } elseif($caught -or @($records | Where-Object {$_ -match 'Status=PASS'}).Count -ne 4) {throw "STOP: positive stages failed: $Name"}
 foreach($record in $records){
   if($record -cne 'SANITIZED_TERMINATION' -and $record -cnotmatch '^Stage=(IdentityVersion|Catalog|HistoricalAccess|HistoricalAggregates); Status=(PASS|STOP); (Classification=[A-Za-z]+; ProviderCode=(Suppressed|-?[0-9]+); )?SourceSha=[0-9a-f]{40}; SqlSha256=[0-9a-f]{64};$'){throw "STOP: output contract: $Name"}
 }
 if($text -match '123456789|DISPOSABLE_SECRET|R3Restricted|ServerName|localhost|SqlException|SELECT |Password|AccessToken' -or $text.Contains($expected)){throw "STOP: privacy contract: $Name"}
 $script:passed++
 Write-Output "PASS R3 disposable case: $Name"
}
Run-Case 'four-real-stages' 4 '' ''
Run-Case 'wrong-server' 1 'IdentityVersion' 'TargetGuardRejected' @{ExpectedServer='WrongServer'}
Run-Case 'wrong-database-input' 0 'IdentityVersion' 'PrerequisiteRejected' @{Connection=@{ServerInstance='localhost,1433';Database='master';Username='sa';Password=$env:SCHEMA_TEST_SQL_PASSWORD}}
Fixture-Sql "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-009';"
Run-Case 'wrong-schema-version' 1 'IdentityVersion' 'RequiredObjectMissing'
Fixture-Sql "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-010'; ALTER TABLE dbo.SchemaVersions DROP COLUMN AppliedAt;"
Run-Case 'missing-version-column' 1 'IdentityVersion' 'MissingObjectOrColumn'
Fixture-Sql 'ALTER TABLE dbo.SchemaVersions ADD AppliedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(); DROP TABLE dbo.VisitTripStops;'
Run-Case 'missing-historical-table' 2 'Catalog' 'RequiredObjectMissing'
Fixture-Sql 'CREATE TABLE dbo.VisitTripStops(WrongKey bigint);'
Run-Case 'missing-historical-column' 2 'Catalog' 'RequiredObjectMissing'
Fixture-Sql 'ALTER TABLE dbo.VisitTripStops ADD VisitTripStopId bigint;'
$restricted=@{ServerInstance='localhost,1433';Database='R3Fixture';Username='R3Restricted';Password='Disposable_Restricted_2026!Only'}
Run-Case 'select-permission-guard' 3 'HistoricalAccess' 'HistoricalSelectOrObjectUnavailable' @{Connection=$restricted}
Fixture-Sql 'DENY SELECT ON dbo.SchemaVersions TO R3Restricted;'
Run-Case 'real-provider-permission-denial' 1 'IdentityVersion' 'SqlPermissionDenied' @{Connection=$restricted}
$script:before={param($n) if($n -eq 4){Fixture-Sql 'ALTER TABLE dbo.VisitTripStops DROP COLUMN VisitTripStopId;'}}
Run-Case 'stage-four-missing-key-stops' 4 'HistoricalAggregates' 'MissingObjectOrColumn'
$script:before=$null
Fixture-Sql 'ALTER TABLE dbo.VisitTripStops ADD VisitTripStopId bigint;'
# Test-only reviewed-input mutations prove error handling with REAL SQL, never UAT.
function New-MutatedRoot([string]$Stage,[string]$Body,[switch]$BadHash) {
 $temp=Join-Path $env:RUNNER_TEMP ([guid]::NewGuid().ToString('N'))
 $manifest=Get-Content (Join-Path $root $manifestRelative) -Raw | ConvertFrom-Json -AsHashtable
 foreach($relative in $manifest.Keys){
  $target=Join-Path $temp $relative
  $null=New-Item (Split-Path $target) -ItemType Directory -Force
  Copy-Item (Join-Path $root $relative) $target
 }
 $relative="docs/release/optionc-canonical-sql-draft/full-scheme-a/r3/$Stage.sql"
 [IO.File]::WriteAllText((Join-Path $temp $relative),$Body)
 if(-not $BadHash){$manifest[$relative]=(Get-FileHash (Join-Path $temp $relative)).Hash.ToLowerInvariant()}
 $manifest | ConvertTo-Json | Set-Content (Join-Path $temp $manifestRelative)
 return @{Root=$temp;ManifestHash=(Get-FileHash (Join-Path $temp $manifestRelative)).Hash.ToLowerInvariant()}
}
Run-Case 'sql-syntax-suppressed' 2 'Catalog' 'SqlSyntaxOrCompatibility' (New-MutatedRoot 'Catalog' 'SELECT FROM DISPOSABLE_SECRET;')
Run-Case 'unknown-provider-suppressed' 2 'Catalog' 'UnclassifiedSqlFailure' (New-MutatedRoot 'Catalog' "THROW 50001,N'DISPOSABLE_SECRET',1;")
Run-Case 'query-timeout' 2 'Catalog' 'TimeoutOrTransient' (New-MutatedRoot 'Catalog' "WAITFOR DELAY '00:00:35'; SELECT CAST(1 AS int) AS StagePass;")
Run-Case 'sql-hash-tamper-before-connect' 0 'IdentityVersion' 'PrerequisiteRejected' (New-MutatedRoot 'Catalog' 'SELECT 1;' -BadHash)
Run-Case 'manifest-hash-tamper' 0 'IdentityVersion' 'PrerequisiteRejected' @{ManifestHash=('0'*64)}
Run-Case 'source-sha-malformed' 0 'IdentityVersion' 'PrerequisiteRejected' @{SourceSha=('b'*40+'z')}
# Data-plane database guard, same runner-local database and a deliberately rehashed test SQL.
$identitySql=Get-Content (Join-Path $root 'docs/release/optionc-canonical-sql-draft/full-scheme-a/r3/IdentityVersion.sql') -Raw
Run-Case 'wrong-observed-database' 1 'IdentityVersion' 'TargetGuardRejected' (New-MutatedRoot 'IdentityVersion' ($identitySql.Replace("N'`$(R3ExpectedDatabase)'","N'WrongDatabase'")))
Run-Case 'extra-result-set-rejected' 2 'Catalog' 'ResultContractRejected' (New-MutatedRoot 'Catalog' 'SELECT CAST(1 AS int) AS StagePass; SELECT CAST(1 AS int) AS StagePass;')
Run-Case 'empty-result-rejected' 2 'Catalog' 'ResultContractRejected' (New-MutatedRoot 'Catalog' 'SELECT CAST(1 AS int) AS StagePass WHERE 1=0;')
# Stop only the disposable container, then verify the fixed endpoint's connection failure.
$container = docker ps --filter ancestor=mcr.microsoft.com/mssql/server:2022-latest --format '{{.ID}}'
if($LASTEXITCODE -ne 0 -or @($container).Count -ne 1 -or $container -notmatch '^[0-9a-f]{12}$'){throw 'STOP: disposable container not uniquely identified.'}
$null = docker stop --time 1 $container
if($LASTEXITCODE -ne 0){throw 'STOP: disposable shutdown failed.'}
# SqlServer module may surface a nonallowlisted provider number: classification must remain suppressed or approved connectivity.
Run-Case 'connection-stopped-service' 1 'IdentityVersion' '(ConnectivityFailure|TimeoutOrTransient|UnclassifiedSqlFailure|UnknownSqlFailure)'
Write-Output "R3 disposable SQL tests: $script:passed passed, 0 failed"
