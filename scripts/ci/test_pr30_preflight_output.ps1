param(
  [Parameter(Mandatory = $true)]
  [string]$WorkflowPath,
  [Parameter(Mandatory = $true)]
  [string]$ModulePath,
  [Parameter(Mandatory = $true)]
  [string]$ExpectedSourceSha
)

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0

Add-Type -TypeDefinition @'
public sealed class SyntheticSqlFailureException : System.Exception
{
    public int Number { get; }
    public SyntheticSqlFailureException(int number, string message) : base(message) { Number = number; }
}
'@


function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
}

function Run-Test([string]$Name, [scriptblock]$Action) {
  try {
    & $Action
    $script:Passed++
    Write-Output "PASS $Name"
  } catch {
    $script:Failed++
    Write-Output "FAIL $Name"
    throw
  }
}

function Assert-Throws([string]$Name, [string]$ExpectedMessage, [scriptblock]$Action) {
  try {
    & $Action
  } catch {
    $message = $_.Exception.Message
    if ($message -notlike "*$ExpectedMessage*") {
      throw "Expected '$ExpectedMessage' but received '$message'."
    }
    $script:Passed++
    Write-Output "PASS $Name"
    return
  }
  $script:Failed++
  throw "Expected fail-closed exception for '$Name'."
}

function New-TestTable([string]$Name, [object[]]$Columns, [object[]]$Rows) {
  $table = [System.Data.DataTable]::new($Name)
  foreach ($column in $Columns) {
    [void]$table.Columns.Add([string]$column.Name, [type]$column.Type)
  }
  foreach ($values in $Rows) {
    $row = $table.NewRow()
    foreach ($column in $Columns) {
      $columnName = [string]$column.Name
      $value = $values[$columnName]
      $row[$columnName] = if ($null -eq $value) { [DBNull]::Value } else { $value }
    }
    [void]$table.Rows.Add($row)
  }
  return ,$table
}

function Add-TestTable([System.Data.DataSet]$DataSet, [System.Data.DataTable]$Table) {
  [void]$DataSet.Tables.Add($Table)
}

