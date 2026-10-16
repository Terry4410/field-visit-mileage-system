Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'uat_schema_fidelity_evidence.ps1')

$script:Passed = 0
$script:Failed = 0

function New-InventoryTable([string]$Name, [string[]]$Columns) {
  $table = [System.Data.DataTable]::new($Name)
  foreach ($column in $Columns) { [void]$table.Columns.Add($column) }
  return ,$table
}

function Add-InventoryRow([System.Data.DataTable]$Table, [object[]]$Values) {
  [void]$Table.Rows.Add($Values)
}

function New-ValidInventoryDataSet {
  $data = [System.Data.DataSet]::new('UatSchemaFidelity')

  $identity = New-InventoryTable 'identity' @('ServerName','DatabaseName','EvidenceMode')
  Add-InventoryRow $identity @('sql-fieldvisit-jpe-uat','db-fieldvisit-uat','METADATA_ONLY_NO_ROW_VALUES')

  $version = New-InventoryTable 'schemaVersion' @('SchemaVersionCount','LatestVersion')
  Add-InventoryRow $version @('10','1.8.0-010')

  $columns = New-InventoryTable 'columns' @('SchemaName','TableName','Ordinal','ColumnName','SqlType','MaxLengthBytesOrCharacters','NumericPrecision','NumericScale','IsNullable','IsIdentity','IsComputed','IsRowVersion','DefaultDefinition')
  Add-InventoryRow $columns @('dbo','RouteCalculationAttempts','1','RouteCalculationAttemptId','bigint','8','19','0','False','True','False','False',$null)

  $indexes = New-InventoryTable 'indexes' @('SchemaName','TableName','IndexName','IndexType','IsPrimaryKey','IsUniqueConstraint','IsUnique','IsDisabled','HasFilter','FilterDefinition','KeyOrdinal','IsIncluded','ColumnName','IsDescending')
  Add-InventoryRow $indexes @('dbo','RouteCalculationAttempts','PK_RouteCalculationAttempts','CLUSTERED','True','False','True','False','False',$null,'1','False','RouteCalculationAttemptId','False')

  $foreignKeys = New-InventoryTable 'foreignKeys' @('SchemaName','TableName','ForeignKeyName','ParentColumn','ReferencedSchema','ReferencedTable','ReferencedColumn','ColumnOrdinal','DeleteAction','UpdateAction','IsDisabled','IsNotTrusted')
  Add-InventoryRow $foreignKeys @('dbo','RouteCalculationAttempts','FK_RouteCalculationAttempts_VisitTrips','VisitTripId','dbo','VisitTrips','VisitTripId','1','NO_ACTION','NO_ACTION','False','False')

  $checks = New-InventoryTable 'checks' @('SchemaName','TableName','CheckConstraintName','Definition','IsDisabled','IsNotTrusted')
  Add-InventoryRow $checks @('dbo','RouteCalculationAttempts','CK_RouteCalculationAttempts_Reason',"CalculationReason IN ('VisitorCalculate','LeaderRetry','CorrectionRecalculate','BackgroundMileageJob')",'False','False')

  $historical = New-InventoryTable 'historicalAggregates' @('HistoricalTable','RowCount','HighWatermark')
  Add-InventoryRow $historical @('VisitTrips','3','3')
  Add-InventoryRow $historical @('VisitTripStops','4','4')
  Add-InventoryRow $historical @('VisitTripSnapshots','2','2')
  Add-InventoryRow $historical @('VisitTripSnapshotStops','3','3')

  foreach ($table in @($identity,$version,$columns,$indexes,$foreignKeys,$checks,$historical)) {
    [void]$data.Tables.Add($table)
  }
  return ,$data
}

function Run-Test([string]$Name, [scriptblock]$Body) {
  try {
    & $Body
    Write-Output "PASS $Name"
    $script:Passed++
  } catch {
    Write-Output "FAIL $Name :: $($_.Exception.Message)"
    $script:Failed++
  }
}

function Assert-Throws([string]$Name, [string]$MessagePattern, [scriptblock]$Body) {
  try {
    & $Body
  } catch {
    if ($_.Exception.Message -notmatch $MessagePattern) {
      throw "$Name threw an unexpected sanitized message: $($_.Exception.Message)"
    }
    return
  }
  throw "$Name did not fail closed."
}

Run-Test 'valid seven-result-set DataSet normalizes' {
  $result = Convert-UatSchemaInventory (New-ValidInventoryDataSet) 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  if ($result.ContractVersion -ne 'uat-schema-fidelity-v1') { throw 'contract version mismatch' }
  if ($result.Columns.Count -ne 1 -or $result.HistoricalAggregates.Count -ne 4) { throw 'normalized counts mismatch' }
}

Run-Test 'wrong server identity fails before evidence output' {
  $data = (New-ValidInventoryDataSet).Copy()
  $data.Tables[0].Rows[0]['ServerName'] = 'wrong-server'
  Assert-Throws 'wrong server' 'server identity did not match' {
    Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  }
}

Run-Test 'unexpected extra column fails closed' {
  $data = (New-ValidInventoryDataSet).Copy()
  [void]$data.Tables[2].Columns.Add('UnexpectedBusinessValue')
  Assert-Throws 'extra column' 'missing or unexpected' {
    Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  }
}

Run-Test 'wrong schema version fails closed' {
  $data = (New-ValidInventoryDataSet).Copy()
  $data.Tables[1].Rows[0]['LatestVersion'] = '1.8.0-009'
  Assert-Throws 'wrong version' 'exact reviewed 1.8.0-010' {
    Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  }
}

Run-Test 'missing historical aggregate fails closed' {
  $data = (New-ValidInventoryDataSet).Copy()
  $data.Tables[6].Rows.RemoveAt(3)
  Assert-Throws 'missing historical aggregate' 'exactly four rows' {
    Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  }
}

Run-Test 'missing seventh result set fails closed' {
  $data = (New-ValidInventoryDataSet).Copy()
  $data.Tables.RemoveAt(6)
  Assert-Throws 'six result sets' 'unexpected result-set count' {
    Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  }
}

Run-Test 'SQL failure output suppresses raw secret and query text' {
  $secret = 'Password=must-not-leak; SELECT PrivateValue FROM dbo.Secret'
  $safe = Get-SanitizedUatInventoryFailure ([System.Exception]::new($secret))
  if ($safe -match 'must-not-leak|PrivateValue|dbo\.Secret') { throw 'raw failure detail leaked' }
  if ($safe -notmatch 'raw SQL error suppressed') { throw 'sanitized marker missing' }
  Assert-Throws 'sanitized throw' 'Classification=UnknownSqlFailure' {
    Throw-SanitizedUatInventoryFailure ([System.Exception]::new($secret))
  }
}

Run-Test 'wrong evidence mode fails closed' {
  $data = (New-ValidInventoryDataSet).Copy()
  $data.Tables[0].Rows[0]['EvidenceMode'] = 'ROW_DATA'
  Assert-Throws 'wrong evidence mode' 'evidence mode was invalid' {
    Convert-UatSchemaInventory $data 'sql-fieldvisit-jpe-uat.database.windows.net' 'db-fieldvisit-uat' ('a' * 40) ('b' * 64)
  }
}

Write-Output "PowerShell DataSet contract tests: $script:Passed passed, $script:Failed failed"
if ($script:Failed -ne 0) { exit 1 }
