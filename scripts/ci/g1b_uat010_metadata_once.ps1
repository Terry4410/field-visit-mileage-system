# G1-B one authenticated SQL connection, exactly one static SELECT-only batch.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$stage = 'SOURCE'
$conn = $null
$cmd = $null
$token = $null
$expected = @('TARGET','PREDECESSOR_010','RESERVED_013_ABSENT','PREMIGRATION_011_020',
  'BASE_010_TABLES','LEGACY_004_007_TRIGGERS','LEGACY_TRIGGER_SOURCE_PARITY',
  'EXISTING_FK_CHECK_TRUST','IMPORT_STATUS_CHECKS','CONNECTED_DIAGNOSTIC_SELECT',
  'CONNECTED_VIEW_DEFINITION','CONNECTED_NO_SCHEMA_ALTER','ACTUAL_API_PRINCIPAL','FULL_010_PARITY')
try {
  if ($env:GITHUB_RUN_ATTEMPT -cne '1' -or $env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
      $env:GITHUB_REF -cne 'refs/heads/post-uat/v1.8.0' -or $env:RUNNER_DEBUG -ceq '1' -or
      $env:GITHUB_SHA -cnotmatch '^[a-f0-9]{40}$' -or
      $env:G1B_EXPECTED_SOURCE -cne $env:GITHUB_SHA -or
      $env:G1B_SERVER -cne 'sql-fieldvisit-jpe-uat.database.windows.net' -or
      $env:G1B_DATABASE -cne 'db-fieldvisit-uat') { throw 'source-guard' }
  $path = Join-Path $env:GITHUB_WORKSPACE 'scripts/ci/g1b_uat010_metadata_once.sql'
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'missing-sql' }
  $stage = 'AUTH_TOKEN'
  $token = (& az account get-access-token --resource 'https://database.windows.net/' --query accessToken --output tsv 2>$null | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) { throw 'auth-token' }
  # Never print access token or subscription identity.
  $stage = 'SINGLE_CONNECTION'
  Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
  $connectionBuilder = [Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
  $connectionBuilder['Data Source'] = 'tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433'
  $connectionBuilder['Initial Catalog'] = 'db-fieldvisit-uat'
  $connectionBuilder['Encrypt'] = $true
  $connectionBuilder['TrustServerCertificate'] = $false
  $connectionBuilder['Pooling'] = $false
  $connectionBuilder['Connect Timeout'] = 120
  $connectionBuilder['ConnectRetryCount'] = 0
  $connectionBuilder['ApplicationIntent'] = 'ReadOnly'
  $conn = [Microsoft.Data.SqlClient.SqlConnection]::new($connectionBuilder.ConnectionString)
  $conn.AccessToken = $token
  $conn.Open() # EXACTLY ONE attempt. Can wake serverless, Owner separately authorized cost.
  $stage = 'ONE_METADATA_QUERY'
  $cmd = $conn.CreateCommand()
  $cmd.CommandText = Get-Content -LiteralPath $path -Raw
  $cmd.CommandTimeout = 30
  $reader = $cmd.ExecuteReader()
  $results = [System.Data.DataTable]::new()
  try { $results.Load($reader) } finally { $reader.Dispose() }
  $stage = 'STRICT_RESULT_CONTRACT'
  if ($results.Columns.Count -ne 2 -or $results.Columns[0].ColumnName -cne 'code' -or
      $results.Columns[1].ColumnName -cne 'status' -or $results.Rows.Count -ne $expected.Count) { throw 'result-shape' }
  $found = @{}
  foreach ($row in $results.Rows) {
    $code = [string]$row['code']; $status = [string]$row['status']
    if ($code -cnotmatch '^[A-Z0-9_]+$' -or $code -cnotin $expected -or
        $status -cnotin @('PASS','FAIL','UNKNOWN') -or $found.ContainsKey($code)) { throw 'result-value' }
    $found[$code] = $status
  }
  foreach ($code in $expected) { if (-not $found.ContainsKey($code)) { throw 'missing-code' } }
  foreach ($code in $expected) { Write-Output "G1BCondition=$code; Status=$($found[$code]);" }
  Write-Output "G1B=METADATA_COLLECTED; SQLConnections=ONE; Queries=ONE; Writes=NONE; UAT=HOLD; SourceSha=$($env:GITHUB_SHA);"
  if ($found['TARGET'] -ne 'PASS') { exit 1 }
  exit 0
} catch {
  if ($stage -cnotin @('SOURCE','AUTH_TOKEN','SINGLE_CONNECTION','ONE_METADATA_QUERY','STRICT_RESULT_CONTRACT')) { $stage='UNKNOWN' }
  Write-Output "G1B=STOP; Stage=$stage; Details=SUPPRESSED; Retry=NONE; Writes=NONE; UAT=HOLD;"
  exit 1
} finally {
  if ($null -ne $cmd) { try {$cmd.Dispose()} catch {} }
  if ($null -ne $conn) { try {$conn.Close()} catch {}; try {$conn.Dispose()} catch {} }
  $token = $null
}
