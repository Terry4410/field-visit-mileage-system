Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'uat_schema_fidelity_evidence.ps1')
Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop

if ($env:SCHEMA_TEST_SQL_SERVER -ne 'localhost,1433' -or
    $env:SCHEMA_TEST_SQL_DATABASE -ne 'master' -or
    $env:SCHEMA_TEST_SQL_USER -ne 'sa' -or
    [string]::IsNullOrWhiteSpace($env:SCHEMA_TEST_SQL_PASSWORD)) {
  throw 'STOP: disposable SQL target configuration was invalid.'
}

$connection = @{
  ServerInstance = $env:SCHEMA_TEST_SQL_SERVER
  Database = $env:SCHEMA_TEST_SQL_DATABASE
  Username = $env:SCHEMA_TEST_SQL_USER
  Password = $env:SCHEMA_TEST_SQL_PASSWORD
  TrustServerCertificate = $true
  ConnectionTimeout = 5
  QueryTimeout = 15
  AbortOnError = $true
  OutputSqlErrors = $false
  ErrorAction = 'Stop'
}

$ready = $false
foreach ($attempt in 1..30) {
  try {
    $null = Invoke-Sqlcmd @connection -Query 'SELECT CAST(1 AS int) AS Ready' -OutputAs DataSet
    $ready = $true
    break
  } catch {
    Start-Sleep -Seconds 2
  }
}
if (-not $ready) { throw 'STOP: disposable SQL did not become ready.' }

function Assert-InvokeSqlcmdFailure(
  [string]$Name,
  [string]$Query,
  [string]$ExpectedClassification,
  [string]$ExpectedProviderCode
) {
  $caught = $null
  try {
    $null = Invoke-Sqlcmd @connection -Query $Query -OutputAs DataSet
  } catch {
    $caught = $_
  }
  if ($null -eq $caught) { throw "STOP: disposable SQL negative case did not fail: $Name" }

  $outerType = $caught.Exception.GetType().FullName
  if ($outerType -ne 'Microsoft.SqlServer.Management.PowerShell.SqlPowerShellSqlExecutionException' -and
      $outerType -ne 'Microsoft.Data.SqlClient.SqlException' -and
      $outerType -ne 'System.Data.SqlClient.SqlException') {
    throw "STOP: disposable SQL returned an unsupported outer wrapper: $Name"
  }

  $safe = Get-SanitizedUatInventoryFailure $caught
  $expected = "Classification=$ExpectedClassification; ProviderCode=$ExpectedProviderCode;"
  if ($safe -notmatch [regex]::Escape($expected)) {
    throw "STOP: disposable SQL classification mismatch: $Name"
  }
  if ($safe -match 'DISPOSABLE_SECRET|dbo\.DefinitelyMissing|Microsoft\.|System\.|localhost|master|sa') {
    throw "STOP: disposable SQL diagnostic leaked details: $Name"
  }
  Write-Output "PASS disposable Invoke-Sqlcmd classification: $Name"
}

Assert-InvokeSqlcmdFailure `
  'custom-throw' `
  "THROW 51992,N'DISPOSABLE_SECRET_REQUIRED_OBJECT',1;" `
  'RequiredObjectMissing' `
  '51992'

Assert-InvokeSqlcmdFailure `
  'missing-object' `
  'SELECT MissingColumn FROM dbo.DefinitelyMissing;' `
  'MissingObjectOrColumn' `
  '208'

Assert-InvokeSqlcmdFailure `
  'unsupported-provider-code' `
  "THROW 50001,N'DISPOSABLE_SECRET_UNKNOWN_CODE',1;" `
  'UnclassifiedSqlFailure' `
  'Suppressed'

Write-Output 'Disposable Invoke-Sqlcmd negative tests: 3 passed, 0 failed'
