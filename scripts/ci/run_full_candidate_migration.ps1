$ErrorActionPreference='Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
$raw=$env:E1_R1_SQL_CONNECTION
if([string]::IsNullOrWhiteSpace($raw)){throw 'R3_SQL_CONNECTION_MISSING'}
$b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($raw)
if($b.DataSource -notin @('localhost,1433','127.0.0.1,1433') -or $b.InitialCatalog -ne 'master' -or $b.UserID -ne 'sa'){throw 'R3_NONLOCAL_TARGET_DENIED'}
$db='OptionCIsolated_R3_'+[guid]::NewGuid().ToString('N')
function Conn([string]$catalog){$x=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($raw);$x.InitialCatalog=$catalog;$x.Pooling=$false;return $x.ConnectionString}
$script:runNo=0
function Run([string]$step,[string]$sql,[string]$sourcePath){
  $script:runNo++
  $connection=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new((Conn $db))
  if([string]::IsNullOrWhiteSpace($sourcePath)){$resolved='<inline SQL>'; $sourceHash=[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql)))}
  else{$resolved=(Resolve-Path -LiteralPath $sourcePath -ErrorAction Stop).Path; $sourceHash=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash}
  Write-Output "R3_SQL_STEP=$script:runNo NAME=$step"
  Write-Output "R3_SQL_SOURCE_PATH=$resolved"
  Write-Output "R3_SQL_SOURCE_SHA256=$sourceHash"
  Write-Output "R3_SQL_CONNECTION_INITIAL_CATALOG=$($connection.InitialCatalog)"
  try {
    $session=Invoke-Sqlcmd -ConnectionString $connection.ConnectionString -Query "SELECT @@SPID AS SessionId, @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, SCHEMA_NAME() AS DefaultSchema, OBJECT_ID(N'dbo.SchemaVersions',N'U') AS SchemaVersionsObjectId, COALESCE((SELECT STRING_AGG(c.name,N',') WITHIN GROUP(ORDER BY c.column_id) FROM sys.columns AS c WHERE c.object_id=OBJECT_ID(N'dbo.SchemaVersions',N'U')),N'<none>') AS SchemaVersionsColumns" -AbortOnError -QueryTimeout 30
    Write-Output "R3_SQL_SESSION_ID=$($session.SessionId) SERVER=$($session.ServerName) DATABASE=$($session.DatabaseName) DEFAULT_SCHEMA=$($session.DefaultSchema) SCHEMAVERSIONS_OBJECT_ID=$($session.SchemaVersionsObjectId) COLUMNS=$($session.SchemaVersionsColumns)"
    if($session.DatabaseName -ne $db){throw "R3_SQL_TARGET_MISMATCH_EXPECTED_${db}_ACTUAL_$($session.DatabaseName)"}
    Invoke-Sqlcmd -ConnectionString $connection.ConnectionString -Query $sql -AbortOnError -QueryTimeout 30
  } catch {
    $e=$_.Exception
    while($null -ne $e){
      foreach($property in @('Number','LineNumber','Procedure')){if($e.PSObject.Properties.Name -contains $property){Write-Output "R3_SQL_EXCEPTION_$property=$($e.$property)"}}
      $e=$e.InnerException
    }
    Write-Output "R3_SQL_EXCEPTION_MESSAGE=$($_.Exception.Message)"
    throw
  }
}
Invoke-Sqlcmd -ConnectionString $raw -Query "CREATE DATABASE [$db]" -AbortOnError
try {
$boot=@'
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL DROP TABLE dbo.SchemaVersions;
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) PRIMARY KEY,Description nvarchar(500),AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200));
CREATE TABLE dbo.Organizations(OrganizationId int PRIMARY KEY); CREATE TABLE dbo.Teams(TeamId int PRIMARY KEY); CREATE TABLE dbo.Users(UserId int PRIMARY KEY);
CREATE TABLE dbo.Locations(LocationId int PRIMARY KEY,IsActive bit NOT NULL,ApprovalStatus nvarchar(30) NOT NULL);
CREATE TABLE dbo.VisitTrips(VisitTripId bigint PRIMARY KEY,Status nvarchar(30) NOT NULL,LocationId int NOT NULL);
CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint PRIMARY KEY,VisitTripId bigint NOT NULL,LocationId int NOT NULL,LocationNameSnapshot nvarchar(200),AddressSnapshot nvarchar(500));
CREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint PRIMARY KEY,VisitTripId bigint NOT NULL,ApprovedDistanceKmSnapshot decimal(10,2),StatusSnapshot nvarchar(30));
CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint PRIMARY KEY,VisitTripSnapshotId bigint NOT NULL,LocationId int NOT NULL,LocationNameSnapshot nvarchar(200));
INSERT dbo.Organizations VALUES(1); INSERT dbo.Teams VALUES(1); INSERT dbo.Users VALUES(1),(2),(3);
INSERT dbo.Locations VALUES(10,1,N'Approved'),(11,1,N'Approved'); INSERT dbo.VisitTrips VALUES(1,N'Approved',10);
INSERT dbo.VisitTripStops VALUES(1,1,10,N'Frozen A',N'Address A'); INSERT dbo.VisitTripSnapshots VALUES(1,1,12.3,N'Approved'); INSERT dbo.VisitTripSnapshotStops VALUES(1,1,10,N'Frozen A');
INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'baseline',SYSUTCDATETIME(),N'fixture');
'@
Invoke-Sqlcmd -ConnectionString (Conn $db) -Query $boot -AbortOnError
$cols=@(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SchemaVersions',N'U') ORDER BY column_id").name
if($cols -notcontains 'AppliedAt'){throw "R3_FIXTURE_SCHEMA_VERSIONS_MISSING_APPLIEDAT_$([string]::Join(',', $cols))"}
Write-Output "BOOTSTRAP_SCHEMA_COLUMNS=$([string]::Join(',', $cols))"
$root=Join-Path $PSScriptRoot '../../docs/release/optionc-canonical-sql-draft/candidate'
$chain=@('1800_011','1800_012','1800_014','1800_015','1800_016','1800_017','1800_018','1800_019','1800_020')
$expected=@{'1800_011'='1.8.0-011';'1800_012'='1.8.0-012';'1800_014'='1.8.0-014';'1800_015'='1.8.0-015';'1800_016'='1.8.0-016';'1800_017'='1.8.0-017';'1800_018'='1.8.0-018';'1800_019'='1.8.0-019';'1800_020'='1.8.0-020'}
foreach($m in $chain){
  $dir=Join-Path $root $m
  foreach($f in @('Preflight.sql','Up.sql')){$file=Join-Path $dir $f; Run "$m/$f" (Get-Content -LiteralPath $file -Raw) $file}
  if($m -eq '1800_014'){$file=Join-Path $dir 'Backfill.sql'; Run "$m/Backfill.sql" (Get-Content -LiteralPath $file -Raw) $file}
  $file=Join-Path $dir 'Verify.sql'; Run "$m/Verify.sql" (Get-Content -LiteralPath $file -Raw) $file
  $actual=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT MAX(VersionNumber) AS VersionNumber FROM dbo.SchemaVersions").VersionNumber
  if($actual -ne $expected[$m]){throw "R3_VERSION_MISMATCH_$m"}
  Write-Output "MIGRATION_PASS $($expected[$m])"
}
try { $file=Join-Path $root '1800_020/Up.sql'; Run '1800_020/Up.sql rerun-negative' (Get-Content -LiteralPath $file -Raw) $file; throw 'R3_RERUN_UNEXPECTED_PASS' } catch { if($_.Exception.Message -match 'UNEXPECTED_PASS'){throw}; Write-Output 'FAIL_CLOSED_RERUN_PASS' }
$count=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT COUNT(*) C FROM dbo.SchemaVersions').C
if($count -ne 10){throw "R3_FINAL_MARKER_COUNT_$count"}
try { Run "BEGIN TRAN; INSERT dbo.SchemaVersions VALUES(N'1.8.0-021',N'injected failure',SYSUTCDATETIME(),N'test'); THROW 52999,N'R3_INJECTED_FAILURE_STOP',1; COMMIT"; throw 'R3_FAILURE_DID_NOT_STOP' } catch { if($_.Exception.Message -match 'DID_NOT_STOP'){throw}; Write-Output 'FAIL_CLOSED_FAILURE_INJECTION_PASS' }
$hist=Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT COUNT(*) RowCount FROM dbo.VisitTrips'
if($hist.RowCount -ne 1){throw 'R3_HISTORY_ROWCOUNT_CHANGED'}
Write-Output "R3_DRAFT_CHAIN_REHEARSAL_PASS final=$((Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT MAX(VersionNumber) AS VersionNumber FROM dbo.SchemaVersions').VersionNumber) UAT_MIGRATION_STATUS=HOLD_CANONICAL_DDL_AND_WRITER_E2E_NOT_PROVEN"
} finally { Invoke-Sqlcmd -ConnectionString $raw -Query "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]" -AbortOnError }
