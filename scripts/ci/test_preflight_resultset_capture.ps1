$ErrorActionPreference = 'Stop'
Import-Module SqlServer -RequiredVersion 22.4.5.1

$server = 'localhost,1433'
$database = 'OptionCIsolated_PreflightCapture'
$password = 'FieldVisit_Recovery!Sql2022'
$masterArgs = @{
  ServerInstance = $server
  Database = 'master'
  Username = 'sa'
  Password = $password
  TrustServerCertificate = $true
  Encrypt = 'Optional'
  ConnectionTimeout = 15
  QueryTimeout = 30
  AbortOnError = $true
}
$dbArgs = @{
  ServerInstance = $server
  Database = $database
  Username = 'sa'
  Password = $password
  TrustServerCertificate = $true
  Encrypt = 'Optional'
  ApplicationIntent = 'ReadOnly'
  ConnectionTimeout = 15
  QueryTimeout = 30
  AbortOnError = $true
}

function Get-DataTables([object]$DataSet) {
  if ($DataSet -is [System.Data.DataSet]) {
    return @($DataSet.Tables | ForEach-Object { $_ })
  }
  if ($DataSet -is [System.Data.DataTable]) {
    return @($DataSet)
  }
  throw "STOP: Invoke-Sqlcmd did not return a DataSet/DataTable."
}

function Get-ColumnNames([System.Data.DataTable]$Table) {
  return @($Table.Columns | ForEach-Object { $_.ColumnName })
}

function Convert-Row([System.Data.DataRow]$Row, [string[]]$Columns) {
  $record = [ordered]@{}
  foreach ($name in $Columns) {
    $value = $Row[$name]
    if ($value -is [System.DBNull]) {
      $value = $null
    } elseif ($value -is [datetime]) {
      $value = $value.ToUniversalTime().ToString('o')
    } elseif ($name -match 'Identity|Login|Principal|User') {
      $value = '[REDACTED]'
    }
    $record[$name] = $value
  }
  return [pscustomobject]$record
}

function Convert-Table([System.Data.DataTable]$Table) {
  $columns = Get-ColumnNames $Table
  $rows = @($Table.Rows | ForEach-Object { Convert-Row $_ $columns })
  return [pscustomobject]@{
    Columns = $columns
    RowCount = $rows.Count
    Rows = $rows
  }
}

function Get-ResultKind([string[]]$Columns) {
  $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($column in $Columns) { [void]$set.Add($column) }
  if ($set.Contains('Result') -and $set.Contains('LatestSchemaVersion') -and $set.Contains('CompletedUtc')) { return 'completion' }
  if ($set.Contains('MigrationPhase') -and $set.Contains('CanonicalRequestTablesPresent')) { return 'summary' }
  if ($set.Contains('CatalogFingerprintSha256')) { return 'catalogFingerprint' }
  if ($set.Contains('OriginalLogin') -and $set.Contains('IsDbOwner') -and $set.Contains('CanUpdateLocations')) { return 'permissions' }
  if ($set.Contains('CheckName') -and $set.Contains('Violations')) { return 'integrity' }
  if ($set.Contains('VersionNumber')) { return 'schemaVersions' }
  if ($set.Contains('TableName') -and $set.Contains('RowCount')) { return 'rowCounts' }
  if ($set.Contains('ForeignKeyName') -and $set.Contains('IsNotTrusted')) { return 'foreignKeys' }
  if ($set.Contains('ConstraintName') -and $set.Contains('is_not_trusted')) { return 'checkConstraints' }
  if ($set.Contains('IndexName') -and $set.Contains('IndexFingerprint')) { return 'indexes' }
  if ($set.Contains('ServerName') -and $set.Contains('DatabaseName') -and $set.Contains('ObservedUtc')) { return 'identity' }
  return 'unclassified'
}

function Capture-Evidence([object]$DataSet) {
  $tables = Get-DataTables $DataSet
  $resultSets = @()
  foreach ($table in $tables) {
    $tableEvidence = Convert-Table $table
    $kind = Get-ResultKind $tableEvidence.Columns
    $resultSets += [pscustomobject]@{
      Kind = $kind
      Columns = $tableEvidence.Columns
      RowCount = $tableEvidence.RowCount
      Rows = $tableEvidence.Rows
    }
  }
  $mandatory = @('identity','summary','catalogFingerprint','permissions','completion')
  $present = @($resultSets | ForEach-Object { $_.Kind } | Select-Object -Unique)
  foreach ($kind in $mandatory) {
    if ($present -notcontains $kind) { throw "STOP: mandatory evidence result set '$kind' was not observed." }
  }
  $completion = @($resultSets | Where-Object { $_.Kind -eq 'completion' } | ForEach-Object { $_.Rows })
  if (@($completion).Count -ne 1 -or $completion[0].Result -ne 'PREFLIGHT_COMPLETE_READ_ONLY') {
    throw 'STOP: completion marker was missing or invalid.'
  }
  return [pscustomobject]@{
    ResultSetCount = $resultSets.Count
    ResultSets = $resultSets
    CompletionMarker = $completion[0].Result
    SourceSha = $env:GITHUB_SHA
    CapturedUtc = [DateTime]::UtcNow.ToString('o')
  }
}

$fixture = @'
SELECT N'sql-fieldvisit-jpe-uat' AS ServerName,
       N'OptionCIsolated_PreflightCapture' AS DatabaseName,
       N'principal@example.invalid' AS ReadOnlyIdentity,
       N'isolated-test' AS ApplicationName,
       SYSUTCDATETIME() AS ObservedUtc;
SELECT N'1.8.0-014' AS VersionNumber,
       SYSUTCDATETIME() AS AppliedAt,
       N'isolated' AS AppliedBy;
