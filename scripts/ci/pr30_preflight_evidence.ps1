Set-StrictMode -Version Latest

function Get-DataTables([object]$DataSet) {
  if ($DataSet -is [System.Data.DataSet]) {
    foreach ($table in $DataSet.Tables) {
      Write-Output -NoEnumerate $table
    }
    return
  }
  if ($DataSet -is [System.Data.DataTable]) {
    Write-Output -NoEnumerate $DataSet
    return
  }
  throw 'STOP: Invoke-Sqlcmd did not return a DataSet/DataTable.'
}

function Get-ColumnNames([System.Data.DataTable]$Table) {
  return @($Table.Columns | ForEach-Object { $_.ColumnName })
}

function Assert-SourceSha([string]$CheckedOutSha, [string]$WorkflowSha) {
  if ([string]::IsNullOrWhiteSpace($CheckedOutSha) -or [string]::IsNullOrWhiteSpace($WorkflowSha)) {
    throw 'STOP: source SHA evidence was unavailable.'
  }
  if ($CheckedOutSha -notmatch '^[0-9a-f]{40}$' -or $WorkflowSha -notmatch '^[0-9a-f]{40}$') {
    throw 'STOP: source SHA evidence was malformed.'
  }
  if ($CheckedOutSha -ne $WorkflowSha) {
    throw 'STOP: checked-out source SHA does not match workflow source SHA.'
  }
}

function Get-ApprovedColumnNames([string[]]$Columns) {
  $allowed = 'ServerName|DatabaseName|ApplicationName|ObservedUtc|ConnectedIdentity|ReadOnlyIdentity|VersionNumber|AppliedAt|MigrationPhase|CanonicalRequestTablesPresent|StageAndPublishedVersionsPresent|MergeDetailsPresent|CanonicalEvidenceAuditExtended|TableName|RowCount|ForeignKeyName|ParentSchema|ParentTable|ReferencedTable|IsNotTrusted|ConstraintName|is_not_trusted|definition|IndexName|is_unique|is_unique_constraint|is_disabled|IndexFingerprint|CatalogFingerprintSha256|OriginalLogin|ServerPrincipal|DatabaseUser|IsDbOwner|IsDbDdlAdmin|IsDbDatawriter|CanCreateTable|CanAlterAnyUser|CanInsertChangeRequests|CanUpdateChangeRequests|CanDeleteChangeRequests|CanInsertSchemaVersions|CanUpdateLocations|CheckName|Violations|Result|LatestSchemaVersion|CompletedUtc'
  return @($Columns | Where-Object { $_ -match "^($allowed)$" })
}

function Convert-EvidenceRow([System.Data.DataRow]$Row, [string[]]$Columns) {
  $record = [ordered]@{}
  $sensitive = @('ConnectedIdentity','ReadOnlyIdentity','OriginalLogin','ServerPrincipal','DatabaseUser')
  foreach ($name in @(Get-ApprovedColumnNames $Columns)) {
    $value = $Row[$name]
    if ($value -is [System.DBNull]) {
      $value = $null
    } elseif ($value -is [datetime]) {
      $value = $value.ToUniversalTime().ToString('o')
    } elseif ($sensitive -contains $name) {
      $value = '[REDACTED]'
    }
    $record[$name] = $value
  }
  return [pscustomobject]$record
}

