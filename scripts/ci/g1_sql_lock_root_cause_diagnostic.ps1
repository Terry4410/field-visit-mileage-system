$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$connections=[Collections.Generic.List[object]]::new()
$databases=[Collections.Generic.List[string]]::new()
$report=[ordered]@{
  contract='g1-sql-lock-root-cause-v1'
  sourceSha=$env:GITHUB_SHA
  frozenParent=$env:FROZEN_SOURCE_SHA
  provider='SQL Server 2022 Developer / disposable local container'
  experimentA=$null
  experimentB=$null
  classification='INCONCLUSIVE_WITH_EXACT_MISSING_EVIDENCE'
  sharedEnvironmentAccess=$false
  candidateSqlChanged=$false
}

function Assert([bool]$condition,[string]$tag){if(-not $condition){throw $tag}}
function SqlCode($error){while($null -ne $error){if($error -is [Microsoft.Data.SqlClient.SqlException]){return [int]$error.Number};$error=$error.InnerException};return -99999}
function ExecCount($connection,[string]$sql,[int]$timeout=120){$command=$connection.CreateCommand();try{$command.CommandTimeout=$timeout;$command.CommandText=$sql;return [int]$command.ExecuteNonQuery()}finally{$command.Dispose()}}
function Scalar($connection,[string]$sql){$command=$connection.CreateCommand();try{$command.CommandTimeout=30;$command.CommandText=$sql;return $command.ExecuteScalar()}finally{$command.Dispose()}}
function Rows($connection,[string]$sql){$command=$connection.CreateCommand();try{$command.CommandTimeout=30;$command.CommandText=$sql;$reader=$command.ExecuteReader();$items=@();while($reader.Read()){$item=[ordered]@{};for($i=0;$i -lt $reader.FieldCount;$i++){$item[$reader.GetName($i)]=if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)}};$items+=[pscustomobject]$item};$reader.Close();return @($items)}finally{$command.Dispose()}}
function OpenDb([string]$database,[bool]$optIn=$false){Assert ($database -match '^(G1LockDiag_|OptionCIsolated_)[0-9a-f]{32}$') 'TARGET_GUARD_STOP';$builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($script:base.ConnectionString);$builder['Initial Catalog']=$database;$builder['Pooling']=$false;$connection=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$connection.Open();$connections.Add($connection);if($optIn){[void](ExecCount $connection "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;")};return $connection}
function DropDb([string]$database){if($database -notmatch '^(G1LockDiag_|OptionCIsolated_)[0-9a-f]{32}$'){return};try{[void](ExecCount $script:admin "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END") }catch{Write-Output 'DiagnosticCleanup=STOP; Details=SUPPRESSED;'}}
function HoldGate($connection,[string]$resource){$result=Scalar $connection "DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'$resource',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;SELECT @r;";Assert ([int]$result -ge 0) 'GATE_HOLD_STOP'}
function ReleaseGate($connection,[string]$resource){[void](Scalar $connection "DECLARE @r int;EXEC @r=sys.sp_releaseapplock @Resource=N'$resource',@LockOwner=N'Session';SELECT @r;")}
function WaitGate($observer,[int]$promotionSession,[int]$controllerSession,[System.Threading.Tasks.Task]$task){for($i=0;$i -lt 500;$i++){if([int](Scalar $observer "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id=$promotionSession AND blocking_session_id=$controllerSession AND database_id=DB_ID()") -eq 1){return $true};if($task.IsCompleted){return $false};Start-Sleep -Milliseconds 20};return $false}
function Snapshot($observer,[int]$promotionSession,[int]$objectId,[object]$initialTransaction){
  $transaction=Scalar $observer "SELECT TOP(1) st.transaction_id FROM sys.dm_tran_session_transactions st JOIN sys.dm_tran_database_transactions dt ON dt.transaction_id=st.transaction_id WHERE st.session_id=$promotionSession AND dt.database_id=DB_ID() ORDER BY st.transaction_id;"
  $openCount=Scalar $observer "SELECT open_transaction_count FROM sys.dm_exec_sessions WHERE session_id=$promotionSession;"
  $locks=Rows $observer "SELECT resource_subtype,request_mode,request_status,request_owner_type,request_owner_id FROM sys.dm_tran_locks WHERE request_session_id=$promotionSession AND resource_database_id=DB_ID() AND resource_type=N'OBJECT' AND resource_associated_entity_id=$objectId AND request_status=N'GRANT';"
  $base=@($locks|Where-Object{[string]::IsNullOrEmpty([string]$_.resource_subtype)})
  $conflicting=@($base|Where-Object{$_.request_mode -in @('X','Sch-M') -and $_.request_owner_type -eq 'TRANSACTION' -and [string]$_.request_owner_id -eq [string]$transaction})
  $allStrong=@($locks|Where-Object{$_.request_mode -in @('X','Sch-M')})
  $subtypes=@($locks|Where-Object{-not [string]::IsNullOrEmpty([string]$_.resource_subtype)}|ForEach-Object{"$($_.request_mode):$($_.resource_subtype):$($_.request_owner_type)"}|Sort-Object -Unique)
  return [ordered]@{
    activeTransaction=($null -ne $transaction)
    sameTransaction=($null -eq $initialTransaction -or [string]$transaction -eq [string]$initialTransaction)
    openTransaction=([int]$openCount -gt 0)
    baseConflictingObjectLock=($conflicting.Count -gt 0)
    broadStrongObjectLock=($allStrong.Count -gt 0)
    subtypeOwnerClasses=$subtypes
    _transaction=$transaction
  }
}
function PublicSnapshot($snapshot){return [ordered]@{activeTransaction=$snapshot.activeTransaction;sameTransaction=$snapshot.sameTransaction;openTransaction=$snapshot.openTransaction;baseConflictingObjectLock=$snapshot.baseConflictingObjectLock;broadStrongObjectLock=$snapshot.broadStrongObjectLock;subtypeOwnerClasses=$snapshot.subtypeOwnerClasses}}
function WriterOutcome($connection,[string]$sql){try{$affected=ExecCount $connection $sql 30;return [ordered]@{kind=if($affected -eq 0){'NORMAL_ZERO'}elseif($affected -eq 1){'NORMAL_ONE'}else{'NORMAL_UNEXPECTED'};affectedRows=$affected;providerCode=$null}}catch{$code=SqlCode $_.Exception;return [ordered]@{kind=if($code -eq 1222){'SQL_EXCEPTION_1222'}else{'OTHER_SQL_EXCEPTION'};affectedRows=$null;providerCode=if($code -eq 1222){1222}else{'SUPPRESSED'}}}}

