$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$case='START'; $results=[Collections.Generic.List[object]]::new(); $gaps=[Collections.Generic.List[string]]::new()
$databases=[Collections.Generic.List[string]]::new(); $connections=[Collections.Generic.List[object]]::new()
function Assert([bool]$ok) { if(-not $ok){throw 'ASSERTION_STOP'} }
function Exec($c,[string]$sql) { $cmd=$c.CreateCommand();try{$cmd.CommandTimeout=30;$cmd.CommandText=$sql;[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()} }
function Scalar($c,[string]$sql) { $cmd=$c.CreateCommand();try{$cmd.CommandTimeout=30;$cmd.CommandText=$sql;return [string]$cmd.ExecuteScalar()}finally{$cmd.Dispose()} }
function Code($e) { while($null -ne $e){if($e -is [Microsoft.Data.SqlClient.SqlException]){return [int]$e.Number};$e=$e.InnerException};return -99999 }
function Refuse($c,[string]$sql,[int[]]$codes) { try{Exec $c $sql}catch{Assert ((Code $_.Exception) -in $codes);return};throw 'EXPECTED_REFUSAL_MISSING' }
function Pass([string]$id,[string]$outcome='PASS') { $results.Add(@{case=$id;result=$outcome});Write-Output "MigrationCase=$id; Status=$outcome;" }
function Gap([string]$id) { $gaps.Add($id);Pass $id 'KNOWN_PROMOTION_GAP' }
try {
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
 Assert ($env:GITHUB_ACTIONS -eq 'true' -and $b.DataSource -in @('localhost,1433','127.0.0.1,1433') -and $b.InitialCatalog -eq 'master' -and $b.UserID -eq 'sa' -and -not $b.IntegratedSecurity)
 $b.Pooling=$false;$b.ConnectTimeout=5;$b.ConnectRetryCount=0
 $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString);$admin.Open()
 $repo=(Resolve-Path "$PSScriptRoot/../..").Path
 $root=Join-Path $repo 'docs/release/optionc-canonical-sql-draft/candidate'
 $old=Get-Content (Join-Path $PSScriptRoot 'run_full_candidate_migration.ps1') -Raw
 $matches=[regex]::Matches($old,"(?s)\`$boot=@'\r?\n(.*?)\r?\n'@")
 Assert ($matches.Count -eq 1);$boot=$matches[0].Groups[1].Value
 function OpenDb([string]$name) { Assert ($name -match '^(OptionCIsolated_G1_|G1Denied_)[0-9a-f]{32}$');$x=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($b.ConnectionString);$x.InitialCatalog=$name;$c=[Microsoft.Data.SqlClient.SqlConnection]::new($x.ConnectionString);$c.Open();$connections.Add($c);return $c }
 function Fixture([string]$prefix='OptionCIsolated_G1_') { $name=$prefix+[guid]::NewGuid().ToString('N');$databases.Add($name);Exec $admin "CREATE DATABASE [$name]";$c=OpenDb $name;Exec $c $boot;return $c }
 function Sql([int]$n,[string]$phase) { return [IO.File]::ReadAllText((Join-Path $root ('1800_{0:000}/{1}.sql' -f $n,$phase))) }
 function Apply($c,[int]$n) { Exec $c (Sql $n 'Preflight');Exec $c (Sql $n 'Up');if($n -eq 14){Exec $c (Sql $n 'Backfill')};Exec $c (Sql $n 'Verify') }
 # Never exported: normalized metadata and synthetic history are compared in memory.
 $state=@'
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
 (SELECT t.name,c.name AS Col,ty.name AS Typ,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY Tbl,name FOR JSON PATH)+
 (SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.is_unique,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH)+
 (SELECT name,is_disabled,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers ORDER BY name FOR JSON PATH)+
 (SELECT * FROM dbo.SchemaVersions ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2)
'@
 $history=@'
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
 (SELECT * FROM dbo.VisitTrips ORDER BY VisitTripId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.VisitTripStops ORDER BY VisitTripStopId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.VisitTripSnapshots ORDER BY VisitTripSnapshotId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.VisitTripSnapshotStops ORDER BY VisitTripSnapshotStopId FOR JSON PATH,INCLUDE_NULL_VALUES))),2)
