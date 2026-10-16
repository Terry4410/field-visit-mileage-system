Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'uat_schema_fidelity_evidence.ps1')

Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop

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

function New-SyntheticSqlException([int]$Number, [byte]$State = 1, [byte]$Class = 16) {
  $errorType = [Microsoft.Data.SqlClient.SqlError]
  $collectionType = [Microsoft.Data.SqlClient.SqlErrorCollection]
  $exceptionType = [Microsoft.Data.SqlClient.SqlException]
  $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'

  $errorConstructor = @($errorType.GetConstructors($flags) | Sort-Object { $_.GetParameters().Count } -Descending)[0]
  $errorArguments = @($errorConstructor.GetParameters() | ForEach-Object {
    if ($_.Name -match 'infoNumber|number') { $Number }
    elseif ($_.Name -match 'errorState|state') { $State }
    elseif ($_.Name -match 'errorClass|class') { $Class }
    elseif ($_.ParameterType -eq [string] -and $_.Name -match 'message') { 'SYNTHETIC_SECRET_MUST_NOT_LEAK' }
    elseif ($_.ParameterType -eq [string]) { '' }
    elseif ($_.ParameterType -eq [int]) { 0 }
    elseif ($_.ParameterType -eq [uint32]) { [uint32]0 }
    elseif ($_.ParameterType -eq [byte]) { [byte]0 }
    elseif ($_.ParameterType -eq [System.Exception]) { $null }
    else { $null }
  })
  $sqlError = $errorConstructor.Invoke($errorArguments)

  $collection = [Activator]::CreateInstance($collectionType, $true)
  $addMethod = $collectionType.GetMethod('Add', $flags)
  [void]$addMethod.Invoke($collection, @($sqlError))

  $createMethod = @($exceptionType.GetMethods([System.Reflection.BindingFlags]'Static,NonPublic') |
    Where-Object { $_.Name -eq 'CreateException' -and $_.GetParameters()[0].ParameterType -eq $collectionType } |
    Sort-Object { $_.GetParameters().Count })[0]
  $createParameters = @($createMethod.GetParameters())
  $createArguments = [object[]]::new($createParameters.Count)
  for ($index = 0; $index -lt $createParameters.Count; $index++) {
    $parameter = $createParameters[$index]
    if ($parameter.ParameterType -eq $collectionType) { $createArguments[$index] = [object]$collection }
    elseif ($parameter.ParameterType -eq [string]) { $createArguments[$index] = 'synthetic-version' }
    elseif ($parameter.ParameterType -eq [Guid]) { $createArguments[$index] = [Guid]::Empty }
    elseif ($parameter.ParameterType -eq [System.Exception]) { $createArguments[$index] = $null }
    elseif ($parameter.ParameterType -eq [bool]) { $createArguments[$index] = $false }
    else { $createArguments[$index] = $null }
  }
  return $createMethod.Invoke($null, $createArguments)
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

Run-Test 'real SqlException signals map only to allow-listed diagnostics' {
  $cases = @(
    @{ Number = 51990; Classification = 'TargetGuardRejected' },
    @{ Number = 51992; Classification = 'RequiredObjectMissing' },
    @{ Number = 51993; Classification = 'HistoricalSelectOrObjectUnavailable' },
    @{ Number = 229; Classification = 'SqlPermissionDenied' },
    @{ Number = 207; Classification = 'MissingObjectOrColumn' },
    @{ Number = 102; Classification = 'SqlSyntaxOrCompatibility' },
    @{ Number = 18456; Classification = 'AuthenticationOrDatabaseAccess' },
    @{ Number = 40501; Classification = 'TimeoutOrTransient' },
    @{ Number = 10060; Classification = 'ConnectivityFailure' }
  )
  foreach ($case in $cases) {
    $exception = New-SyntheticSqlException $case.Number
    if ($exception.GetType().FullName -ne 'Microsoft.Data.SqlClient.SqlException' -or $exception.Number -ne $case.Number) {
      throw "test fixture was not a real SqlException with number $($case.Number)"
    }
    $safe = Get-SanitizedUatInventoryFailure $exception
    if ($safe -notmatch "Classification=$($case.Classification); ProviderCode=$($case.Number);") {
      throw "SQL $($case.Number) did not map to $($case.Classification)"
    }
    if ($safe -match 'SYNTHETIC_SECRET_MUST_NOT_LEAK') { throw 'synthetic SQL message leaked' }
  }

  $unknown = Get-SanitizedUatInventoryFailure (New-SyntheticSqlException 50001)
  if ($unknown -notmatch 'Classification=UnclassifiedSqlFailure; ProviderCode=Suppressed;') {
    throw 'unapproved SQL number was not suppressed'
  }
  if ($unknown -match '50001|SYNTHETIC_SECRET_MUST_NOT_LEAK') { throw 'unapproved SQL signal leaked' }

  $wrappedException = New-SyntheticSqlException 50001
  $wrapped = [System.Management.Automation.ErrorRecord]::new(
    $wrappedException,
    'SyntheticSqlFailure',
    [System.Management.Automation.ErrorCategory]::InvalidOperation,
    $null
  )
  $wrappedSafe = Get-SanitizedUatInventoryFailure $wrapped
  if ($wrappedSafe -notmatch 'Classification=UnclassifiedSqlFailure; ProviderCode=Suppressed;') {
    throw 'wrapped unapproved SQL number was not suppressed'
  }
  if ($wrappedSafe -match '50001|SYNTHETIC_SECRET_MUST_NOT_LEAK|SyntheticSqlFailure') {
    throw 'wrapped SQL signal leaked'
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
