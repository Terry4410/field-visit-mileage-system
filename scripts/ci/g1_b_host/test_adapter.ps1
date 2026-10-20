# REAL unopened SqlClient guards only. No Open, network SQL, token acquisition.
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$cases=0;$pass=$true
try {
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop *> $null
 . (Join-Path $PSScriptRoot 'adapter.ps1')
 $root=(git rev-parse --show-toplevel).Trim();$sha=(git rev-parse HEAD).Trim()
 $digest='a'*64
 function MakeBinding {return @{digest=$digest;source=$sha;scope='SCHEMA_DIAGNOSTIC_ONLY';verified=$true;token_bound=$true}}
 function MakeConnection {
  $c=[Microsoft.Data.SqlClient.SqlConnection]::new('Server=tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433;Initial Catalog=db-fieldvisit-uat;Encrypt=True;TrustServerCertificate=False;ConnectRetryCount=0;Pooling=False')
  $c.AccessToken='SYNTHETIC_NOT_A_VALID_ACCESS_TOKEN';return $c
 }
 function Check([string]$case,[hashtable]$overrides){
  $c=MakeConnection;$b=MakeBinding
  $invoke=@{Connection=$c;Binding=$b;ExpectedBindingDigest=$digest;SourceSha=$sha;ExpectedSourceSha=$sha;Root=$root;EvidenceFile=(Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString()));DedicatedWorker=$true;SeparatelyAuthorized=$true}
  foreach($key in $overrides.Keys){$invoke[$key]=$overrides[$key]}
  try {$result=Invoke-G1BoundedCollector @invoke;if($result -cne 'ADAPTER_GUARD_STOP'){throw 'STOP'}
   if($c.State -ne [Data.ConnectionState]::Closed){throw 'STOP'}
   $script:cases++
  }finally{$c.Dispose()}
 }
 Check 'AuthorizationMissing' @{SeparatelyAuthorized=$false}
 Check 'WorkerMissing' @{DedicatedWorker=$false}
 Check 'WrongSource' @{SourceSha=('0'*40)}
 Check 'MissingBinding' @{Binding=$null}
 $b=MakeBinding;$b.digest='b'*64;Check 'MismatchedBinding' @{Binding=$b}
 $b=MakeBinding;$b.token_bound=$false;Check 'UnboundToken' @{Binding=$b}
 $c=MakeConnection;$c.AccessToken='';try{Check 'MissingToken' @{Connection=$c}}finally{$c.Dispose()}
 $c=MakeConnection;$c.ConnectionString=$c.ConnectionString.Replace('db-fieldvisit-uat','synthetic-wrong');try{Check 'WrongDatabase' @{Connection=$c}}finally{$c.Dispose()}
 $c=MakeConnection;$c.ConnectionString=$c.ConnectionString.Replace('sql-fieldvisit-jpe-uat','synthetic-wrong');try{Check 'WrongServer' @{Connection=$c}}finally{$c.Dispose()}
 $b=MakeBinding;$b.scope='API_PRINCIPAL';Check 'IdentitySubstitution' @{Binding=$b}
}catch{$pass=$false}
Write-Output ('UnopenedSqlClientGuards='+$(if($pass){'PASS'}else{'STOP'})+'; Cases='+$cases)
if(-not $pass -or $cases -ne 10){exit 1}
