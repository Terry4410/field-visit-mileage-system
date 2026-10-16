$ErrorActionPreference='Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
$raw=$env:E1_R1_SQL_CONNECTION
if([string]::IsNullOrWhiteSpace($raw)){throw 'R3_SQL_CONNECTION_MISSING'}
$b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($raw)
if($b.DataSource -notin @('localhost,1433','127.0.0.1,1433') -or $b.InitialCatalog -ne 'master' -or $b.UserID -ne 'sa'){throw 'R3_NONLOCAL_TARGET_DENIED'}
$db='OptionCIsolated_R3_'+[guid]::NewGuid().ToString('N')
function Conn([string]$catalog){$x=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($raw);$x['Initial Catalog']=$catalog;$x['Pooling']=$false;return $x.ConnectionString}
$script:runNo=0
function Run([string]$step,[string]$sql,[string]$sourcePath=$null){
  $script:runNo++
  $connection=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new((Conn $db))
  if([string]::IsNullOrWhiteSpace($sourcePath)){$resolved='<inline SQL>'; $sourceHash=[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql)))}
  else{$resolved=(Resolve-Path -LiteralPath $sourcePath -ErrorAction Stop).Path; $sourceHash=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash}
  Write-Output "R3_SQL_STEP=$script:runNo NAME=$step"
  Write-Output "R3_SQL_SOURCE_PATH=$resolved"
  Write-Output "R3_SQL_SOURCE_SHA256=$sourceHash"
  Write-Output "R3_SQL_CONNECTION_INITIAL_CATALOG=$($connection.InitialCatalog)"
  $sqlConnection=[Microsoft.Data.SqlClient.SqlConnection]::new($connection.ConnectionString)
  try {
    $sqlConnection.Open()
    $probe=$sqlConnection.CreateCommand()
    $probe.CommandText="SELECT @@SPID AS SessionId, @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, SCHEMA_NAME() AS DefaultSchema, OBJECT_ID(N'dbo.SchemaVersions',N'U') AS SchemaVersionsObjectId, COALESCE((SELECT STRING_AGG(c.name,N',') WITHIN GROUP(ORDER BY c.column_id) FROM sys.columns AS c WHERE c.object_id=OBJECT_ID(N'dbo.SchemaVersions',N'U')),N'<none>') AS SchemaVersionsColumns"
    $probe.CommandTimeout=30
    $sessionReader=$probe.ExecuteReader()
    if(-not $sessionReader.Read()){throw 'R3_SQL_SESSION_DIAGNOSTIC_EMPTY'}
    $sessionId=$sessionReader['SessionId']; $serverName=$sessionReader['ServerName']; $databaseName=$sessionReader['DatabaseName']; $defaultSchema=$sessionReader['DefaultSchema']; $schemaObjectId=$sessionReader['SchemaVersionsObjectId']; $schemaColumns=$sessionReader['SchemaVersionsColumns']
    $sessionReader.Close()
    Write-Output "R3_SQL_SESSION_ID=$sessionId SERVER=$serverName DATABASE=$databaseName DEFAULT_SCHEMA=$defaultSchema SCHEMAVERSIONS_OBJECT_ID=$schemaObjectId COLUMNS=$schemaColumns"
    if($databaseName -ne $db){throw "R3_SQL_TARGET_MISMATCH_EXPECTED_${db}_ACTUAL_$databaseName"}
    $command=$sqlConnection.CreateCommand(); $command.CommandText=$sql; $command.CommandTimeout=30
    [void]$command.ExecuteNonQuery()
  } catch {
    $e=$_.Exception
    while($null -ne $e){
      if($e.GetType().FullName -eq 'Microsoft.Data.SqlClient.SqlException'){
        foreach($sqlError in $e.Errors){Write-Output "R3_SQL_EXCEPTION_NUMBER=$($sqlError.Number) LINE=$($sqlError.LineNumber) PROCEDURE=$($sqlError.Procedure)"}
      }
      foreach($property in @('Number','LineNumber','Procedure')){if($e.PSObject.Properties.Name -contains $property){Write-Output "R3_SQL_EXCEPTION_$property=$($e.$property)"}}
      $e=$e.InnerException
    }
    Write-Output "R3_SQL_EXCEPTION_MESSAGE=$($_.Exception.Message)"
    throw
  } finally {
    if($null -ne $sqlConnection){$sqlConnection.Dispose()}
  }
}
Invoke-Sqlcmd -ConnectionString $raw -Query "CREATE DATABASE [$db]" -AbortOnError
try {
$boot=@'
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL DROP TABLE dbo.SchemaVersions;
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) PRIMARY KEY,Description nvarchar(500),AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200));
CREATE TABLE dbo.Organizations(OrganizationId int PRIMARY KEY); CREATE TABLE dbo.Teams(TeamId int PRIMARY KEY); CREATE TABLE dbo.Users(UserId int PRIMARY KEY);
CREATE TABLE dbo.Locations(LocationId int PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NOT NULL,CreatedByUserId int NOT NULL,LocationType nvarchar(20) NOT NULL,LocationName nvarchar(200) NOT NULL,City nvarchar(100) NULL,District nvarchar(100) NULL,Address nvarchar(500) NOT NULL,PlusCode nvarchar(100) NULL,TaxId nvarchar(50) NULL,MasterNote nvarchar(1000) NULL,ApprovalStatus nvarchar(30) NOT NULL,IsActive bit NOT NULL,RowVersion rowversion NOT NULL);
CREATE TABLE dbo.VisitTrips(VisitTripId bigint PRIMARY KEY,Status nvarchar(30) NOT NULL,LocationId int NOT NULL);
CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint PRIMARY KEY,VisitTripId bigint NOT NULL,LocationId int NOT NULL,LocationNameSnapshot nvarchar(200),AddressSnapshot nvarchar(500));
CREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint PRIMARY KEY,VisitTripId bigint NOT NULL,ApprovedDistanceKmSnapshot decimal(10,2),StatusSnapshot nvarchar(30));
CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint PRIMARY KEY,VisitTripSnapshotId bigint NOT NULL,LocationId int NOT NULL,LocationNameSnapshot nvarchar(200));
INSERT dbo.Organizations VALUES(1); INSERT dbo.Teams VALUES(1); INSERT dbo.Users VALUES(1),(2),(3);
INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,LocationType,LocationName,City,District,Address,ApprovalStatus,IsActive) VALUES(10,1,1,1,N'Customer',N'Legacy approved 10',N'Taipei',N'Xinyi',N'Legacy address 10',N'Approved',1),(11,1,1,1,N'Customer',N'Legacy approved 11',N'Taipei',N'Da-an',N'Legacy address 11',N'Approved',1); INSERT dbo.VisitTrips VALUES(1,N'Approved',10);
INSERT dbo.VisitTripStops VALUES(1,1,10,N'Frozen A',N'Address A'); INSERT dbo.VisitTripSnapshots VALUES(1,1,12.3,N'Approved'); INSERT dbo.VisitTripSnapshotStops VALUES(1,1,10,N'Frozen A');
INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'baseline',SYSUTCDATETIME(),N'fixture');
'@
Invoke-Sqlcmd -ConnectionString (Conn $db) -Query $boot -AbortOnError
$cols=@(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SchemaVersions',N'U') ORDER BY column_id").name
if($cols -notcontains 'AppliedAt'){throw "R3_FIXTURE_SCHEMA_VERSIONS_MISSING_APPLIEDAT_$([string]::Join(',', $cols))"}
Write-Output "BOOTSTRAP_SCHEMA_COLUMNS=$([string]::Join(',', $cols))"
$historySql="SELECT N'VisitTrips' TableName,COUNT_BIG(*) AS RowTotal,CHECKSUM_AGG(BINARY_CHECKSUM(VisitTripId,Status,LocationId)) AS Fingerprint FROM dbo.VisitTrips UNION ALL SELECT N'VisitTripStops',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(VisitTripStopId,VisitTripId,LocationId,LocationNameSnapshot,AddressSnapshot)) FROM dbo.VisitTripStops UNION ALL SELECT N'VisitTripSnapshots',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(VisitTripSnapshotId,VisitTripId,ApprovedDistanceKmSnapshot,StatusSnapshot)) FROM dbo.VisitTripSnapshots UNION ALL SELECT N'VisitTripSnapshotStops',COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(VisitTripSnapshotStopId,VisitTripSnapshotId,LocationId,LocationNameSnapshot)) FROM dbo.VisitTripSnapshotStops"
$historyBefore=@{}
foreach($row in @(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query $historySql -AbortOnError)){$historyBefore[$row.TableName]="$($row.RowTotal):$($row.Fingerprint)"}
$historySummary=@($historyBefore.GetEnumerator() | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Name,$_.Value }) -join ';'
Write-Output "R3_HISTORY_BASELINE=$historySummary"
$root=Join-Path $PSScriptRoot '../../docs/release/optionc-canonical-sql-draft'
$canonical=@('1800_011','1800_012','1800_014')
$chain=@('1800_015','1800_016','1800_017','1800_018','1800_019','1800_020')
$expected=@{'1800_011'='1.8.0-011';'1800_012'='1.8.0-012';'1800_014'='1.8.0-014';'1800_015'='1.8.0-015';'1800_016'='1.8.0-016';'1800_017'='1.8.0-017';'1800_018'='1.8.0-018';'1800_019'='1.8.0-019';'1800_020'='1.8.0-020'}
foreach($m in $canonical){
  $dir=Join-Path $root $m
  foreach($f in @('Preflight.sql','Up.sql')){$file=Join-Path $dir $f; Run "CANONICAL $m/$f" (Get-Content -LiteralPath $file -Raw) $file}
  if($m -eq '1800_014'){$file=Join-Path $dir 'Backfill.sql'; Run "$m/Backfill.sql" (Get-Content -LiteralPath $file -Raw) $file}
  $file=Join-Path $dir 'Verify.sql'; Run "CANONICAL $m/Verify.sql" (Get-Content -LiteralPath $file -Raw) $file
  $expectedCanonical=@{'1800_011'='1.8.0-011';'1800_012'='1.8.0-012';'1800_014'='1.8.0-014'}
  $actual=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC").VersionNumber
  if($actual -ne $expectedCanonical[$m]){throw "R3_CANONICAL_VERSION_MISMATCH_$m"}
  Write-Output "CANONICAL_MIGRATION_PASS $($expectedCanonical[$m])"
}
$canonicalBackfill=Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "IF (SELECT COUNT_BIG(*) FROM dbo.Locations WHERE IsActive=1 AND ApprovalStatus=N'Approved')<>(SELECT COUNT_BIG(*) FROM dbo.LocationPublishedVersions WHERE VersionNumber=1 AND IsCurrent=1 AND ChangeRequestId IS NULL) THROW 51980,N'CANONICAL_BACKFILL_COUNT_MISMATCH',1; IF EXISTS(SELECT 1 FROM dbo.LocationPublishedVersions WHERE VersionNumber=1 AND (PublishedJson=N'{}' OR ISJSON(PublishedJson)<>1 OR PublishedSha256<>HASHBYTES('SHA2_256',CONVERT(varbinary(max),PublishedJson)))) THROW 51981,N'CANONICAL_BACKFILL_JSON_HASH_INVALID',1; SELECT COUNT_BIG(*) AS Version1Count FROM dbo.LocationPublishedVersions WHERE VersionNumber=1" -AbortOnError
Write-Output "CANONICAL_VERSION1_BACKFILL_PASS rows=$($canonicalBackfill.Version1Count)"
$candidateRoot=Join-Path $PSScriptRoot '../../docs/release/optionc-canonical-sql-draft/candidate'
foreach($m in $chain){
  $dir=Join-Path $candidateRoot $m
  foreach($f in @('Preflight.sql','Up.sql')){$file=Join-Path $dir $f; Run "$m/$f" (Get-Content -LiteralPath $file -Raw) $file}
  if($m -eq '1800_014'){$file=Join-Path $dir 'Backfill.sql'; Run "$m/Backfill.sql" (Get-Content -LiteralPath $file -Raw) $file}
  $file=Join-Path $dir 'Verify.sql'; Run "$m/Verify.sql" (Get-Content -LiteralPath $file -Raw) $file
  $actual=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT MAX(VersionNumber) AS VersionNumber FROM dbo.SchemaVersions").VersionNumber
  if($actual -ne $expected[$m]){throw "R3_ADDON_FIXTURE_VERSION_MISMATCH_$m"}
  Write-Output "CANDIDATE_ADDON_FIXTURE_PASS $($expected[$m])"
}
try { $file=Join-Path $candidateRoot '1800_020/Preflight.sql'; Run 'candidate 1800_020/Preflight.sql rerun-negative' (Get-Content -LiteralPath $file -Raw) $file; throw 'R3_RERUN_UNEXPECTED_PASS' } catch { if($_.Exception.Message -match 'UNEXPECTED_PASS'){throw}; if($_.Exception.ToString() -notmatch '1\.8\.0-020_RERUN_STOP'){throw 'R3_RERUN_NEGATIVE_WRONG_FAILURE'}; Write-Output 'FAIL_CLOSED_RERUN_PASS' }
$count=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT COUNT(*) C FROM dbo.SchemaVersions').C
if($count -ne 10){throw "R3_FINAL_MARKER_COUNT_$count"}
try { Run 'unknown-commit-negative' "SET XACT_ABORT ON; BEGIN TRAN; INSERT dbo.SchemaVersions VALUES(N'1.8.0-021',N'injected failure',SYSUTCDATETIME(),N'test'); THROW 52999,N'R3_INJECTED_FAILURE_STOP',1; COMMIT"; throw 'R3_FAILURE_DID_NOT_STOP' } catch { if($_.Exception.Message -match 'DID_NOT_STOP'){throw}; if($_.Exception.ToString() -notmatch 'R3_INJECTED_FAILURE_STOP'){throw 'R3_FAILURE_INJECTION_WRONG_FAILURE'}; Write-Output 'FAIL_CLOSED_FAILURE_INJECTION_PASS' }
$unexpectedMarker=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT COUNT(*) C FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-021'").C
if($unexpectedMarker -ne 0){throw 'R3_UNKNOWN_COMMIT_MARKER_PERSISTED'}
$historyAfter=@{}
foreach($row in @(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query $historySql -AbortOnError)){$historyAfter[$row.TableName]="$($row.RowTotal):$($row.Fingerprint)"}
foreach($name in @('VisitTrips','VisitTripStops','VisitTripSnapshots','VisitTripSnapshotStops')){if($historyAfter[$name] -ne $historyBefore[$name]){throw "R3_HISTORY_FINGERPRINT_CHANGED_$name"}}
Write-Output "R3_HISTORY_FINGERPRINT_PASS tables=4"
try { Run 'partial-failure-negative' "SET XACT_ABORT ON; BEGIN TRAN; CREATE TABLE dbo.R3PartialFailureProbe(Id int NOT NULL); THROW 52998,N'R3_PARTIAL_FAILURE_STOP',1; COMMIT"; throw 'R3_PARTIAL_FAILURE_DID_NOT_STOP' } catch { if($_.Exception.Message -match 'DID_NOT_STOP'){throw}; if($_.Exception.ToString() -notmatch 'R3_PARTIAL_FAILURE_STOP'){throw 'R3_PARTIAL_FAILURE_WRONG_FAILURE'}; Write-Output 'FAIL_CLOSED_PARTIAL_FAILURE_PASS' }
$partialObject=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT COALESCE(OBJECT_ID(N'dbo.R3PartialFailureProbe',N'U'),0) AS ObjectId").ObjectId
if([int]$partialObject -ne 0){throw 'R3_PARTIAL_FAILURE_OBJECT_PERSISTED'}
Write-Output "R3_CANONICAL_CORE_PLUS_CANDIDATE_ADDON_REHEARSAL_PASS canonical=011,012,014 candidateAddons=015-020 final=$((Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC').VersionNumber) UAT_MIGRATION_STATUS=HOLD_010_BASELINE_AND_015_020_WRITER_COMPATIBILITY_NOT_PROVEN"
} finally { Invoke-Sqlcmd -ConnectionString $raw -Query "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]" -AbortOnError }
