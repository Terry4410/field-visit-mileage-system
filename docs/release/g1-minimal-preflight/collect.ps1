# Future review candidate; default validates pins without opening SQL.
param([switch]$ExecuteAuthorized,[object]$ApplicationConnection,
 [string]$ExpectedManifestSha256,[string]$EvidenceFile)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$opened=$false
function Guard([bool]$ok){if(-not $ok){throw 'CONTROLLED_STOP'}}
try{
 $root=$PSScriptRoot;$mp=Join-Path $root 'manifest.json'
 Guard ($ExpectedManifestSha256 -cmatch '^[0-9a-f]{64}$')
 Guard ((Get-FileHash $mp -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $ExpectedManifestSha256)
 $m=Get-Content $mp -Raw|ConvertFrom-Json
 Guard ($m.source -ceq '88c21921860888f531a0962867f916c307452bbc' -and $m.rc -ceq 'f29affb3299828f45b1f131321779e8bea2f86a8')
 foreach($p in $m.files.PSObject.Properties){
  Guard ([IO.Path]::GetFileName($p.Name) -ceq $p.Name)
  Guard ((Get-FileHash (Join-Path $root $p.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $p.Value)
 }
 if(-not $ExecuteAuthorized){Write-Output 'Pins=PASS; Execution=NOT_REQUESTED; G1=UNKNOWN;';exit 0}
 # Future secure host supplies SAME application auth context. Not a management login.
 # Supplying a connection is NOT proof of actual runtime binding; that remains UNKNOWN.
 Guard ($null -ne $ApplicationConnection)
 Guard (-not [string]::IsNullOrWhiteSpace($EvidenceFile))
 Guard (-not (Test-Path -LiteralPath $EvidenceFile))
 Guard ($ApplicationConnection.GetType().FullName -ceq 'Microsoft.Data.SqlClient.SqlConnection')
 Guard ($ApplicationConnection.State -eq [Data.ConnectionState]::Closed)
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($ApplicationConnection.ConnectionString)
 Guard ($b.DataSource -ceq 'tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433' -and $b.InitialCatalog -ceq 'db-fieldvisit-uat')
 Guard (-not $b.TrustServerCertificate -and -not $b.IntegratedSecurity -and -not $b.AttachDBFilename -and -not $b.FailoverPartner)
 # SqlClient lazily returns a non-null built-in no-retry provider. Accept only
 # the exact SDK implementation/logic/enumerator produced by its public factory.
 $retry=$ApplicationConnection.RetryLogicProvider
 if($null -ne $retry){
  $none=[Microsoft.Data.SqlClient.SqlConfigurableRetryFactory]::CreateNoneRetryProvider()
  Guard ($retry.GetType() -eq $none.GetType() -and $null -ne $retry.RetryLogic)
  Guard ($retry.RetryLogic.GetType() -eq $none.RetryLogic.GetType() -and $null -ne $retry.RetryLogic.RetryIntervalEnumerator)
  Guard ($retry.RetryLogic.RetryIntervalEnumerator.GetType() -eq $none.RetryLogic.RetryIntervalEnumerator.GetType())
 }
 $b['Encrypt']=$true;$b['Connect Timeout']=120;$b['ConnectRetryCount']=0;$b['Pooling']=$false
 $b['ApplicationIntent']='ReadOnly';$b['MultiSubnetFailover']=$false
 $ApplicationConnection.ConnectionString=$b.ConnectionString
 . (Join-Path $root 'uat_schema_fidelity_evidence.ps1')
 $groups=[Collections.Generic.List[object]]::new();$clock=[Diagnostics.Stopwatch]::StartNew()
 $ApplicationConnection.Open();$opened=$true;$stage=0
 foreach($file in @('01-target.sql','02-marker.sql','03-catalog.sql','04-permissions.sql')){
  Guard ($clock.Elapsed.TotalSeconds -lt 210)
  $cmd=$ApplicationConnection.CreateCommand();$reader=$null
  try{
   $cmd.CommandTimeout=30;$cmd.CommandText=[IO.File]::ReadAllText((Join-Path $root $file))
   if($file -ceq '03-catalog.sql'){
    $parameter=$cmd.Parameters.Add('@requirements',[Data.SqlDbType]::NVarChar,-1)
    $parameter.Value=[IO.File]::ReadAllText((Join-Path $root 'requirements.json'))
    $reference=$cmd.Parameters.Add('@reference',[Data.SqlDbType]::NVarChar,-1)
    $reference.Value=[IO.File]::ReadAllText((Join-Path $root 'necessary-reference.json'))
   }
   $reader=$cmd.ExecuteReader()
   Guard ($reader.FieldCount -eq 2 -and $reader.GetName(0) -ceq 'code' -and $reader.GetName(1) -ceq 'status')
   $rows=[Collections.Generic.List[object]]::new()
   while($reader.Read()){
    Guard ($rows.Count -lt 20 -and -not $reader.IsDBNull(0) -and -not $reader.IsDBNull(1))
    $rows.Add([ordered]@{code=$reader.GetString(0);status=$reader.GetString(1)})
   }
   Guard (-not $reader.NextResult());$groups.Add($rows.ToArray())
   # Validate EACH result before any later SQL. Unknown columns/codes/errors STOP.
   $prefix=[ordered]@{contract='G1-MINIMAL-REVIEW-3';source=$m.source;rc=$m.rc;pins=$m.files;groups=$groups.ToArray()}
   $prefixPath=[IO.Path]::GetTempFileName()
   try{
    $prefix|ConvertTo-Json -Depth 15|Set-Content $prefixPath -Encoding utf8
    $null=& python (Join-Path $root 'validate.py') $prefixPath --prefix 2>$null
    Guard ($LASTEXITCODE -eq 0)
   }finally{Remove-Item $prefixPath -Force -ErrorAction SilentlyContinue}
   # Target/visibility/version remain prerequisites. Catalog findings only HOLD
   # the final release decision; they do not suppress independent permissions.
   if($stage -lt 2 -and @($rows|Where-Object{$_.status -cne 'PASS'}).Count -gt 0){break}
   $stage++
  }finally{if($null -ne $reader){$reader.Dispose()};$cmd.Dispose()}
 }
 $e=[ordered]@{contract='G1-MINIMAL-REVIEW-3';source=$m.source;rc=$m.rc;pins=$m.files;groups=$groups.ToArray()}
 $temporary=[IO.Path]::GetTempFileName()
 try{
  $e|ConvertTo-Json -Depth 15|Set-Content $temporary -Encoding utf8
  $null=& python (Join-Path $root 'validate.py') $temporary 2>$null
  Guard ($LASTEXITCODE -eq 0)
  Copy-Item $temporary $EvidenceFile -ErrorAction Stop
 }finally{Remove-Item $temporary -Force -ErrorAction SilentlyContinue}
 Write-Output 'Collection=VALIDATED; G1=UNKNOWN; API_PRINCIPAL_BINDING=UNKNOWN;'
}catch{
 if(Get-Command Get-SanitizedUatInventoryFailure -ErrorAction SilentlyContinue){
  $failure=Get-SanitizedUatInventoryFailure $_;Write-Output ('Collection=STOP; '+$failure)
 }else{Write-Output 'Collection=STOP; Classification=UnknownSqlFailure; ProviderCode=Suppressed;'}
 exit 1
}finally{if($opened){$ApplicationConnection.Dispose()}}
