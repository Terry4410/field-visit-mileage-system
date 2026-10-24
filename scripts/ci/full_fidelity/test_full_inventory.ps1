Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1
. (Join-Path $PSScriptRoot '../uat_schema_fidelity_evidence.ps1')
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot))
$sql=Join-Path $root 'docs/release/full-schema-fidelity/UatSchemaFidelity.release-gate.readonly.sql'
$sqlHash='fcd35b6dc7df5f18e5cea09c9110e3d4efdeb82dee7dbb0291a57ffdedacf8df'
$validatorHash='5b71d40b58c6119fc1d694f3b934f89745ee4633c873da8dfd50fa1bd3309653'
if($env:GITHUB_ACTIONS -cne 'true' -or [string]::IsNullOrWhiteSpace($env:FIDELITY_FIXTURE_PASSWORD)){throw 'STOP: disposable prerequisites.'}
if((Get-FileHash $sql).Hash.ToLowerInvariant() -cne $sqlHash -or (Get-FileHash (Join-Path $PSScriptRoot '../uat_schema_fidelity_evidence.ps1')).Hash.ToLowerInvariant() -cne $validatorHash){throw 'STOP: original hash mismatch.'}
$conn=@{ServerInstance='localhost,1433';Database='master';Username='sa';Password=$env:FIDELITY_FIXTURE_PASSWORD;TrustServerCertificate=$true;ConnectionTimeout=10;QueryTimeout=30;AbortOnError=$true;OutputSqlErrors=$false;ErrorAction='Stop';WarningAction='SilentlyContinue'}
$script:passed=0
$script:phase='CaseBody'
function Fixture([string]$Query){try{$null=Invoke-Sqlcmd @conn -Query $Query -OutputAs DataSet}catch{throw 'STOP: fixture setup suppressed.'}}
function Inventory([string]$Query='',[string]$User='sa'){
 $argsSql=@{}+$conn;$argsSql.Username=$User
 if($Query){return Invoke-Sqlcmd @argsSql -Query $Query -OutputAs DataSet}
 return Invoke-Sqlcmd @argsSql -InputFile $sql -OutputAs DataSet
}
function Normalize($Data){Convert-UatSchemaInventory -DataSet $Data -ExpectedServer 'sql-fieldvisit-jpe-uat' -ExpectedDatabase 'db-fieldvisit-uat' -SourceSha $env:FIDELITY_SOURCE_SHA -SqlSha256 $sqlHash}
function Case([string]$Name,[scriptblock]$Body){
 try{$script:phase='CaseBody';& $Body;$script:passed++;Write-Output "PASS FullInventory case: $Name"}catch{$safe=Get-SanitizedUatInventoryFailure $_;Write-Output $safe;Write-Output "STOP FullInventory case: $Name; Phase=$script:phase; details suppressed.";exit 1}
}
function Reject([scriptblock]$Body,[string]$Class=''){
 $stopped=$false
 try{$null=&$Body}catch{$stopped=$true;if($Class){$safe=Get-SanitizedUatInventoryFailure $_;if($safe -notmatch "Classification=$Class;" -or $safe -match 'SYNTHETIC_RAW'){throw 'classification'}}}
 if(-not $stopped){throw 'expected rejection'}
}
Fixture 'CREATE DATABASE [db-fieldvisit-uat];'
$conn.Database='db-fieldvisit-uat'
Fixture @'
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40),AppliedAt datetime2);
INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',SYSUTCDATETIME());
CREATE TABLE dbo.VisitTrips(VisitTripId bigint PRIMARY KEY, Note nvarchar(80) NOT NULL DEFAULT N'fixture  value');
CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint PRIMARY KEY,VisitTripId bigint NULL CONSTRAINT FK_Full_StopTrip REFERENCES dbo.VisitTrips(VisitTripId));
CREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint PRIMARY KEY);
CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint PRIMARY KEY);
CREATE TABLE dbo.CatalogOnly(CatalogId bigint PRIMARY KEY);
CREATE TABLE dbo.RouteCalculationAttempts(RouteCalculationAttemptId bigint PRIMARY KEY,CalculationReason nvarchar(40),CONSTRAINT CK_RouteCalculationAttempts_Reason CHECK(CalculationReason IN(N'VisitorCalculate',N'LeaderRetry',N'CorrectionRecalculate',N'BackgroundMileageJob')));
CREATE INDEX IX_Full_StopTrip ON dbo.VisitTripStops(VisitTripId) INCLUDE(VisitTripStopId);
INSERT dbo.VisitTrips(VisitTripId) VALUES(123456789);