function Get-ResultKind([string[]]$Columns) {
  $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($column in $Columns) { [void]$set.Add($column) }
  $candidates = @()
  if ($set.Contains('Result') -and $set.Contains('LatestSchemaVersion') -and $set.Contains('CompletedUtc')) { $candidates += 'completion' }
  if ($set.Contains('MigrationPhase') -and $set.Contains('CanonicalRequestTablesPresent')) { $candidates += 'summary' }
  if ($set.Contains('CatalogFingerprintSha256')) { $candidates += 'catalogFingerprint' }
  if ($set.Contains('OriginalLogin') -and $set.Contains('IsDbOwner') -and $set.Contains('CanUpdateLocations')) { $candidates += 'permissions' }
  if ($set.Contains('CheckName') -and $set.Contains('Violations')) { $candidates += 'integrity' }
  if ($set.Contains('VersionNumber')) { $candidates += 'schemaVersions' }
  if ($set.Contains('TableName') -and $set.Contains('RowCount')) { $candidates += 'rowCounts' }
  if ($set.Contains('ForeignKeyName') -and $set.Contains('IsNotTrusted')) { $candidates += 'foreignKeys' }
  if ($set.Contains('ConstraintName') -and $set.Contains('is_not_trusted')) { $candidates += 'checkConstraints' }
  if ($set.Contains('IndexName') -and $set.Contains('IndexFingerprint')) { $candidates += 'indexes' }
  if ($set.Contains('ServerName') -and $set.Contains('DatabaseName') -and $set.Contains('ObservedUtc')) { $candidates += 'identity' }
  if ($candidates.Count -gt 1) { throw 'STOP: conflicting result-set column signatures.' }
  if ($candidates.Count -eq 1) { return $candidates[0] }
  return 'unclassified'
}

function Convert-EvidenceTable([System.Data.DataTable]$Table, [switch]$AllowUnknown) {
  $columns = Get-ColumnNames $Table
  $kind = Get-ResultKind $columns
  if ($kind -eq 'unclassified' -and -not $AllowUnknown) {
    throw 'STOP: unrecognized SQL result-set columns; refusing to expose unclassified data.'
  }
  if ($kind -eq 'unclassified') {
    return [pscustomobject]@{
      Kind = 'unclassified'
      Columns = @()
      RowCount = $Table.Rows.Count
      Rows = @()
      Applicability = 'UnclassifiedDataSuppressed'
    }
  }
  $projectedColumns = @(Get-ApprovedColumnNames $columns)
  $rows = @($Table.Rows | ForEach-Object { Convert-EvidenceRow $_ $columns })
  $applicability = if ($rows.Count -eq 0) { 'NotApplicable' } else { 'Executed' }
  return [pscustomobject]@{
    Kind = $kind
    Columns = $projectedColumns
    RowCount = $rows.Count
    Rows = $rows
    Applicability = $applicability
  }
}

