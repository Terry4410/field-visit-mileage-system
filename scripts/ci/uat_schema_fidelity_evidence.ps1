Set-StrictMode -Version Latest

function Get-UatInventoryTables([object]$DataSet) {
  if ($DataSet -is [System.Data.DataSet]) {
    return @($DataSet.Tables | ForEach-Object { Write-Output -NoEnumerate $_ })
  }
  throw 'STOP: schema inventory did not return a DataSet.'
}

function Get-UatInventoryColumnNames([System.Data.DataTable]$Table) {
  return @($Table.Columns | ForEach-Object { $_.ColumnName })
}

function Assert-UatInventoryColumns(
  [System.Data.DataTable]$Table,
  [string]$Kind,
  [string[]]$Expected
) {
  $actual = @(Get-UatInventoryColumnNames $Table)
  if ($actual.Count -ne $Expected.Count) {
    throw "STOP: $Kind result columns were missing or unexpected."
  }
  for ($index = 0; $index -lt $Expected.Count; $index++) {
    if ($actual[$index] -cne $Expected[$index]) {
      throw "STOP: $Kind result columns were missing, reordered or unexpected."
    }
  }
}

function Convert-UatInventoryValue([object]$Value) {
  if ($Value -is [System.DBNull]) { return $null }
  if ($Value -is [datetime]) { return $Value.ToUniversalTime().ToString('o') }
  if ($Value -is [byte[]]) { return [Convert]::ToHexString($Value) }
  return $Value
}

function Convert-UatInventoryRows([System.Data.DataTable]$Table) {
  $columns = @(Get-UatInventoryColumnNames $Table)
  return @($Table.Rows | ForEach-Object {
    $row = [ordered]@{}
    foreach ($column in $columns) {
      $row[$column] = Convert-UatInventoryValue $_[$column]
    }
    [pscustomobject]$row
  })
}

function Assert-NonEmptyMetadata([System.Data.DataTable]$Table, [string]$Kind) {
  if ($Table.Rows.Count -lt 1) { throw "STOP: $Kind metadata was empty." }
}