'@
$escapedFixturePassword=$env:FIDELITY_FIXTURE_PASSWORD.Replace("'","''")
Fixture "CREATE LOGIN FullRestricted WITH PASSWORD=N'$escapedFixturePassword'; CREATE USER FullRestricted FOR LOGIN FullRestricted;"
Case 'original-unquoted-rowcount-syntax-rejected' {Reject {Inventory ((Get-Content $sql -Raw).Replace('AS [RowCount]','AS RowCount'))} 'SqlSyntaxOrCompatibility'}
Case 'exact-seven-result-sets' {
 $script:phase='SqlExecution'
 $script:data=Inventory
 $script:phase='DatasetShape'
 if($data.Tables.Count -ne 7){throw 'shape'}
 $script:phase='Normalize'
 Write-Output ('ContractSevenTables=' + ($data.Tables.Count -eq 7))
 Write-Output ('ContractServerMatch=' + ([string]$data.Tables[0].Rows[0]['ServerName'] -ceq 'sql-fieldvisit-jpe-uat'))
 $script:normalized=Normalize $data
 $script:phase='FacetAssertions'
 if($normalized.SchemaVersion.Latest -cne '1.8.0-010' -or $normalized.Columns.Count -lt 6 -or $normalized.Checks.Count -lt 1 -or $normalized.ForeignKeys.Count -lt 1){throw 'facets'}
 $dir=Join-Path $env:RUNNER_TEMP 'full-fidelity-private';$null=New-Item $dir -ItemType Directory -Force
 $script:privateFile=Join-Path $dir 'fixture.json'
 $script:phase='WriteNormalized'
 $null=Write-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat' 'db-fieldvisit-uat' $env:FIDELITY_SOURCE_SHA $sqlHash $privateFile
}
Case 'column-default-index-fk-check-and-aggregates' {
 if(-not @($normalized.Columns|Where-Object {$_.DefaultDefinition -like '*fixture  value*'}).Count){throw 'default'}
 if(-not @($normalized.Indexes|Where-Object {$_.IsIncluded}).Count){throw 'included index'}
 if($normalized.HistoricalAggregates.Count -ne 4 -or $normalized.HistoricalAggregates[0].RowCount -ne 1){throw 'aggregate'}
}
Case 'wrong-server' {Reject {Inventory ((Get-Content $sql -Raw).Replace("@ExpectedServer sysname=N'sql-fieldvisit-jpe-uat'","@ExpectedServer sysname=N'wrong-fixture'"))} 'TargetGuardRejected'}
Case 'wrong-database' { $old=$conn.Database;$conn.Database='master';try{Reject {Inventory} 'TargetGuardRejected'}finally{$conn.Database=$old} }
Case 'wrong-version' {Fixture "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-009'";Reject {Normalize (Inventory)};Fixture "UPDATE dbo.SchemaVersions SET VersionNumber=N'1.8.0-010'"}
Case 'missing-required-column' {Fixture 'ALTER TABLE dbo.SchemaVersions DROP COLUMN AppliedAt';Reject {Inventory} 'MissingObjectOrColumn';Fixture 'ALTER TABLE dbo.SchemaVersions ADD AppliedAt datetime2 NULL'}
Case 'missing-required-table' {Fixture 'DROP TABLE dbo.VisitTripSnapshotStops';Reject {Inventory} '(HistoricalSelectOrObjectUnavailable|MissingObjectOrColumn)';Fixture 'CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint PRIMARY KEY)'}
Case 'missing-permissions' {
 $old=$conn.Password;$conn.Password=$env:FIDELITY_FIXTURE_PASSWORD
 try {Reject {Inventory -User 'FullRestricted'} '(HistoricalSelectOrObjectUnavailable|RequiredObjectMissing|SqlPermissionDenied|MissingObjectOrColumn)'}finally{$conn.Password=$old}
}
Case 'unexpected-result-set' { $copy=$data.Copy();$null=$copy.Tables.Add('Extra');Reject {Normalize $copy};$copy.Dispose() }
Case 'unexpected-column' {$copy=$data.Copy();$null=$copy.Tables[2].Columns.Add('Extra',[string]);Reject {Normalize $copy};$copy.Dispose()}
Case 'missing-result-set' {$copy=$data.Copy();$copy.Tables.RemoveAt(6);Reject {Normalize $copy};$copy.Dispose()}
Case 'wrong-source-format' {Reject {Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat' 'db-fieldvisit-uat' 'bad' $sqlHash}}
Case 'wrong-sql-hash-format' {Reject {Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat' 'db-fieldvisit-uat' $env:FIDELITY_SOURCE_SHA 'bad'}}
Case 'syntax-classified' {Reject {Inventory 'SELECT FROM SYNTHETIC_RAW'} 'SqlSyntaxOrCompatibility'}
Case 'compatibility-classified' {Reject {Inventory 'SELECT SYNTHETIC_RAW(1)'} '(SqlSyntaxOrCompatibility|UnclassifiedSqlFailure)'}
Case 'unsupported-provider-suppressed' {
 try {$null=Inventory "THROW 50001,N'SYNTHETIC_RAW',1;";throw 'did not throw'}catch{
  $safe=Get-SanitizedUatInventoryFailure $_
  if($safe -notmatch 'Classification=UnclassifiedSqlFailure; ProviderCode=Suppressed;' -or $safe.Contains('SYNTHETIC_RAW')){throw 'suppression'}
 }
}
Case 'altered-facets-visible' {Fixture 'ALTER TABLE dbo.VisitTrips ADD Drift int NULL';$changed=Normalize (Inventory);if($changed.Columns.Count -ne $normalized.Columns.Count+1){throw 'drift'};Fixture 'ALTER TABLE dbo.VisitTrips DROP COLUMN Drift'}
Case 'partial-metadata-visibility-characterized' {
 Fixture 'GRANT SELECT ON dbo.RouteCalculationAttempts TO FullRestricted; GRANT SELECT ON dbo.SchemaVersions TO FullRestricted; GRANT SELECT ON dbo.VisitTrips TO FullRestricted; GRANT SELECT ON dbo.VisitTripStops TO FullRestricted; GRANT SELECT ON dbo.VisitTripSnapshots TO FullRestricted; GRANT SELECT ON dbo.VisitTripSnapshotStops TO FullRestricted;'
 $old=$conn.Password;$conn.Password=$env:FIDELITY_FIXTURE_PASSWORD
 try {$partial=Normalize (Inventory -User 'FullRestricted');if($partial.Columns.Count -ge $normalized.Columns.Count){throw 'partial fixture not hidden'}}finally{$conn.Password=$old}
 # This PASS characterizes a coverage gap: validator shape alone does not prove catalog visibility.
}
Case 'normalized-real-provider-evidence-binding' {
 $digest=(Get-FileHash $privateFile).Hash.ToLowerInvariant()
 $output=& python3 (Join-Path $root 'scripts/ci/compare_uat010_schema_inventory.py') --expected $privateFile --actual $privateFile --expected-source-sha $env:FIDELITY_SOURCE_SHA --actual-source-sha $env:FIDELITY_SOURCE_SHA --expected-sha256 $digest --actual-sha256 $digest
 if($LASTEXITCODE -ne 0 -or $output -cne 'SCHEMA_FIDELITY_COMPARISON_MATCH'){throw 'binding'}
 # Same-file synthetic fixture validates serialization/interoperability only, never reference provenance.
}
$null=Remove-Item $privateFile -Force
$dir=Join-Path $env:RUNNER_TEMP 'full-fidelity-evidence';$null=New-Item $dir -ItemType Directory -Force
@{SourceSha=$env:FIDELITY_SOURCE_SHA;SqlSha256=$sqlHash;ValidatorSha256=$validatorHash;RealSqlServer2022=$true;Passed=$script:passed;Failed=0;PartialMetadataVisibilityGap=$true}|ConvertTo-Json|Set-Content (Join-Path $dir 'contract-results.json')
Write-Output "Full Inventory disposable contracts: $script:passed passed, 0 failed"