function New-ValidEvidenceDataSet(
  [switch]$EmptySchema,
  [switch]$NullPermission,
  [switch]$InvalidIntegrity,
  [switch]$InvalidCompletion,
  [switch]$AddUnknown,
  [switch]$AddConflict,
  [switch]$DuplicateSchema,
  [switch]$DuplicateOptional,
  [switch]$OmitSchema,
  [switch]$OmitCatalog,
  [switch]$OmitPermissions
) {
  $dataSet = [System.Data.DataSet]::new('SyntheticEvidence')
  $now = [DateTime]::UtcNow

  Add-TestTable $dataSet (New-TestTable 'identity' @(
    @{ Name='ServerName'; Type=[string] },
    @{ Name='DatabaseName'; Type=[string] },
    @{ Name='ReadOnlyIdentity'; Type=[string] },
    @{ Name='ApplicationName'; Type=[string] },
    @{ Name='ObservedUtc'; Type=[datetime] },
    @{ Name='SecretToken'; Type=[string] }
  ) @(
    @{ ServerName='sql-fieldvisit-jpe-uat'; DatabaseName='db-fieldvisit-uat'; ReadOnlyIdentity='principal-secret'; ApplicationName='test'; ObservedUtc=$now; SecretToken='token-must-not-leak' }
  ))

  if (-not $OmitSchema) {
    $schemaRows = if ($EmptySchema) { @() } else { @(
      @{ VersionNumber='1.8.0-014'; AppliedAt=$now },
      @{ VersionNumber='1.8.0-012'; AppliedAt=$now.AddMinutes(-1) }
    ) }
    Add-TestTable $dataSet (New-TestTable 'schemaVersions' @(
      @{ Name='VersionNumber'; Type=[string] },
      @{ Name='AppliedAt'; Type=[datetime] }
    ) $schemaRows)
    if ($DuplicateSchema) {
      Add-TestTable $dataSet (New-TestTable 'schemaVersionsDuplicate' @(
        @{ Name='VersionNumber'; Type=[string] },
        @{ Name='AppliedAt'; Type=[datetime] }
      ) @(@{ VersionNumber='1.8.0-011'; AppliedAt=$now.AddMinutes(-2) }))
    }
  }

  Add-TestTable $dataSet (New-TestTable 'rowCountsEmpty' @(
    @{ Name='TableName'; Type=[string] },
    @{ Name='RowCount'; Type=[long] }
  ) @())

  if ($DuplicateOptional) {
    Add-TestTable $dataSet (New-TestTable 'rowCountsDuplicate' @(
      @{ Name='TableName'; Type=[string] },
      @{ Name='RowCount'; Type=[long] }
    ) @())
  }

  Add-TestTable $dataSet (New-TestTable 'integrityOne' @(
    @{ Name='CheckName'; Type=[string] },
    @{ Name='Violations'; Type=[int] }
  ) @(@{ CheckName='ProjectLocationsWithoutLocation'; Violations=if ($InvalidIntegrity) { 1 } else { 0 } }))

  Add-TestTable $dataSet (New-TestTable 'integrityTwo' @(
    @{ Name='CheckName'; Type=[string] },
    @{ Name='Violations'; Type=[int] }
  ) @())

  if (-not $OmitPermissions) {
    $nullValue = if ($NullPermission) { $null } else { 0 }
    Add-TestTable $dataSet (New-TestTable 'permissions' @(
      @{ Name='OriginalLogin'; Type=[string] },
      @{ Name='ServerPrincipal'; Type=[string] },
      @{ Name='DatabaseUser'; Type=[string] },
      @{ Name='IsDbOwner'; Type=[int] },
      @{ Name='IsDbDdlAdmin'; Type=[int] },
      @{ Name='IsDbDatawriter'; Type=[int] },
      @{ Name='CanCreateTable'; Type=[int] },
      @{ Name='CanAlterAnyUser'; Type=[int] },
      @{ Name='CanInsertChangeRequests'; Type=[int] },
      @{ Name='CanUpdateChangeRequests'; Type=[int] },
      @{ Name='CanDeleteChangeRequests'; Type=[int] },
      @{ Name='CanInsertSchemaVersions'; Type=[int] },
      @{ Name='CanUpdateLocations'; Type=[int] }
    ) @(@{
      OriginalLogin='login-secret'; ServerPrincipal='server-secret'; DatabaseUser='database-secret';
      IsDbOwner=0; IsDbDdlAdmin=1; IsDbDatawriter=$nullValue; CanCreateTable=0; CanAlterAnyUser=0;
      CanInsertChangeRequests=0; CanUpdateChangeRequests=0; CanDeleteChangeRequests=0;
      CanInsertSchemaVersions=0; CanUpdateLocations=0
    }))
  }

  Add-TestTable $dataSet (New-TestTable 'summary' @(
    @{ Name='MigrationPhase'; Type=[string] },
    @{ Name='CanonicalRequestTablesPresent'; Type=[int] },
    @{ Name='StageAndPublishedVersionsPresent'; Type=[int] },
    @{ Name='MergeDetailsPresent'; Type=[int] },
    @{ Name='CanonicalEvidenceAuditExtended'; Type=[int] }
  ) @(@{
    MigrationPhase='1.8.0-014'; CanonicalRequestTablesPresent=1;
    StageAndPublishedVersionsPresent=1; MergeDetailsPresent=1; CanonicalEvidenceAuditExtended=1
  }))

  if (-not $OmitCatalog) {
    Add-TestTable $dataSet (New-TestTable 'catalog' @(
      @{ Name='CatalogFingerprintSha256'; Type=[string] }
    ) @(@{ CatalogFingerprintSha256=('A' * 64) }))
  }

  Add-TestTable $dataSet (New-TestTable 'completion' @(
    @{ Name='Result'; Type=[string] },
    @{ Name='ServerName'; Type=[string] },
    @{ Name='DatabaseName'; Type=[string] },
    @{ Name='LatestSchemaVersion'; Type=[string] },
    @{ Name='CompletedUtc'; Type=[datetime] }
  ) @(@{
    Result=if ($InvalidCompletion) { 'INVALID' } else { 'PREFLIGHT_COMPLETE_READ_ONLY' };
    ServerName='sql-fieldvisit-jpe-uat'; DatabaseName='db-fieldvisit-uat';
    LatestSchemaVersion='1.8.0-014'; CompletedUtc=$now
  }))

  if ($AddUnknown) {
    Add-TestTable $dataSet (New-TestTable 'unknown' @(
      @{ Name='SecretToken'; Type=[string] }
    ) @(@{ SecretToken='token-must-not-leak' }))
  }

  if ($AddConflict) {
    Add-TestTable $dataSet (New-TestTable 'conflict' @(
      @{ Name='Result'; Type=[string] },
      @{ Name='LatestSchemaVersion'; Type=[string] },
      @{ Name='CompletedUtc'; Type=[datetime] },
      @{ Name='VersionNumber'; Type=[string] }
    ) @(@{
      Result='PREFLIGHT_COMPLETE_READ_ONLY'; LatestSchemaVersion='1.8.0-014';
      CompletedUtc=$now; VersionNumber='1.8.0-014'
    }))
  }

  return ,$dataSet
}

