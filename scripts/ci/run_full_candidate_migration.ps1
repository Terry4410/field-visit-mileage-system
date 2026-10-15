$ErrorActionPreference='Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
$raw=$env:E1_R1_SQL_CONNECTION
if([string]::IsNullOrWhiteSpace($raw)){throw 'R3_SQL_CONNECTION_MISSING'}
$b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($raw)
if($b.DataSource -notin @('localhost,1433','127.0.0.1,1433') -or $b.InitialCatalog -ne 'master' -or $b.UserID -ne 'sa'){throw 'R3_NONLOCAL_TARGET_DENIED'}
$db='OptionCIsolated_R3_'+[guid]::NewGuid().ToString('N')
function Conn([string]$catalog){$x=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($raw);$x['Initial Catalog']=$catalog;$x['Pooling']=$false;return $x.ConnectionString}
function Run([string]$sql){Invoke-Sqlcmd -ConnectionString (Conn $db) -Query $sql -AbortOnError -QueryTimeout 30}
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
$root=Join-Path $PSScriptRoot '../../docs/release/optionc-canonical-sql-draft/candidate'
$chain=@('1800_011','1800_012','1800_014','1800_015','1800_016','1800_017','1800_018','1800_019','1800_020')
$expected=@{'1800_011'='1.8.0-011';'1800_012'='1.8.0-012';'1800_014'='1.8.0-014';'1800_015'='1.8.0-015';'1800_016'='1.8.0-016';'1800_017'='1.8.0-017';'1800_018'='1.8.0-018';'1800_019'='1.8.0-019';'1800_020'='1.8.0-020'}
foreach($m in $chain){
  $dir=Join-Path $root $m
  foreach($f in @('Preflight.sql','Up.sql')){Run (Get-Content (Join-Path $dir $f) -Raw)}
  if($m -eq '1800_014'){Run (Get-Content (Join-Path $dir 'Backfill.sql') -Raw)}
  Run (Get-Content (Join-Path $dir 'Verify.sql') -Raw)
  $actual=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query "SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC").VersionNumber
  if($actual -ne $expected[$m]){throw "R3_VERSION_MISMATCH_$m"}
  Write-Output "MIGRATION_PASS $($expected[$m])"
}
try { Run (Get-Content (Join-Path $root '1800_020/Up.sql') -Raw); throw 'R3_RERUN_UNEXPECTED_PASS' } catch { if($_.Exception.Message -match 'UNEXPECTED_PASS'){throw}; Write-Output 'FAIL_CLOSED_RERUN_PASS' }
Run "INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,OperationCode,RiskCode,Status,RequestedByUserId,ProposedJson) VALUES(NEWID(),1,1,N'Location',N'10',N'Update',N'High',N'Pending',1,N'{}');"
Run "DECLARE @id bigint=(SELECT MAX(ChangeRequestId) FROM dbo.ChangeRequests); INSERT dbo.LeaderReviewDetails VALUES(@id,2,N'Approved',NEWID(),N'ok',SYSUTCDATETIME()); UPDATE dbo.ChangeRequests SET Status=N'Applied',ReviewerUserId=2,ReviewedAt=SYSUTCDATETIME(),AppliedAt=SYSUTCDATETIME() WHERE ChangeRequestId=@id; INSERT dbo.StageEvidenceAuditEvents VALUES(@id,N'GeocodeEvidenceRecorded',NEWID(),HASHBYTES('SHA2_256',N'address'),2,SYSUTCDATETIME());"
Run "INSERT dbo.CreateLowRiskContracts VALUES((SELECT MAX(ChangeRequestId) FROM dbo.ChangeRequests),1,NEWID(),1,SYSUTCDATETIME()); INSERT dbo.DirectLowRiskAudit VALUES(10,1,NEWID(),HASHBYTES('SHA2_256',N'before'),HASHBYTES('SHA2_256',N'after'),SYSUTCDATETIME()); INSERT dbo.LocationMergeDetails VALUES((SELECT MAX(ChangeRequestId) FROM dbo.ChangeRequests),10,11,NEWID(),SYSUTCDATETIME()); INSERT dbo.ExcelImportStatus VALUES(NEWID(),1,N'Applied',(SELECT MAX(ChangeRequestId) FROM dbo.ChangeRequests),NULL);"
$count=(Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT COUNT(*) C FROM dbo.SchemaVersions').C
if($count -ne 10){throw "R3_FINAL_MARKER_COUNT_$count"}
try { Run "BEGIN TRAN; INSERT dbo.SchemaVersions VALUES(N'1.8.0-021',N'injected failure',SYSUTCDATETIME(),N'test'); THROW 52999,N'R3_INJECTED_FAILURE_STOP',1; COMMIT"; throw 'R3_FAILURE_DID_NOT_STOP' } catch { if($_.Exception.Message -match 'DID_NOT_STOP'){throw}; Write-Output 'FAIL_CLOSED_FAILURE_INJECTION_PASS' }
$hist=Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT COUNT(*) RowCount FROM dbo.VisitTrips'
if($hist.RowCount -ne 1){throw 'R3_HISTORY_ROWCOUNT_CHANGED'}
Write-Output "R3_FULL_CHAIN_PASS final=$((Invoke-Sqlcmd -ConnectionString (Conn $db) -Query 'SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC').VersionNumber)"
} finally { Invoke-Sqlcmd -ConnectionString $raw -Query "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]" -AbortOnError }