function Convert-UatSchemaInventory(
  [object]$DataSet,
  [string]$ExpectedServer,
  [string]$ExpectedDatabase,
  [string]$SourceSha,
  [string]$SqlSha256
) {
  if ($SourceSha -notmatch '^[0-9a-f]{40}$') { throw 'STOP: source SHA evidence was malformed.' }
  if ($SqlSha256 -notmatch '^[0-9a-f]{64}$') { throw 'STOP: SQL SHA-256 evidence was malformed.' }

  $tables = @(Get-UatInventoryTables $DataSet)
  if ($tables.Count -ne 7) { throw 'STOP: schema inventory returned an unexpected result-set count.' }

  $signatures = @(
    @('ServerName','DatabaseName','EvidenceMode'),
    @('SchemaVersionCount','LatestVersion'),
    @('SchemaName','TableName','Ordinal','ColumnName','SqlType','MaxLengthBytesOrCharacters','NumericPrecision','NumericScale','IsNullable','IsIdentity','IsComputed','IsRowVersion','DefaultDefinition'),
    @('SchemaName','TableName','IndexName','IndexType','IsPrimaryKey','IsUniqueConstraint','IsUnique','IsDisabled','HasFilter','FilterDefinition','KeyOrdinal','IsIncluded','ColumnName','IsDescending'),
    @('SchemaName','TableName','ForeignKeyName','ParentColumn','ReferencedSchema','ReferencedTable','ReferencedColumn','ColumnOrdinal','DeleteAction','UpdateAction','IsDisabled','IsNotTrusted'),
    @('SchemaName','TableName','CheckConstraintName','Definition','IsDisabled','IsNotTrusted'),
    @('HistoricalTable','RowCount','HighWatermark')
  )
  $kinds = @('identity','schemaVersion','columns','indexes','foreignKeys','checks','historicalAggregates')
  for ($index = 0; $index -lt $tables.Count; $index++) {
    Assert-UatInventoryColumns $tables[$index] $kinds[$index] $signatures[$index]
  }

  foreach ($index in 0,1,2,3,4,5,6) { Assert-NonEmptyMetadata $tables[$index] $kinds[$index] }
  if ($tables[0].Rows.Count -ne 1) { throw 'STOP: target identity result must contain exactly one row.' }
  $identity = $tables[0].Rows[0]
  $actualServer = [string]$identity['ServerName']
  if ($actualServer -ne $ExpectedServer -and $actualServer -ne ($ExpectedServer -replace '\.database\.windows\.net$','')) {
    throw 'STOP: schema inventory server identity did not match the fixed target.'
  }
  if ([string]$identity['DatabaseName'] -ne $ExpectedDatabase) { throw 'STOP: schema inventory database identity did not match the fixed target.' }
  if ([string]$identity['EvidenceMode'] -ne 'METADATA_ONLY_NO_ROW_VALUES') { throw 'STOP: schema inventory evidence mode was invalid.' }

  if ($tables[1].Rows.Count -ne 1) { throw 'STOP: schema version result must contain exactly one row.' }
  $version = $tables[1].Rows[0]
  if ([int64]$version['SchemaVersionCount'] -lt 1) { throw 'STOP: SchemaVersions was empty.' }
  if ([string]$version['LatestVersion'] -ne '1.8.0-010') { throw 'STOP: UAT is not at the exact reviewed 1.8.0-010 predecessor.' }

  $expectedHistorical = @('VisitTrips','VisitTripStops','VisitTripSnapshots','VisitTripSnapshotStops')
  if ($tables[6].Rows.Count -ne $expectedHistorical.Count) { throw 'STOP: historical aggregate result did not contain exactly four rows.' }
  $seen = @($tables[6].Rows | ForEach-Object { [string]$_['HistoricalTable'] })
  if ((@($seen | Sort-Object) -join '|') -ne (@($expectedHistorical | Sort-Object) -join '|')) {
    throw 'STOP: historical aggregate table names were missing or unexpected.'
  }
  foreach ($row in $tables[6].Rows) {
    if ([int64]$row['RowCount'] -lt 0) { throw 'STOP: historical aggregate row count was invalid.' }
  }

  return [pscustomobject][ordered]@{
    ContractVersion = 'uat-schema-fidelity-v1'
    SourceSha = $SourceSha
    SqlSha256 = $SqlSha256
    Target = [pscustomobject][ordered]@{
      ServerName = $actualServer
      DatabaseName = [string]$identity['DatabaseName']
      EvidenceMode = [string]$identity['EvidenceMode']
    }
    SchemaVersion = [pscustomobject][ordered]@{
      Count = [int64]$version['SchemaVersionCount']
      Latest = [string]$version['LatestVersion']
    }
    Columns = @(Convert-UatInventoryRows $tables[2])
    Indexes = @(Convert-UatInventoryRows $tables[3])
    ForeignKeys = @(Convert-UatInventoryRows $tables[4])
    Checks = @(Convert-UatInventoryRows $tables[5])
    HistoricalAggregates = @(Convert-UatInventoryRows $tables[6])
    CapturedUtc = [DateTime]::UtcNow.ToString('o')
  }
}

function Write-UatSchemaInventory(
  [object]$DataSet,
  [string]$ExpectedServer,
  [string]$ExpectedDatabase,
  [string]$SourceSha,
  [string]$SqlSha256,
  [string]$OutputPath
) {
  $evidence = Convert-UatSchemaInventory $DataSet $ExpectedServer $ExpectedDatabase $SourceSha $SqlSha256
  $json = $evidence | ConvertTo-Json -Depth 20
  [System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))
  return [pscustomobject]@{
    ContractVersion = $evidence.ContractVersion
    SourceSha = $evidence.SourceSha
    SqlSha256 = $evidence.SqlSha256
    LatestVersion = $evidence.SchemaVersion.Latest
    ColumnCount = $evidence.Columns.Count
    IndexColumnCount = $evidence.Indexes.Count
    ForeignKeyColumnCount = $evidence.ForeignKeys.Count
    CheckCount = $evidence.Checks.Count
    HistoricalAggregateCount = $evidence.HistoricalAggregates.Count
  }
}
