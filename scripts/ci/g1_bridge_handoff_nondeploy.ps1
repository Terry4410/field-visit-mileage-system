# NONDEPLOY-only closed-connection handoff contract. No Azure token or SQL execution here.
Set-StrictMode -Version Latest
function Invoke-G1IsolatedHandoff {
 [CmdletBinding()]
 param(
  [Parameter(Mandatory)][object]$Connection,
  [Parameter(Mandatory)][scriptblock]$TestCollector,
  [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha
 )
 $ErrorActionPreference='Stop'
 try {
  if($env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
     $env:GITHUB_REF -cne 'refs/heads/work/g1-readonly-bridge-nondeploy-20261021' -or
     $env:GITHUB_SHA -cne $SourceSha -or
     $env:RUNNER_DEBUG -eq '1') { throw 'STOP' }
  if($Connection.GetType().FullName -cne 'Microsoft.Data.SqlClient.SqlConnection' -or
     $Connection.State -ne [Data.ConnectionState]::Closed) { throw 'STOP' }
  $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($Connection.ConnectionString)
  if($b.DataSource -cne '127.0.0.1,1433' -or $b.InitialCatalog -cne 'master' -or
     $b.ConnectRetryCount -ne 0 -or $b.Pooling -or $b.TrustServerCertificate -ne $true) {throw 'STOP'}
  # A supplied test callback is allowed only with local disposable SQL metadata.
  # No Live collector is invoked here; accepted Collector owns real Open and Dispose.
  $out=@(& $TestCollector $Connection 2>$null)
  if($out.Count -ne 1 -or $out[0] -cne 'ISOLATED_COLLECTOR_SIMULATED') {throw 'STOP'}
  Write-Output 'IsolatedHandoff=PASS; LiveExecution=FALSE; ApiPrincipal=UNKNOWN;'
 } catch {
  Write-Output 'IsolatedHandoff=STOP; Details=SUPPRESSED;'
  throw 'G1_ISOLATED_HANDOFF_REJECTED'
 } finally {
  if($null -ne $Connection) {$Connection.Dispose()}
 }
}