function Capture-Evidence([object]$DataSet, [string]$CheckedOutSha) {
  $tables = @(Get-DataTables $DataSet)
  if ($tables.Count -eq 0) { throw 'STOP: no SQL result sets were returned.' }
  $resultSets = @($tables | ForEach-Object { Convert-EvidenceTable $_ })
  if (@($resultSets | Where-Object { $_.Kind -eq 'unclassified' }).Count -gt 0) {
    throw 'STOP: unclassified result set was returned.'
  }

  $duplicateKinds = @($resultSets | Group-Object Kind | Where-Object { $_.Count -gt 1 -and $_.Name -ne 'integrity' })
  if ($duplicateKinds.Count -gt 0) {
    throw "STOP: duplicated result kind '$($duplicateKinds[0].Name)' was returned."
  }

  $mandatory = @('identity','schemaVersions','summary','catalogFingerprint','permissions','completion')
  $byKind = @{}
  foreach ($kind in $mandatory) {
    $matches = @($resultSets | Where-Object { $_.Kind -eq $kind })
    if ($matches.Count -ne 1) { throw "STOP: mandatory evidence result set '$kind' was missing or duplicated." }
    $byKind[$kind] = $matches[0]
    if ($matches[0].RowCount -lt 1) { throw "STOP: mandatory evidence result set '$kind' was empty." }
  }

  $identity = @($byKind['identity'].Rows)
  if ($identity.Count -ne 1) { throw 'STOP: identity evidence must contain exactly one row.' }
  foreach ($name in @('ServerName','DatabaseName','ObservedUtc')) {
    if (-not (@($identity[0].PSObject.Properties.Name) -contains $name) -or [string]::IsNullOrWhiteSpace([string]$identity[0].$name)) {
      throw "STOP: identity field '$name' was missing or empty."
    }
  }

  $schemaRows = @($byKind['schemaVersions'].Rows)
  if ($schemaRows.Count -lt 1) { throw 'STOP: schemaVersions evidence was missing or empty.' }
  $versionValues = @($schemaRows | ForEach-Object { [string]$_.VersionNumber })
  if (@($versionValues | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
    throw 'STOP: schemaVersions evidence contained an empty version.'
  }
  if (($versionValues | Select-Object -Unique).Count -ne $versionValues.Count) {
    throw 'STOP: schemaVersions evidence contained duplicate version rows.'
  }

  $summary = @($byKind['summary'].Rows)
  if ($summary.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$summary[0].MigrationPhase)) {
    throw 'STOP: summary evidence was missing or unusable.'
  }
  foreach ($name in @('CanonicalRequestTablesPresent','StageAndPublishedVersionsPresent','MergeDetailsPresent','CanonicalEvidenceAuditExtended')) {
    if (-not (@($summary[0].PSObject.Properties.Name) -contains $name) -or $null -eq $summary[0].$name) {
      throw "STOP: summary field '$name' was missing or unknown."
    }
  }

  $fingerprint = @($byKind['catalogFingerprint'].Rows)
  if ($fingerprint.Count -ne 1 -or [string]$fingerprint[0].CatalogFingerprintSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw 'STOP: catalog fingerprint evidence was missing or unusable.'
  }

  $permissions = @($byKind['permissions'].Rows)
  if ($permissions.Count -ne 1) { throw 'STOP: permission evidence must contain exactly one row.' }
  foreach ($name in @('IsDbOwner','IsDbDdlAdmin','IsDbDatawriter','CanCreateTable','CanAlterAnyUser','CanInsertChangeRequests','CanUpdateChangeRequests','CanDeleteChangeRequests','CanInsertSchemaVersions','CanUpdateLocations')) {
    if (-not (@($permissions[0].PSObject.Properties.Name) -contains $name) -or $null -eq $permissions[0].$name) {
      throw "STOP: permission field '$name' was missing or unknown."
    }
    if ($permissions[0].$name -notin @(0,1)) { throw "STOP: permission field '$name' was not boolean numeric evidence." }
  }

  $completion = @($byKind['completion'].Rows)
  if ($completion.Count -ne 1 -or $completion[0].Result -ne 'PREFLIGHT_COMPLETE_READ_ONLY' -or [string]::IsNullOrWhiteSpace([string]$completion[0].LatestSchemaVersion) -or [string]::IsNullOrWhiteSpace([string]$completion[0].CompletedUtc)) {
    throw 'STOP: completion marker was missing, invalid or incomplete.'
  }
  if ($completion[0].LatestSchemaVersion -ne $summary[0].MigrationPhase -or $versionValues -notcontains [string]$completion[0].LatestSchemaVersion) {
    throw 'STOP: schema version evidence is inconsistent across result sets.'
  }

  $integritySets = @($resultSets | Where-Object { $_.Kind -eq 'integrity' })
  foreach ($set in $integritySets) {
    foreach ($row in @($set.Rows)) {
      if ([string]::IsNullOrWhiteSpace([string]$row.CheckName) -or $null -eq $row.Violations) {
        throw 'STOP: integrity evidence was incomplete.'
      }
      if ([int]$row.Violations -ne 0) { throw "STOP: integrity violations detected for $($row.CheckName)." }
    }
  }

  return [pscustomobject]@{
    ResultSetCount = $resultSets.Count
    ResultSets = $resultSets
    CompletionMarker = $completion[0].Result
    SourceSha = $CheckedOutSha
    CapturedUtc = [DateTime]::UtcNow.ToString('o')
  }
}