if (-not (Test-Path -LiteralPath $WorkflowPath)) {
  throw "Workflow not found: $WorkflowPath"
}
if (-not (Test-Path -LiteralPath $ModulePath)) {
  throw "Evidence module not found: $ModulePath"
}
$workflowText = Get-Content -LiteralPath $WorkflowPath -Raw
. $ModulePath

$actualSourceSha = (git rev-parse HEAD).Trim()
Assert-SourceSha $actualSourceSha $ExpectedSourceSha
$validSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'

Run-Test 'multiple result sets and valid completion' {
  $evidence = Capture-Evidence (New-ValidEvidenceDataSet) $validSha
  Assert-True ($evidence.CompletionMarker -eq 'PREFLIGHT_COMPLETE_READ_ONLY') 'Completion marker mismatch.'
  Assert-True ($evidence.SourceSha -eq $validSha) 'Source SHA mismatch.'
  Assert-True ($evidence.ResultSetCount -ge 8) 'Not all result sets were captured.'
}

Run-Test 'numeric permission evidence preserves zero and one' {
  $evidence = Capture-Evidence (New-ValidEvidenceDataSet) $validSha
  $permission = @($evidence.ResultSets | Where-Object Kind -eq 'permissions')[0].Rows[0]
  Assert-True ($permission.IsDbOwner -eq 0) 'Permission 0 was not preserved.'
  Assert-True ($permission.IsDbDdlAdmin -eq 1) 'Permission 1 was not preserved.'
}

Run-Test 'sensitive identities are redacted' {
  $evidence = Capture-Evidence (New-ValidEvidenceDataSet) $validSha
  $json = $evidence | ConvertTo-Json -Depth 20 -Compress
  foreach ($secret in @('principal-secret','login-secret','server-secret','database-secret')) {
    Assert-True (-not $json.Contains($secret)) "Sensitive identity leaked: $secret"
  }
  Assert-True ($json.Contains('[REDACTED]')) 'Redaction marker missing.'
}

Run-Test 'unauthorized columns and token values are excluded' {
  $evidence = Capture-Evidence (New-ValidEvidenceDataSet) $validSha
  $json = $evidence | ConvertTo-Json -Depth 20 -Compress
  Assert-True (-not $json.Contains('SecretToken')) 'Unauthorized column name leaked.'
  Assert-True (-not $json.Contains('token-must-not-leak')) 'Unauthorized value leaked.'
}

Run-Test 'conditional empty result is explicitly not applicable' {
  $evidence = Capture-Evidence (New-ValidEvidenceDataSet) $validSha
  $empty = @($evidence.ResultSets | Where-Object Kind -eq 'rowCounts')[0]
  Assert-True ($empty.RowCount -eq 0) 'Conditional result was not empty.'
  Assert-True ($empty.Applicability -eq 'NotApplicable') 'Empty semantics missing.'
}

