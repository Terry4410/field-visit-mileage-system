# NONDEPLOY CANDIDATE ONLY. Invocation is deliberately not wired to Azure or any Live workflow.
# Diagnostic OIDC is NOT the deployed API SQL principal.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$ReviewedManifestSha256,
 [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ReviewedSourceSha,
 [Parameter(Mandatory)][string]$DiagnosticAccessToken,
 [Parameter(Mandatory)][string]$EvidenceFile,
 [switch]$ExecuteIsolated
)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$connection=$null
function Stop-Bridge { throw 'BRIDGE_STOP' }
try {
 if (-not $ExecuteIsolated) { Stop-Bridge }
 if ($env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
     $env:GITHUB_REF -cne 'refs/heads/work/g1-readonly-bridge-nondeploy-20261021' -or
     $env:GITHUB_SHA -cne $ReviewedSourceSha) { Stop-Bridge }
 if ($env:ACTIONS_STEP_DEBUG -eq 'true' -or $env:RUNNER_DEBUG -eq '1') { Stop-Bridge }
 if ([string]::IsNullOrWhiteSpace($DiagnosticAccessToken) -or
     [string]::IsNullOrWhiteSpace($EvidenceFile) -or
     (Test-Path -LiteralPath $EvidenceFile)) { Stop-Bridge }
 # An isolated test harness must prove the token's origin separately.
 # This script must never claim that token identity represents DefaultConnection.
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
 $b['Data Source']='tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433'
 $b['Initial Catalog']='db-fieldvisit-uat'
 $b['Encrypt']=$true
 $b['Trust Server Certificate']=$false
 $b['Connect Timeout']=120
 $b['ConnectRetryCount']=0
 $b['Pooling']=$false
 $b['ApplicationIntent']='ReadOnly'
 $connection=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
 $connection.AccessToken=$DiagnosticAccessToken
 if($connection.State -ne [Data.ConnectionState]::Closed) { Stop-Bridge }
 # Critical: this candidate is never permitted to open a connection; only the
 # reviewed Collector owns Open/Close after independently reviewed host authorization.
 # No invocation here until end-to-end deadline + live identity gates are proven.
 Write-Output 'BridgeConnection=PREPARED_ONLY; SqlOpened=FALSE; ApiPrincipal=UNKNOWN; G1=HOLD;'
} catch {
 Write-Output 'BridgeConnection=STOP; Details=SUPPRESSED;'
 exit 1
} finally {
 if($null -ne $connection) { $connection.Dispose() }
 $DiagnosticAccessToken=$null
}
