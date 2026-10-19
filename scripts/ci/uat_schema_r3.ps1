Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'uat_schema_fidelity_evidence.ps1')

# No automatic execution on import. Only the fixed UAT wrapper below selects UAT.
function Invoke-R3Diagnostic {
  param(
    [hashtable]$Connection,
    [string]$SourceSha,
    [string]$ExpectedServer,
    [string]$ExpectedDatabase,
    [ValidateSet('Uat','Disposable')][string]$Mode = 'Uat',
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot)),
    [string]$ManifestHash
  )
  $safeSource = if ($env:GITHUB_SHA -cmatch '^[0-9a-f]{40}$') { $env:GITHUB_SHA } else { '0' * 40 }
  $stages = @('IdentityVersion','Catalog','HistoricalAccess','HistoricalAggregates')
  $stage = $stages[0]
  $hash = '0' * 64
  $classification = 'PrerequisiteRejected'
  $provider = 'Suppressed'
  try {
    if ($SourceSha -cne $env:GITHUB_SHA -or $SourceSha -cnotmatch '^[0-9a-f]{40}$' -or $ManifestHash -cnotmatch '^[0-9a-f]{64}$') { throw 'guard' }
    if ($ExpectedServer -cnotmatch '^[A-Za-z0-9_.-]{1,128}$' -or $ExpectedDatabase -cnotmatch '^[A-Za-z0-9_-]{1,128}$') { throw 'guard' }
    if ($Mode -eq 'Uat') {
      if ($ExpectedServer -cne 'sql-fieldvisit-jpe-uat' -or $ExpectedDatabase -cne 'db-fieldvisit-uat' -or
          $Connection.ServerInstance -cne 'sql-fieldvisit-jpe-uat.database.windows.net' -or
          $Connection.Database -cne 'db-fieldvisit-uat' -or
          @($Connection.Keys | Where-Object { $_ -notin @('ServerInstance','Database','AccessToken') }).Count -ne 0 -or
          [string]::IsNullOrWhiteSpace($Connection.AccessToken)) { throw 'guard' }
    } else {
      if ($env:GITHUB_ACTIONS -cne 'true' -or $Connection.ServerInstance -cne 'localhost,1433' -or
          $Connection.Database -cne 'R3Fixture' -or $ExpectedDatabase -cne 'R3Fixture' -or
          @($Connection.Keys | Where-Object { $_ -notin @('ServerInstance','Database','Username','Password') }).Count -ne 0) { throw 'guard' }
    }
    $manifestPath = Join-Path $Root 'scripts/ci/uat_schema_r3.hashes.json'
    if ((Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $ManifestHash) { throw 'guard' }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    # Verify every reviewed file before any connection, including classifier and this runner.
    foreach ($relative in $manifest.Keys) {
      if ((Get-FileHash (Join-Path $Root $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest[$relative]) { throw 'guard' }
    }
    $invoke = $Connection.Clone()
    $invoke.ApplicationIntent = 'ReadOnly'
    $invoke.ConnectionTimeout = 10
    $invoke.QueryTimeout = 30
    $invoke.AbortOnError = $true
    $invoke.OutputSqlErrors = $false
    $invoke.ErrorAction = 'Stop'
    $invoke.WarningAction = 'SilentlyContinue'
    $invoke.Verbose = $false
    $invoke.Debug = $false
    if ($Mode -eq 'Disposable') { $invoke.TrustServerCertificate = $true }
    foreach ($stage in $stages) {
      $relative = "docs/release/optionc-canonical-sql-draft/full-scheme-a/r3/$stage.sql"
      $hash = $manifest[$relative]
      $classification = 'ResultContractRejected'
      $provider = 'Suppressed'
      try {
        $data = Invoke-Sqlcmd @invoke -InputFile (Join-Path $Root $relative) -Variable @("R3ExpectedServer=$ExpectedServer","R3ExpectedDatabase=$ExpectedDatabase") -OutputAs DataSet
      } catch {
        $safe = Get-SanitizedUatInventoryFailure $_
        # Only the approved classifier's closed format reaches output.
        if ($safe -cmatch 'Classification=([A-Za-z]+); ProviderCode=(Suppressed|-?[0-9]+);') {
          $classification = $Matches[1]
          $provider = $Matches[2]
        } else { $classification = 'UnknownSqlFailure' }
        throw 'sql-stop'
      }
      if ($data -isnot [System.Data.DataSet] -or $data.Tables.Count -ne 1) { throw 'result' }
      $table = $data.Tables[0]
      if ($table.Columns.Count -ne 1 -or $table.Columns[0].ColumnName -cne 'StagePass' -or
          $table.Columns[0].DataType -ne [int] -or $table.Rows.Count -ne 1 -or
          $table.Rows[0][0] -is [System.DBNull] -or $table.Rows[0][0] -ne 1) { throw 'result' }
      Write-Output "Stage=$stage; Status=PASS; SourceSha=$SourceSha; SqlSha256=$hash;"
      $data.Dispose()
    }
    return
  } catch {
    # Never interpolate the caught error, data, identity, or connection.
    Write-Output "Stage=$stage; Status=STOP; Classification=$classification; ProviderCode=$provider; SourceSha=$safeSource; SqlSha256=$hash;"
    throw 'STOP: R3 diagnostic stopped; raw details suppressed.'
  }
}