Run-Test 'duplicate integrity result sets remain independently observable' {
  $evidence = Capture-Evidence (New-ValidEvidenceDataSet) $validSha
  Assert-True (@($evidence.ResultSets | Where-Object Kind -eq 'integrity').Count -eq 2) 'Integrity result sets were lost.'
}

Run-Test 'source SHA consistency passes' {
  Assert-SourceSha $validSha $validSha
}

Assert-Throws 'source SHA mismatch fails closed' 'does not match' {
  Assert-SourceSha $validSha 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
}
Assert-Throws 'missing schemaVersions fails closed' "schemaVersions" {
  Capture-Evidence (New-ValidEvidenceDataSet -OmitSchema) $validSha
}
Assert-Throws 'duplicate schemaVersions fails closed' 'duplicated result kind' {
  Capture-Evidence (New-ValidEvidenceDataSet -DuplicateSchema) $validSha
}
Assert-Throws 'required empty schemaVersions fails closed' 'empty' {
  Capture-Evidence (New-ValidEvidenceDataSet -EmptySchema) $validSha
}
Assert-Throws 'conflicting result kind fails closed' 'conflicting result-set' {
  Capture-Evidence (New-ValidEvidenceDataSet -AddConflict) $validSha
}
Assert-Throws 'unknown result set fails closed without projection' 'unrecognized SQL result-set' {
  Capture-Evidence (New-ValidEvidenceDataSet -AddUnknown) $validSha
}
Assert-Throws 'missing catalog fails closed' 'catalogFingerprint' {
  Capture-Evidence (New-ValidEvidenceDataSet -OmitCatalog) $validSha
}
Assert-Throws 'integrity violation fails closed' 'integrity violations' {
  Capture-Evidence (New-ValidEvidenceDataSet -InvalidIntegrity) $validSha
}
Assert-Throws 'invalid completion fails closed' 'completion marker' {
  Capture-Evidence (New-ValidEvidenceDataSet -InvalidCompletion) $validSha
}
Assert-Throws 'missing permission evidence fails closed' 'permissions' {
  Capture-Evidence (New-ValidEvidenceDataSet -OmitPermissions) $validSha
}
Assert-Throws 'null permission evidence fails closed' 'missing or unknown' {
  Capture-Evidence (New-ValidEvidenceDataSet -NullPermission) $validSha
}
Assert-Throws 'duplicate nonmandatory result kind fails closed' 'duplicated result kind' {
  Capture-Evidence (New-ValidEvidenceDataSet -DuplicateOptional) $validSha
}
Assert-Throws 'malformed source SHA fails closed' 'malformed' {
  Assert-SourceSha 'not-a-sha' $validSha
}


Run-Test 'sanitized timeout classification preserves approved number only' {
  $failure = [SyntheticSqlFailureException]::new(-2, 'token-and-connection-secret')
  $safe = Get-SanitizedSqlFailure -Failure $failure -Phase BasicQuery
  Assert-True ($safe.Classification -eq 'Timeout') 'Timeout was not classified.'
  Assert-True (@($safe.SqlErrorNumbers).Count -eq 1 -and $safe.SqlErrorNumbers[0] -eq -2) 'Approved timeout number was not preserved.'
  $json = $safe | ConvertTo-Json -Depth 5 -Compress
  Assert-True (-not $json.Contains('token-and-connection-secret')) 'Raw exception message leaked.'
}

Run-Test 'sanitized authentication classification does not expose message' {
  $failure = [SyntheticSqlFailureException]::new(18456, 'principal-secret')
  $safe = Get-SanitizedSqlFailure -Failure $failure -Phase PreflightQuery
  Assert-True ($safe.Classification -eq 'AuthenticationRejected') 'Authentication failure was not classified.'
  Assert-True (-not (($safe | ConvertTo-Json -Compress).Contains('principal-secret'))) 'Principal detail leaked.'
}

Run-Test 'unknown failure remains fail closed without raw details' {
  $failure = [System.InvalidOperationException]::new('server-and-token-secret')
  $safe = Get-SanitizedSqlFailure -Failure $failure -Phase BasicQuery
  Assert-True ($safe.Classification -eq 'UnknownSqlFailure') 'Unknown failure was not retained as unknown.'
  Assert-True ($safe.ExceptionTypes[0] -eq 'OtherException') 'Unapproved exception type was exposed.'
  Assert-True (-not (($safe | ConvertTo-Json -Compress).Contains('server-and-token-secret'))) 'Unknown exception message leaked.'
}

