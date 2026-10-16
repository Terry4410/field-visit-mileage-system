$ErrorActionPreference = 'Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
$database = 'OptionCIsolated_Fingerprint_' + [Guid]::NewGuid().ToString('N')
$master = $env:E1_R1_SQL_CONNECTION
if ([string]::IsNullOrWhiteSpace($master) -or $master -notmatch 'Server=localhost,1433' -or $master -notmatch 'Database=master') { throw 'Historical fingerprint fixture requires disposable loopback SQL master.' }
$builder = [Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($master)
$builder['Initial Catalog'] = $database; $builder['Pooling'] = $false
$connection = $builder.ConnectionString; $created = $false
$script = Join-Path $PSScriptRoot '../../docs/release/optionc-canonical-sql-draft/full-scheme-a/HistoricalFingerprint.readonly.sql'
function Invoke-Fingerprint([hashtable]$bounds,[string]$expectedDatabase=$database) {
  $variables = @("EXPECTED_SERVER=localhost","EXPECTED_DATABASE=$expectedDatabase","MAX_VISIT_TRIP_ID=$($bounds.Trips)","MAX_VISIT_TRIP_STOP_ID=$($bounds.Stops)","MAX_VISIT_TRIP_SNAPSHOT_ID=$($bounds.Snapshots)","MAX_VISIT_TRIP_SNAPSHOT_STOP_ID=$($bounds.SnapshotStops)")
  Invoke-Sqlcmd -ConnectionString $connection -InputFile $script -Variable $variables -AbortOnError -OutputAs DataSet -QueryTimeout 30
}
function Assert-Same($before,$after) {
  foreach($i in 0..3) { if ([int64]$before.Tables[$i].Rows[0].RowCount -ne [int64]$after.Tables[$i].Rows[0].RowCount -or [string]$before.Tables[$i].Rows[0].FingerprintSha256 -ne [string]$after.Tables[$i].Rows[0].FingerprintSha256) { throw "Historical fingerprint changed for result $i." } }
}
try {
  Invoke-Sqlcmd -ConnectionString $master -Query "CREATE DATABASE [$database]" -AbortOnError -QueryTimeout 30; $created = $true
  Invoke-Sqlcmd -ConnectionString $connection -AbortOnError -QueryTimeout 30 -Query @'
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) PRIMARY KEY);
CREATE TABLE dbo.SchemaMigrationDataBaselines(MigrationVersion nvarchar(40) PRIMARY KEY);
CREATE TABLE dbo.VisitTrips(VisitTripId bigint PRIMARY KEY,TripNo nvarchar(40),UserId int,OrganizationId int,TeamId int,Status nvarchar(30),VehicleType nvarchar(30),Purpose nvarchar(500),Notes nvarchar(500),ReturnReason nvarchar(500));
CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint PRIMARY KEY,VisitTripId bigint,StopSequence int,LocationId int,LocationNameSnapshot nvarchar(200),AddressSnapshot nvarchar(500));
CREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint PRIMARY KEY,VisitTripId bigint,SnapshotVersion int,SnapshotType nvarchar(30),ApprovedDistanceKmSnapshot decimal(10,2),SubsidyAmountSnapshot decimal(12,2),StatusSnapshot nvarchar(30));
CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint PRIMARY KEY,VisitTripSnapshotId bigint,StopSequence int,LocationId int,LocationNameSnapshot nvarchar(200),AddressSnapshot nvarchar(500));
INSERT dbo.SchemaVersions VALUES(N'1.8.0-001'); INSERT dbo.SchemaMigrationDataBaselines VALUES(N'1.8.0-001');
INSERT dbo.VisitTrips VALUES(1,N'T|1',7,1,1,N'Approved',N'Car',NULL,N'<NULL>',N'A:B');
INSERT dbo.VisitTripStops VALUES(2,1,1,100,N'Frozen|A',NULL);
INSERT dbo.VisitTripSnapshots VALUES(3,1,1,N'Approved',12.30,30.75,N'Approved');
INSERT dbo.VisitTripSnapshotStops VALUES(4,3,1,100,N'Frozen:A',N'Address|A');
'@
  $unbounded=@{Trips=[int64]::MaxValue;Stops=[int64]::MaxValue;Snapshots=[int64]::MaxValue;SnapshotStops=[int64]::MaxValue}; $capture=Invoke-Fingerprint $unbounded
  if($capture.Tables.Count -ne 5){throw "Expected five result sets; observed $($capture.Tables.Count)."}
  foreach($i in 0..3){if([int64]$capture.Tables[$i].Rows[0].RowCount -ne 1){throw "Missing fingerprint result $i."};if([string]::IsNullOrWhiteSpace([string]$capture.Tables[$i].Rows[0].FingerprintSha256)){throw "Missing hash $i."}}
  $bounds=@{Trips=[int64]$capture.Tables[0].Rows[0].ObservedHighWatermark;Stops=[int64]$capture.Tables[1].Rows[0].ObservedHighWatermark;Snapshots=[int64]$capture.Tables[2].Rows[0].ObservedHighWatermark;SnapshotStops=[int64]$capture.Tables[3].Rows[0].ObservedHighWatermark}
  Assert-Same $capture (Invoke-Fingerprint $bounds)
  Invoke-Sqlcmd -ConnectionString $connection -AbortOnError -Query @'
INSERT dbo.VisitTrips VALUES(11,N'new',7,1,1,N'Draft',N'Car',NULL,NULL,NULL); INSERT dbo.VisitTripStops VALUES(12,11,1,100,N'new',N'new'); INSERT dbo.VisitTripSnapshots VALUES(13,11,1,N'Submitted',NULL,NULL,N'Submitted'); INSERT dbo.VisitTripSnapshotStops VALUES(14,13,1,100,N'new',N'new');
'@
  Assert-Same $capture (Invoke-Fingerprint $bounds)
  Invoke-Sqlcmd -ConnectionString $connection -AbortOnError -Query "UPDATE dbo.VisitTripStops SET LocationNameSnapshot=N'Frozen|B' WHERE VisitTripStopId=2"
  $tampered=Invoke-Fingerprint $bounds
  if([int64]$tampered.Tables[1].Rows[0].RowCount -ne [int64]$capture.Tables[1].Rows[0].RowCount){throw 'Tamper fixture changed row count.'}
  if([string]$tampered.Tables[1].Rows[0].FingerprintSha256 -eq [string]$capture.Tables[1].Rows[0].FingerprintSha256){throw 'Frozen-value tamper was not detected.'}
  try { Invoke-Fingerprint $bounds 'WrongDatabase'; throw 'Wrong database allow-list unexpectedly passed.' } catch { if($_.Exception.Message -match 'unexpectedly passed'){throw} }
  Write-Output 'PASS historical fingerprint: 1.8.0-001 prerequisite, four high-water tables, NULL/delimiter serialization, constant-count tamper detection, exact DB allow-list'
} finally { if ($created) { Invoke-Sqlcmd -ConnectionString $master -Query "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]" -AbortOnError -QueryTimeout 30 } }