SELECT N'CurrentPublishedVersionDuplicates' AS CheckName, 0 AS Violations;
SELECT N'SchemaVersions' AS TableName, 1 AS RowCount;
SELECT N'FK_Test' AS ForeignKeyName, N'dbo' AS ParentSchema,
       N'ChangeRequests' AS ParentTable, N'ChangeRequestEvents' AS ReferencedTable,
       CAST(0 AS bit) AS IsNotTrusted;
SELECT N'CK_Test' AS ConstraintName, N'ChangeRequests' AS TableName,
       CAST(0 AS bit) AS is_not_trusted, N'CHECK (1=1)' AS definition;
SELECT N'IX_Test' AS IndexName, N'ChangeRequests' AS TableName,
       CAST(1 AS bit) AS is_unique, CAST(0 AS bit) AS is_unique_constraint,
       CAST(0 AS bit) AS is_disabled, CONVERT(varchar(64),HASHBYTES('SHA2_256',N'ix'),2) AS IndexFingerprint;
SELECT N'1.8.0-014' AS MigrationPhase,
       CAST(1 AS bit) AS CanonicalRequestTablesPresent,
       CAST(1 AS bit) AS StageAndPublishedVersionsPresent,
       CAST(0 AS bit) AS MergeDetailsPresent,
       CAST(1 AS bit) AS CanonicalEvidenceAuditExtended;
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',N'catalog'),2) AS CatalogFingerprintSha256;
SELECT N'OriginalLogin' AS OriginalLogin,
       N'ServerPrincipal' AS ServerPrincipal,
       N'DatabaseUser' AS DatabaseUser,
       CAST(0 AS int) AS IsDbOwner,
       CAST(0 AS int) AS IsDbDdlAdmin,
       CAST(0 AS int) AS IsDbDatawriter,
       CAST(0 AS int) AS CanCreateTable,
       CAST(0 AS int) AS CanAlterAnyUser,
       CAST(0 AS int) AS CanInsertChangeRequests,
       CAST(0 AS int) AS CanUpdateChangeRequests,
       CAST(0 AS int) AS CanDeleteChangeRequests,
       CAST(0 AS int) AS CanInsertSchemaVersions,
       CAST(0 AS int) AS CanUpdateLocations;
SELECT N'PREFLIGHT_COMPLETE_READ_ONLY' AS Result,
       N'sql-fieldvisit-jpe-uat' AS ServerName,
       N'OptionCIsolated_PreflightCapture' AS DatabaseName,
       N'1.8.0-014' AS LatestSchemaVersion,
       SYSUTCDATETIME() AS CompletedUtc;
'@
$fixturePath = Join-Path $env:RUNNER_TEMP 'preflight-resultsets.sql'
$fixture | Set-Content -Path $fixturePath -Encoding utf8

$stopFixture = @'
THROW 51027, 'STOP: unauthorized Migration 013 marker is present.', 1;
SELECT N'PREFLIGHT_COMPLETE_READ_ONLY' AS Result;
'@
$stopPath = Join-Path $env:RUNNER_TEMP 'preflight-stop.sql'
$stopFixture | Set-Content -Path $stopPath -Encoding utf8

try {
  Invoke-Sqlcmd @masterArgs -Query "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database];"
  $ready = $false
  for ($i = 0; $i -lt 30; $i++) {
    try {
      $dbId = Invoke-Sqlcmd @masterArgs -Query "SELECT DB_ID(N'$database') AS DatabaseId;"
      if ($null -ne $dbId.DatabaseId) { $ready = $true; break }
    } catch {}
    Start-Sleep -Seconds 1
  }
  if (-not $ready) { throw 'STOP: disposable database did not become available.' }

  $captured = Invoke-Sqlcmd @dbArgs -Variable @{ EXPECTED_SERVER='localhost,1433'; EXPECTED_DATABASE=$database } -InputFile $fixturePath -OutputAs DataSet
  $evidence = Capture-Evidence $captured
  if ($evidence.CompletionMarker -ne 'PREFLIGHT_COMPLETE_READ_ONLY') { throw 'STOP: successful capture lacked completion marker.' }
  if (@($evidence.ResultSets | Where-Object Kind -eq 'integrity').Count -ne 1) { throw 'STOP: conditional integrity result set was not captured.' }
  $permissionRows = @($evidence.ResultSets | Where-Object Kind -eq 'permissions' | ForEach-Object Rows)
  if ($permissionRows[0].IsDbOwner -ne 0 -or $permissionRows[0].CanUpdateLocations -ne 0) { throw 'STOP: permission 0 values were not preserved.' }

  $missingCaught = $false
  try {
    Invoke-Sqlcmd @dbArgs -InputFile $fixturePath -OutputAs DataSet
  } catch {
    $missingCaught = $true
  }
  if (-not $missingCaught) { throw 'STOP: missing SQLCMD variables did not fail closed.' }

  $stopCaught = $false
  try {
    Invoke-Sqlcmd @dbArgs -InputFile $stopPath -OutputAs DataSet
  } catch {
    if ($_.Exception.Message -notmatch '51027|unauthorized Migration 013') { throw }
    $stopCaught = $true
  }
  if (-not $stopCaught) { throw 'STOP: expected SQL STOP was not surfaced.' }

  Write-Output ($evidence | ConvertTo-Json -Depth 20 -Compress)
  Write-Output '{"RuntimeAssertions":{"OutputAs":"DataSet","SqlServerModule":"22.4.5.1","MissingVariablesFailClosed":true,"StopDoesNotComplete":true,"SensitiveIdentityRedacted":true}}'
} finally {
  try { Invoke-Sqlcmd @masterArgs -Query "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END" } catch {}
}
