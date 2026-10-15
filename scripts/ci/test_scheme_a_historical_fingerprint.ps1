$ErrorActionPreference = 'Stop'

$database = 'OptionCIsolated_Fingerprint_' + [Guid]::NewGuid().ToString('N')
$master = $env:E1_R1_SQL_CONNECTION
if ([string]::IsNullOrWhiteSpace($master) -or $master -notmatch 'Server=localhost,1433' -or $master -notmatch 'Database=master') {
    throw 'Historical fingerprint fixture requires the disposable local SQL Server master connection.'
}

$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.ConnectionString = $master
$builder['Database'] = $database
$fixtureConnection = $builder.ConnectionString
$created = $false

try {
    Invoke-Sqlcmd -ConnectionString $master -Query "CREATE DATABASE [$database]" -AbortOnError -QueryTimeout 30
    $created = $true
    $schema = @'
CREATE TABLE dbo.VisitTripStops(
 VisitTripStopId bigint NOT NULL PRIMARY KEY, VisitTripId bigint NOT NULL,
 StopSequence int NOT NULL, LocationId int NULL,
 LocationNameSnapshot nvarchar(200) NULL, AddressSnapshot nvarchar(500) NULL);
CREATE TABLE dbo.VisitTripSnapshots(
 VisitTripSnapshotId bigint NOT NULL PRIMARY KEY, VisitTripId bigint NOT NULL,
 SnapshotVersion int NOT NULL, SnapshotType nvarchar(30) NOT NULL,
 ApprovedDistanceKmSnapshot decimal(10,2) NULL,
 SubsidyAmountSnapshot decimal(12,2) NULL, StatusSnapshot nvarchar(30) NOT NULL);
CREATE TABLE dbo.VisitTripSnapshotStops(
 VisitTripSnapshotStopId bigint NOT NULL PRIMARY KEY,
 VisitTripSnapshotId bigint NOT NULL, StopSequence int NOT NULL,
 LocationId int NULL, LocationNameSnapshot nvarchar(200) NULL,
 AddressSnapshot nvarchar(500) NULL);
INSERT dbo.VisitTripStops VALUES(1,10,1,100,N'Frozen A',N'Address A');
INSERT dbo.VisitTripSnapshots VALUES(2,10,1,N'Approved',12.30,30.75,N'Approved');
INSERT dbo.VisitTripSnapshotStops VALUES(3,2,1,100,N'Frozen A',N'Address A');
'@
    Invoke-Sqlcmd -ConnectionString $fixtureConnection -Query $schema -AbortOnError -QueryTimeout 30

    $before = Invoke-Sqlcmd -ConnectionString $fixtureConnection -Query @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.VisitTripStops) AS TripStops,
 (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots) AS Snapshots,
 (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops) AS SnapshotStops;
'@ -AbortOnError -QueryTimeout 30

    $path = Join-Path $PSScriptRoot '../../docs/release/optionc-canonical-sql-draft/full-scheme-a/HistoricalFingerprint.readonly.sql'
    $data = Invoke-Sqlcmd -ConnectionString $fixtureConnection -InputFile $path -AbortOnError -OutputAs DataSet -QueryTimeout 30
    $tables = @($data.Tables)
    if ($tables.Count -ne 4) { throw "Expected four fingerprint result sets; observed $($tables.Count)." }
    foreach ($index in 0..2) {
        if ($tables[$index].Rows.Count -ne 1) { throw "Fingerprint result $index is missing." }
        if ([string]::IsNullOrWhiteSpace([string]$tables[$index].Rows[0].FingerprintSha256)) {
            throw "Fingerprint result $index has no SHA-256."
        }
        if ([int64]$tables[$index].Rows[0].RowCount -ne 1) { throw "Fingerprint result $index row count drift." }
    }
    if ([string]$tables[3].Rows[0].Result -ne 'HISTORICAL_FINGERPRINT_COMPLETE_READ_ONLY') {
        throw 'Historical fingerprint completion marker is invalid.'
    }

    $after = Invoke-Sqlcmd -ConnectionString $fixtureConnection -Query @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.VisitTripStops) AS TripStops,
 (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots) AS Snapshots,
 (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops) AS SnapshotStops;
'@ -AbortOnError -QueryTimeout 30
    foreach ($column in 'TripStops','Snapshots','SnapshotStops') {
        if ([int64]$before.$column -ne [int64]$after.$column) { throw "Read-only fingerprint mutated $column." }
    }
    Write-Output 'PASS historical fingerprint SELECT-only disposable runtime'
}
finally {
    if ($created) {
        Invoke-Sqlcmd -ConnectionString $master -Query "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]" -AbortOnError -QueryTimeout 30
    }
}
