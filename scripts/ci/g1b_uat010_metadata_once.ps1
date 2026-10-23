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
  $stage = 'CLIENT_IMPORT'
  Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
  # Verify fixed Azure management DB state before opening SQL.
  $stage = 'MANAGEMENT_STATE'
  $subscription = (& az account show --query id --output tsv 2>$null | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or $subscription -cnotmatch '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { throw 'subscription' }
  $raw = (& az sql db show --subscription $subscription --resource-group rg-fieldvisit-uat --server sql-fieldvisit-jpe-uat --name db-fieldvisit-uat --query '{id:id,name:name,status:status}' --output json --only-show-errors 2>$null | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) { throw 'management' }
  $data = ConvertFrom-Json -InputObject $raw -ErrorAction Stop
  $targetId = "/subscriptions/$subscription/resourceGroups/rg-fieldvisit-uat/providers/Microsoft.Sql/servers/sql-fieldvisit-jpe-uat/databases/db-fieldvisit-uat"
  if ([string]$data.id -ine $targetId -or [string]$data.name -cne 'db-fieldvisit-uat') { throw 'target' }
  $dbState = [string]$data.status
  if ($dbState -cnotin @('Online','Paused','Resuming')) { throw 'state' }
  if ($dbState -cne 'Online') {
    Write-Output "G1BPreconnect=HOLD; State=$($dbState.ToUpperInvariant()); SQLConnections=ZERO;"
    exit 1
  }
  Write-Output 'G1BPreconnect=PASS; State=ONLINE; SQLConnections=ZERO;'
  $stage = 'CONNECTION_BUILDER'
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
  $stage = 'SQL_OPEN'
  $conn.Open() # Exactly one SQL Open. Never retry.
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
  $failure = $_
  $kind='UNCLASSIFIED'; $provider='Suppressed'
  if ($stage -ceq 'SQL_OPEN') {
    try {
      # Existing bounded error classifier; raw provider message is never output.
      . (Join-Path $PSScriptRoot 'uat_schema_fidelity_evidence.ps1')
      $safe = Get-SanitizedUatInventoryFailure $failure
      $m = [regex]::Match($safe, 'Classification=([A-Za-z]+); ProviderCode=(Suppressed|-?[0-9]+);')
      if ($m.Success -and $m.Groups[1].Value -in @('UnknownSqlFailure','TimeoutOrTransient','ConnectivityFailure','AuthenticationOrDatabaseAccess','SqlPermissionDenied','TargetGuardRejected','RequiredObjectMissing','HistoricalSelectOrObjectUnavailable','MissingObjectOrColumn','SqlSyntaxOrCompatibility','UnclassifiedSqlFailure')) {
        $kind=$m.Groups[1].Value; $provider=$m.Groups[2].Value
      }
    } catch { $kind='UNCLASSIFIED'; $provider='Suppressed' }
  }
  if ($stage -cnotin @('SOURCE','AUTH_TOKEN','CLIENT_IMPORT','MANAGEMENT_STATE','CONNECTION_BUILDER','SQL_OPEN','ONE_METADATA_QUERY','STRICT_RESULT_CONTRACT')) { $stage='UNKNOWN' }
  Write-Output "G1B=STOP; Stage=$stage; Class=$kind; ProviderCode=$provider; Details=SUPPRESSED; Retry=NONE; Writes=NONE; UAT=HOLD;"
  exit 1
} finally {
  if ($null -ne $cmd) { try {$cmd.Dispose()} catch {} }
  if ($null -ne $conn) { try {$conn.Close()} catch {}; try {$conn.Dispose()} catch {} }
  $token = $null
}