'@
 $c=Fixture;$historyBefore=Scalar $c $history
 foreach($n in @(11,12,14,15,16,17,18,19,20)) {
  $case="Step${n}_Preflight";Exec $c (Sql $n 'Preflight')
  $before=Scalar $c $state;$up=Sql $n 'Up';$at=$up.LastIndexOf('COMMIT TRANSACTION;',[StringComparison]::OrdinalIgnoreCase);Assert ($at -gt 0)
  $fault=$up.Insert($at,"THROW 52990,N'G1_TEST_INJECTED_FAILURE',1;`n")
  $case="Step${n}_InjectedRollback";Refuse $c $fault @(52990);Assert ((Scalar $c $state) -eq $before);Assert ((Scalar $c 'SELECT @@TRANCOUNT') -eq '0');Pass $case
  $case="Step${n}_Up";Exec $c $up
  if($n -eq 14) {
   $case='Backfill_InjectedRollback';$before=Scalar $c $state;$backfill=Sql 14 'Backfill';$at=$backfill.LastIndexOf('COMMIT TRANSACTION;',[StringComparison]::OrdinalIgnoreCase)
   Refuse $c ($backfill.Insert($at,"THROW 52990,N'G1_TEST_INJECTED_FAILURE',1;`n")) @(52990)
   Assert ((Scalar $c $state) -eq $before);Assert ((Scalar $c 'SELECT COUNT(*) FROM dbo.LocationPublishedVersions') -eq '0');Pass $case
   Assert ((Scalar $c "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-014'") -eq '1');Gap '014_MARKER_CAN_EXIST_WITHOUT_BACKFILL'
   Exec $c $backfill
   Assert ((Scalar $c 'SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE VersionNumber=1 AND IsCurrent=1 AND ChangeRequestId IS NULL') -eq '2')
   Assert ((Scalar $c "SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE PublishedSha256<>HASHBYTES('SHA2_256',CONVERT(varbinary(max),PublishedJson))") -eq '0')
   Pass 'Version1_CanonicalProjectionAndHash';Refuse $c $backfill @(51432,51435);Pass 'Backfill_ReplayRefused'
  }
  Exec $c (Sql $n 'Verify');Assert ((Scalar $c $history) -eq $historyBefore);Pass "Step${n}_ForwardVerifyHistory"
  $case="Step${n}_PreflightReplayRefused";$before=Scalar $c $state
  Refuse $c (Sql $n 'Preflight') @(51102,51201,51203,51402,51403,51903);Assert ((Scalar $c $state) -eq $before);Pass $case
 }
 $case='Final_TrustedConstraintsAndReserved013'
 Assert ((Scalar $c "SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1") -eq '0')
 Assert ((Scalar $c "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1") -eq '0')
 Assert ((Scalar $c "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-013'") -eq '0');Pass $case
 # Actual fresh-connection reconciliation after committed SQL; client ACK loss is simulated.
 $case='CommittedState_FreshConnection_NoReplay';$peer=OpenDb $c.Database;Assert ((Scalar $peer $state) -eq (Scalar $c $state));Exec $peer (Sql 20 'Verify');Refuse $peer (Sql 20 'Preflight') @(51903);Pass $case
 $case='ObjectWithoutMarker_PreflightGap';Exec $c 'BEGIN TRANSACTION; DELETE dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-020'';'
 try { Exec $c (Sql 20 'Preflight');Gap '020_OBJECTS_WITHOUT_MARKER_PREFLIGHT_ACCEPTS' } finally {Exec $c 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'}
 $case='AlteredColumn_VerifyRefuses';Exec $c "BEGIN TRANSACTION; EXEC sys.sp_rename N'dbo.LocationMergeDetails.SourceTeamId',N'G1MissingColumn',N'COLUMN';"
 try {Refuse $c (Sql 19 'Verify') @(51913);Pass $case}finally{Exec $c 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'}
 $case='MissingTable_VerifyRefuses';Exec $c "BEGIN TRANSACTION; EXEC sys.sp_rename N'dbo.LocationMergeDetails',N'G1MissingTable';"
 try {Refuse $c (Sql 19 'Verify') @(51911);Pass $case}finally{Exec $c 'IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;'}
 $case='Permissions_DdlDenied_NoPartialState';$p=Fixture;Exec $p 'CREATE USER G1Restricted WITHOUT LOGIN; GRANT SELECT ON SCHEMA::dbo TO G1Restricted; DENY CREATE TABLE TO G1Restricted; DENY ALTER ON SCHEMA::dbo TO G1Restricted;'
 $before=Scalar $p $state;Exec $p "EXECUTE AS USER=N'G1Restricted';"
 try {Refuse $p (Sql 11 'Up') @(262,229,2760);Pass $case}finally{Exec $p 'REVERT;'}
 Assert ((Scalar $p $state) -eq $before)
 $case='WrongPredecessors_Refused';foreach($n in @(12,14,15,16,17,18,19,20)){Refuse $p (Sql $n 'Preflight') @(51201,51401,51402,51902);Pass "Step${n}_WrongPredecessorRefused"}
 $case='EmptyVersionLedger_Gap';$empty=Fixture;Exec $empty 'DELETE dbo.SchemaVersions;';Exec $empty (Sql 11 'Up')
 Assert ((Scalar $empty "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-011'") -eq '1');Gap '011_NULL_PREDECESSOR_ACCEPTED'
 $case='MigrationLock_014_Gap';$lockDb=Fixture;Apply $lockDb 11;Apply $lockDb 12
 Exec $lockDb "DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;IF @r<0 THROW 52991,N'G1_TEST_LOCK_FAILED',1;"
 try { $other=OpenDb $lockDb.Database;Exec $other (Sql 14 'Up');Assert ((Scalar $other "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-014'") -eq '1');Gap '014_IGNORES_NEGATIVE_APPLOCK_RETURN' }
 finally {Exec $lockDb "EXEC sys.sp_releaseapplock @Resource=N'FieldVisit.SchemaMigration',@LockOwner=N'Session';"}
 $case='DisposableGuard_WrongDatabase';$denied=Fixture 'G1Denied_'
 foreach($n in @(14,15,16,17,18,19,20)){Refuse $denied (Sql $n 'Preflight') @(51400,51900);Pass "Step${n}_DisposableGuardRefused"}
 $case='Final_HistoryUnchanged';Assert ((Scalar $c $history) -eq $historyBefore);Pass $case
 @{contract='g1-migration-disposable-v1';sourceSha=$env:GITHUB_SHA;rcSource='f29affb3299828f45b1f131321779e8bea2f86a8';sqlServer='DISPOSABLE_SQL_SERVER_2022';fixture='ACCEPTED_SYNTHETIC_010_NOT_UAT_OR_G3_BASELINE';cases=$results;knownPromotionGaps=$gaps;automaticRetry=$false;uatSqlExecuted=$false;decision='HOLD_UAT_PROMOTION'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $repo 'artifacts/migration-cases.json')
} catch { Write-Output "MigrationCase=$case; Status=STOP; Details=SUPPRESSED;";exit 1 }
finally {
 foreach($c in $connections){$c.Dispose()}
 if($null -ne $admin){foreach($db in $databases){try{Exec $admin "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db];"}catch{Write-Output 'DisposableCleanup=STOP; Details=SUPPRESSED;'}};$admin.Dispose()}
}