try{
  Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
  Assert ($env:GITHUB_ACTIONS -eq 'true') 'RUNNER_GUARD_STOP'
  Assert ($env:GITHUB_SHA -match '^[0-9a-f]{40}$') 'SOURCE_GUARD_STOP'
  $base=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
  Assert ($base.DataSource -in @('localhost,1433','127.0.0.1,1433') -and $base.InitialCatalog -eq 'master' -and $base.UserID -eq 'sa' -and -not $base.IntegratedSecurity) 'CONNECTION_GUARD_STOP'
  $base['Pooling']=$false;$base['Connect Timeout']=5;$base['ConnectRetryCount']=0
  $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($base.ConnectionString);$admin.Open()
  $repo=(Resolve-Path "$PSScriptRoot/../..").Path
  $promotionPath=Join-Path $repo 'docs/release/g1-execution/promotion/Promotion.sql'
  $verifyPath=Join-Path $repo 'docs/release/g1-execution/promotion/Verify.sql'
  Assert ((Get-FileHash $promotionPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $env:PROMOTION_SHA256) 'PROMOTION_HASH_STOP'
  Assert ((Get-FileHash $verifyPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $env:VERIFY_SHA256) 'VERIFY_HASH_STOP'

  # Experiment A: native SQL Server baseline.
  $database='G1LockDiag_'+[guid]::NewGuid().ToString('N');$databases.Add($database);[void](ExecCount $admin "CREATE DATABASE [$database]")
  $sessionA=OpenDb $database;$sessionB=OpenDb $database;$observer=OpenDb $database
  [void](ExecCount $sessionA 'CREATE TABLE dbo.LockProbe(Id int NOT NULL PRIMARY KEY,ProbeValue int NOT NULL);INSERT dbo.LockProbe VALUES(1,0);')
  $aSpid=[int](Scalar $sessionA 'SELECT @@SPID');$bSpid=[int](Scalar $sessionB 'SELECT @@SPID')
  Assert ($aSpid -ne $bSpid -and [int](Scalar $sessionB 'SELECT DB_ID()') -eq [int](Scalar $sessionA 'SELECT DB_ID()')) 'BASELINE_SESSION_STOP'
  [void](ExecCount $sessionA 'BEGIN TRANSACTION;DECLARE @n bigint;SELECT @n=COUNT_BIG(*) FROM dbo.LockProbe WITH(TABLOCKX,HOLDLOCK);')
  $probeObject=[int](Scalar $observer "SELECT OBJECT_ID(N'dbo.LockProbe')")
  $aSnapshot=Snapshot $observer $aSpid $probeObject $null
  $aWriter=WriterOutcome $sessionB 'SET LOCK_TIMEOUT 0;UPDATE dbo.LockProbe SET ProbeValue=1 WHERE Id=1;'
  [void](ExecCount $sessionA 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;')
  $aChanged=([int](Scalar $observer 'SELECT ProbeValue FROM dbo.LockProbe WHERE Id=1') -ne 0)
  $aPass=($aSnapshot.baseConflictingObjectLock -and $aWriter.kind -eq 'SQL_EXCEPTION_1222' -and -not $aChanged)
  $report.experimentA=[ordered]@{result=if($aPass){'PASS'}else{'FAIL'};distinctSessions=$true;sameDatabase=$true;lock=(PublicSnapshot $aSnapshot);writer=$aWriter;syntheticRowChanged=$aChanged}
  Write-Output "DiagnosticCase=NativeLockBaseline; Status=$(if($aPass){'PASS'}else{'FAIL'}); Writer=$($aWriter.kind); RowChanged=$aChanged;"
  if(-not $aPass){$report.classification='SQL_SERVER_ENVIRONMENT_DEFECT_PROVEN';throw 'BASELINE_EXPECTATION_STOP'}
  DropDb $database

  # Experiment B: unchanged frozen Promotion SQL, with in-memory observation gates only.
  $database='OptionCIsolated_'+[guid]::NewGuid().ToString('N');$databases.Add($database);[void](ExecCount $admin "CREATE DATABASE [$database]")
  $promotionSession=OpenDb $database $true;$observer=OpenDb $database $true;$writer=OpenDb $database $true
  $old=Get-Content (Join-Path $repo 'scripts/ci/run_full_candidate_migration.ps1') -Raw
  $match=[regex]::Matches($old,"(?s)\`$boot=@'\r?\n(.*?)\r?\n'@");Assert ($match.Count -eq 1) 'FIXTURE_SOURCE_STOP';[void](ExecCount $promotionSession $match[0].Groups[1].Value)
  $promotion=[IO.File]::ReadAllText($promotionPath)
  $lastLock="EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.ImportBatchItems WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;"
  $resources=@('G1.Diagnostic.AfterLocks','G1.Diagnostic.Before014','G1.Diagnostic.After014')
  $controllers=@();foreach($resource in $resources){$controller=OpenDb $database $true;HoldGate $controller $resource;$controllers+=,$controller}
  function GateSql([string]$resource,[string]$suffix){return "DECLARE @G1DiagnosticGate$suffix int;EXEC @G1DiagnosticGate$suffix=sys.sp_getapplock @Resource=N'$resource',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000;IF @G1DiagnosticGate$suffix<0 THROW 52991,N'G1_DIAGNOSTIC_GATE_STOP',1;"}
  $instrumented=$promotion.Replace($lastLock,$lastLock+(GateSql $resources[0] 'A')).Replace('-- G1_STEP_014_BEGIN',(GateSql $resources[1] 'B')+'-- G1_STEP_014_BEGIN').Replace('-- G1_STEP_014_END',(GateSql $resources[2] 'C')+'-- G1_STEP_014_END')
  Assert ($instrumented -ne $promotion -and ([regex]::Matches($instrumented,'G1_DIAGNOSTIC_GATE_STOP')).Count -eq 3) 'GATE_INSTRUMENTATION_STOP'
  $promotionSpid=[int](Scalar $promotionSession 'SELECT @@SPID');$writerSpid=[int](Scalar $writer 'SELECT @@SPID');Assert ($promotionSpid -ne $writerSpid) 'PROMOTION_SESSION_STOP'
  $locationObject=[int](Scalar $observer "SELECT OBJECT_ID(N'dbo.Locations')")
  $command=$promotionSession.CreateCommand();$command.CommandTimeout=180;$command.CommandText=$instrumented;$task=$command.ExecuteNonQueryAsync()
  $stageResults=@();$firstTransaction=$null;$writerResult=$null
  try{
    for($index=0;$index -lt 3;$index++){
      $controlSpid=[int](Scalar $controllers[$index] 'SELECT @@SPID')
      Assert (WaitGate $observer $promotionSpid $controlSpid $task) 'PROMOTION_GATE_NOT_OBSERVED'
      $snapshot=Snapshot $observer $promotionSpid $locationObject $firstTransaction
      if($null -eq $firstTransaction){$firstTransaction=$snapshot._transaction;$snapshot.sameTransaction=$true}
      $stageResults+=,[ordered]@{stage=$resources[$index];observation=(PublicSnapshot $snapshot)}
      Write-Output "DiagnosticCase=$($resources[$index]); Status=PASS; ActiveTransaction=$($snapshot.activeTransaction); SameTransaction=$($snapshot.sameTransaction); BaseBlockingLock=$($snapshot.baseConflictingObjectLock); BroadStrongLock=$($snapshot.broadStrongObjectLock);"
      if($index -eq 2){$writerResult=WriterOutcome $writer "SET LOCK_TIMEOUT 0;UPDATE dbo.Locations SET MasterNote=N'Synthetic diagnostic' WHERE LocationId=10;"}
      ReleaseGate $controllers[$index] $resources[$index]
    }
    [void]$task.GetAwaiter().GetResult()
  }finally{
    for($index=0;$index -lt 3;$index++){try{ReleaseGate $controllers[$index] $resources[$index]}catch{}}
    $command.Dispose()
  }
  $rowChanged=([string](Scalar $observer "SELECT CASE WHEN MasterNote=N'Synthetic diagnostic' THEN N'1' ELSE N'0' END FROM dbo.Locations WHERE LocationId=10") -eq '1')
  $final=$stageResults[2].observation
  $report.experimentB=[ordered]@{result='OBSERVED';distinctPromotionAndWriterSessions=$true;stages=$stageResults;writer=$writerResult;syntheticRowChanged=$rowChanged;promotionCompleted=$true}
  if($writerResult.kind -eq 'SQL_EXCEPTION_1222' -and -not $rowChanged -and $final.baseConflictingObjectLock){$report.classification='TEST_HARNESS_DEFECT_PROVEN'}
  elseif($writerResult.kind -eq 'NORMAL_ONE' -and $rowChanged -and (-not $final.baseConflictingObjectLock -or -not $final.sameTransaction)){$report.classification='MIGRATION_LOCK_LIFETIME_DEFECT_PROVEN'}
  else{$report.classification='INCONCLUSIVE_WITH_EXACT_MISSING_EVIDENCE'}
  Write-Output "DiagnosticCase=PromotionWriter; Status=PASS; Writer=$($writerResult.kind); RowChanged=$rowChanged; Classification=$($report.classification);"
}catch{
  if($_.Exception.Message -notin @('BASELINE_EXPECTATION_STOP')){Write-Output "DiagnosticCase=Execution; Status=STOP; ProviderCode=$(if((SqlCode $_.Exception) -in @(1222)){SqlCode $_.Exception}else{'SUPPRESSED'});"}
}finally{
  foreach($connection in $connections){try{$connection.Dispose()}catch{}}
  if($null -ne $admin){foreach($database in @($databases)){DropDb $database};$admin.Dispose()}
  $artifact=Join-Path $repo 'artifacts';New-Item -ItemType Directory -Path $artifact -Force|Out-Null
  $report|ConvertTo-Json -Depth 10|Set-Content (Join-Path $artifact 'g1-sql-lock-root-cause.json') -Encoding utf8
  Write-Output "DiagnosticFinal=$($report.classification); SourceSha=$($report.sourceSha); SharedEnvironmentAccess=FALSE;"
}
