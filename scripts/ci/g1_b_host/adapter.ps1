# Library only. No login, credential resolver, SQL Open, or dispatch on import.
$ErrorActionPreference='Stop'
function Invoke-G1BoundedCollector {
 param([object]$Connection,[object]$Binding,[string]$ExpectedBindingDigest,
       [string]$SourceSha,[string]$ExpectedSourceSha,[string]$Root,
       [string]$EvidenceFile,[switch]$DedicatedWorker,[switch]$SeparatelyAuthorized)
 # This must run only in the dedicated worker of a separately reviewed host.
 # A binding receipt is host attestation, not independently discovered SQL identity.
 $code='ADAPTER_GUARD_STOP';$owned=$false;$ps=$null;$runspace=$null
 try {
  if(-not $DedicatedWorker -or -not $SeparatelyAuthorized){throw 'STOP'}
  if($ExpectedSourceSha -cnotmatch '^[0-9a-f]{40}$' -or $SourceSha -cne $ExpectedSourceSha){throw 'STOP'}
  $source=(git -C $Root rev-parse HEAD 2>$null)
  if($LASTEXITCODE -ne 0 -or $source -cne $SourceSha){throw 'STOP'}
  if($null -eq $Binding -or $ExpectedBindingDigest -cnotmatch '^[0-9a-f]{64}$'){throw 'STOP'}
  if($Binding.verified -isnot [bool] -or $Binding.token_bound -isnot [bool]){throw 'STOP'}
  if($Binding.digest -cne $ExpectedBindingDigest -or $Binding.source -cne $SourceSha -or
     $Binding.scope -cne 'SCHEMA_DIAGNOSTIC_ONLY' -or $Binding.verified -cne $true -or
     $Binding.token_bound -cne $true){throw 'STOP'}
  if($null -eq $Connection -or $Connection.GetType().FullName -cne 'Microsoft.Data.SqlClient.SqlConnection'){throw 'STOP'}
  # Ownership transfers only after context and type validation; always dispose it.
  $owned=$true
  if($Connection.State -ne [Data.ConnectionState]::Closed){throw 'STOP'}
  if([string]::IsNullOrWhiteSpace($Connection.AccessToken)){throw 'STOP'}
  $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($Connection.ConnectionString)
  if($b.DataSource -cne 'tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433' -or
     $b.InitialCatalog -cne 'db-fieldvisit-uat' -or $b.TrustServerCertificate -or
     $b.IntegratedSecurity -or $b.AttachDBFilename -or $b.FailoverPartner -or
     $b.UserID -or $b.Password -or $b.Authentication.ToString() -cne 'NotSpecified') {throw 'STOP'}
  $package=Join-Path $Root 'docs/release/g1-minimal-preflight'
  $manifestHash='7742024b9bb790acef0821d0dd10361dd284f4a44311d8045464d1a7f1db686f'
  $manifest=Join-Path $package 'manifest.json'
  if((Get-FileHash $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifestHash){throw 'STOP'}
  $m=Get-Content $manifest -Raw|ConvertFrom-Json
  foreach($p in $m.files.PSObject.Properties){
   if([IO.Path]::GetFileName($p.Name) -cne $p.Name -or
      (Get-FileHash (Join-Path $package $p.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $p.Value){throw 'STOP'}
  }
  # Collector owns the sole Open and its four pinned commands. No probes here.
  # A local runspace shares the actual object; do not use remoting/Start-Job serialization.
  $runspace=[runspacefactory]::CreateRunspace();$runspace.Open()
  $ps=[powershell]::Create();$ps.Runspace=$runspace
  $null=$ps.AddCommand((Join-Path $package 'collect.ps1')).AddParameter('ExecuteAuthorized',$true).AddParameter('ApplicationConnection',$Connection).AddParameter('ExpectedManifestSha256',$manifestHash).AddParameter('EvidenceFile',$EvidenceFile)
  $pending=$ps.BeginInvoke()
  # Parent process supervisor owns the HARD 240s boundary. This is cooperative
  # cancellation only, with enough reserve for parent forced termination.
  if(-not $pending.AsyncWaitHandle.WaitOne(220000)){
   $code='ADAPTER_DEADLINE_STOP'
   $null=$ps.BeginStop($null,$null)
   throw 'STOP'
  }
  $null=$ps.EndInvoke($pending) # All Collector streams remain private in memory.
  if($ps.HadErrors -or -not (Test-Path -LiteralPath $EvidenceFile)){throw 'STOP'}
  $check=& python (Join-Path $package 'validate.py') $EvidenceFile 2>$null
  if($LASTEXITCODE -ne 0 -or $check -cnotin @('FORMAT_PASS; G1=UNKNOWN','FORMAT_PASS; G1=FAIL')){throw 'STOP'}
  $code='ADAPTER_COLLECTED_HOLD'
 }catch{# Never emit Exception, ErrorRecord, provider text, or connection properties.
 }finally{
  try{if($owned){$Connection.Dispose()}}catch{$code='ADAPTER_CLEANUP_STOP'}
  try{if($null -ne $ps){$ps.Dispose()};if($null -ne $runspace){$runspace.Dispose()}}catch{$code='ADAPTER_CLEANUP_STOP'}
 }
 # Only this fixed enum is public. Supervisor discards all worker stdout/stderr.
 return $code
}