Assert-Throws 'sanitized throw is nonzero and message-safe' 'Classification=Timeout' {
  Throw-SanitizedSqlFailure -Failure ([SyntheticSqlFailureException]::new(-2, 'must-not-leak')) -Phase BasicQuery
}


Run-Test 'bounded connection and query timeout contract' {
  Assert-True (($workflowText | Select-String -Pattern '-ConnectionTimeout 120' -AllMatches).Matches.Count -eq 2) 'Both SQL calls must use ConnectionTimeout 120.'
  Assert-True (($workflowText | Select-String -Pattern '-ConnectionTimeout 15' -AllMatches).Matches.Count -eq 0) 'Legacy ConnectionTimeout 15 remains.'
  Assert-True (($workflowText | Select-String -Pattern '-QueryTimeout 30' -AllMatches).Matches.Count -eq 1) 'BasicQuery must retain QueryTimeout 30.'
  Assert-True (($workflowText | Select-String -Pattern '-QueryTimeout 120' -AllMatches).Matches.Count -eq 1) 'ReviewedPreflight must retain QueryTimeout 120.'
  Assert-True ($workflowText.Contains('timeout-minutes: 15')) 'SQL job timeout budget changed or is missing.'
}

Run-Test 'bounded wait preserves protected read-only guards' {
  Assert-True (($workflowText | Select-String -Pattern '-ApplicationIntent ReadOnly' -AllMatches).Matches.Count -eq 2) 'ApplicationIntent ReadOnly must remain on both SQL calls.'
  Assert-True (($workflowText | Select-String -Pattern '-OutputSqlErrors \$false' -AllMatches).Matches.Count -eq 2) 'Raw SQL error output must remain disabled.'
  Assert-True ($workflowText.Contains('environment: uat')) 'Protected uat Environment guard is missing.'
  Assert-True ($workflowText.Contains('refs/heads/post-uat/v1.8.0')) 'Protected branch guard is missing.'
  Assert-True ($workflowText.Contains('44c20438c8125150d8cdbfae5e2dab40f23acea6386507b96bec31ff3cb10955')) 'Reviewed SQL checksum pin changed.'
  Assert-True ($workflowText -notmatch 'Start-Sleep|az\s+sql\s+db\s+(update|resume)|while\s*\(') 'Retry, wake-up, or Azure mutation logic was introduced.'
}

Assert-True ($workflowText -notmatch 'Format-Table') 'Format-Table must not be used.'
Assert-True ($workflowText -notmatch "-replace 'principal") 'String-level regex masking must not be used.'
Assert-True (($workflowText | Select-String -Pattern '-OutputAs DataSet' -AllMatches).Matches.Count -eq 2) 'Both SQL calls must use DataSet output.'
Assert-True (($workflowText | Select-String -Pattern 'Throw-SanitizedSqlFailure' -AllMatches).Matches.Count -eq 2) 'Both SQL catches must use sanitized diagnostics.'
Assert-True (($workflowText | Select-String -Pattern '-OutputSqlErrors \$false' -AllMatches).Matches.Count -eq 2) 'Raw SQL error output must remain disabled.'
Assert-True ($workflowText.Contains('-ApplicationIntent ReadOnly')) 'ApplicationIntent ReadOnly is missing.'
Assert-True ($workflowText.Contains('environment: uat')) 'Protected environment guard is missing.'
Assert-True ($workflowText.Contains('refs/heads/post-uat/v1.8.0')) 'Protected branch guard is missing.'
Assert-True ($workflowText.Contains('44c20438c8125150d8cdbfae5e2dab40f23acea6386507b96bec31ff3cb10955')) 'Reviewed SQL checksum pin changed.'

Write-Output "PR30_SYNTHETIC_TESTS_PASSED=$script:Passed"
if ($script:Failed -ne 0) {
  throw "Synthetic tests failed: $script:Failed"
}
