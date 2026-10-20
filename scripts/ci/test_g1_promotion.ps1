param([ValidateSet('Reference','Test')][string]$Mode='Reference')
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$case='START';$connections=[Collections.Generic.List[object]]::new();$dbs=[Collections.Generic.List[string]]::new();$results=[Collections.Generic.List[object]]::new()
function Assert([bool]$ok){if(-not $ok){throw 'ASSERT_STOP'}}
function Exec($c,[string]$sql){$cmd=$c.CreateCommand();try{$cmd.CommandTimeout=120;$cmd.CommandText=$sql;[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}
function Scalar($c,[string]$sql){$cmd=$c.CreateCommand();try{$cmd.CommandTimeout=30;$cmd.CommandText=$sql;return [string]$cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function Code($e){while($null -ne $e){if($e -is [Microsoft.Data.SqlClient.SqlException]){return [int]$e.Number};$e=$e.InnerException};return -99999}
function Refuse($c,[string]$sql,[int[]]$codes){try{Exec $c $sql}catch{Assert ((Code $_.Exception) -in $codes);return};throw 'EXPECTED_STOP_MISSING'}
function Pass([string]$id){$results.Add(@{case=$id;result='PASS'});Write-Output "PromotionCase=$id; Status=PASS;"}
try{
 $case='Client';Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
 Assert ($env:GITHUB_ACTIONS -eq 'true' -and $b.DataSource -in @('localhost,1433','127.0.0.1,1433') -and $b.InitialCatalog -eq 'master' -and $b.UserID -eq 'sa' -and -not $b.IntegratedSecurity)
 $b['Pooling']=$false;$b['Connect Timeout']=5;$b['ConnectRetryCount']=0
 $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString);$admin.Open()
 $repo=(Resolve-Path "$PSScriptRoot/../..").Path;$p=Join-Path $repo 'docs/release/g1-execution/promotion';$draft=Join-Path $repo 'docs/release/optionc-canonical-sql-draft/candidate'
 $old=Get-Content (Join-Path $PSScriptRoot 'run_full_candidate_migration.ps1') -Raw
 $match=[regex]::Matches($old,"(?s)\`$boot=@'\r?\n(.*?)\r?\n'@");Assert ($match.Count -eq 1);$boot=$match[0].Groups[1].Value
 $catalog=[IO.File]::ReadAllText((Join-Path $p 'CatalogContract.sql'));$history=[IO.File]::ReadAllText((Join-Path $p 'HistoryContract.sql'));$ledger=[IO.File]::ReadAllText((Join-Path $p 'LedgerContract.sql'))
 function OpenDb([string]$name,[bool]$optIn=$true){Assert ($name -match '^(OptionCIsolated_|G1Denied_)[0-9a-f]{32}$');$x=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($b.ConnectionString);$x['Initial Catalog']=$name;$c=[Microsoft.Data.SqlClient.SqlConnection]::new($x.ConnectionString);$c.Open();$connections.Add($c);if($optIn){Exec $c "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;"};return $c}
 function Fixture([string]$prefix='OptionCIsolated_',[bool]$optIn=$true){$name=$prefix+[guid]::NewGuid().ToString('N');$dbs.Add($name);Exec $admin "CREATE DATABASE [$name]";$c=OpenDb $name $optIn;Exec $c $boot;return $c}
 function DraftSql([int]$n,[string]$phase){return [IO.File]::ReadAllText((Join-Path $draft ('1800_{0:000}/{1}.sql' -f $n,$phase)))}
 function Pristine($c){Assert ((Scalar $c $catalog) -eq $reference.initial);Assert ((Scalar $c "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber<>N'1.8.0-010'") -eq '0');Assert ((Scalar $c "SELECT COUNT(*) FROM sys.tables WHERE name=N'FieldVisitMigrationLedger'") -eq '0');Assert ((Scalar $c "SELECT APPLOCK_TEST(N'public',N'FieldVisit.SchemaMigration',N'Exclusive',N'Session')") -eq '1')}
 if($Mode -eq 'Reference'){
  $case='ReferenceBootstrap';$c=Fixture;$h=Scalar $c $history;$initial=Scalar $c $catalog;Exec $c $ledger;$emptyLedger=Scalar $c $catalog;$steps=@()
  foreach($n in @(11,12,14,15,16,17,18,19,20)){
   $case="Reference$n";Exec $c (DraftSql $n 'Preflight');Exec $c (DraftSql $n 'Up');if($n -eq 14){Exec $c (DraftSql $n 'Backfill')};Exec $c (DraftSql $n 'Verify')
   Assert ((Scalar $c $history) -eq $h);$steps+=@{id=$n;catalog=Scalar $c $catalog};Pass $case
  }
  @{contract='g1-scoped-promotion-reference-v1';originRc='f29affb3299828f45b1f131321779e8bea2f86a8';fixture='SYNTHETIC_SCOPED_MIGRATION_CONTRACT_NOT_UAT_OR_150_BASELINE';initial=$initial;emptyLedger=$emptyLedger;steps=$steps} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $repo 'artifacts/observed-reference.json')
 }else{
  $reference=Get-Content (Join-Path $p 'reference.json') -Raw | ConvertFrom-Json
  $promotion=[IO.File]::ReadAllText((Join-Path $p 'Promotion.sql'));$verify=[IO.File]::ReadAllText((Join-Path $p 'Verify.sql'))
  # TEST_CASES
  $case='PositiveAtomicChain';$c=Fixture;$before=Scalar $c $history;Exec $c $promotion;Exec $c $verify;Assert ((Scalar $c $history) -eq $before);Pass $case
  @{contract='g1-promotion-real-sql-v1';sourceSha=$env:GITHUB_SHA;rcSource='f29affb3299828f45b1f131321779e8bea2f86a8';cases=$results;uatExecuted=$false;automaticRetry=$false} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $repo 'artifacts/promotion-cases.json')
 }
}catch{Write-Output "PromotionCase=$case; Status=STOP; Details=SUPPRESSED;";$line=$_.InvocationInfo.ScriptLineNumber;$code=Code $_.Exception;Write-Output "HarnessDiagnostic=STOP; Line=$line; ProviderCode=$code;";exit 1}
finally{foreach($c in $connections){$c.Dispose()};if($null -ne $admin){foreach($name in $dbs){try{Exec $admin "ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name];"}catch{Write-Output 'Cleanup=STOP; Details=SUPPRESSED;'}};$admin.Dispose()}}
