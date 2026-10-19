# Future separate Owner cold-start authorization ONLY. No command/query is executed.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$connection=$null
$token=$null
try {
  if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_DEBUG -ceq '1' -or
      $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
      $env:GITHUB_REF -cne 'refs/heads/post-uat/v1.8.0' -or
      $env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or $env:R3_APPROVED_SHA -cne $env:GITHUB_SHA -or
      $env:R3_AUTHORIZE_WAKE -cne 'true' -or $env:R3_ACCEPT_CHARGES -cne 'true') { throw 'guard' }
  Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
  . (Join-Path $PSScriptRoot 'uat_schema_fidelity_evidence.ps1')
  $token=az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv 2>$null
  if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)){throw 'guard'}
  $builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
  $builder['Data Source']='tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433'
  $builder['Initial Catalog']='db-fieldvisit-uat'
  $builder['Connect Timeout']=120
  $builder['ConnectRetryCount']=0
  $builder['Pooling']=$false
  $builder['Encrypt']='True'
  $builder['Trust Server Certificate']=$false
  $builder['Application Intent']=[Microsoft.Data.SqlClient.ApplicationIntent]::ReadOnly
  $connection=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
  $connection.AccessToken=$token
  try {
    $connection.Open() # Exactly one Open; no SqlCommand, no SQL statement.
    Write-Output 'Opened'
  } catch {
    $safe=Get-SanitizedUatInventoryFailure $_
    if($safe -cmatch 'Classification=TimeoutOrTransient; ProviderCode=40613;'){
      # Expected cold-start signal only. A fresh management Online check is still mandatory.
      Write-Output 'ResumeSignaled'
    } else { throw 'guard' }
  }
} catch {
  # Called with captured output. Never emit provider exception or identity.
  Write-Output 'Rejected'
  exit 1
} finally {
  if($null -ne $connection){$connection.Dispose()}
  $connection=$null
  $token=$null
}
