$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$base='feb244d480d35ba7dbadc4d7ae796e8875d705e3'
$sqlPath='docs/release/optionc-canonical-sql-draft/candidate/1800_020/Up.sql'
$oldBlob='7ccd3aadb8b1d2c1ffd0a7c88b0c0497df8ab6f2'
$cases=[Collections.Generic.List[object]]::new()
$connections=[Collections.Generic.List[object]]::new()
$admin=$null
function Assert([bool]$ok){if(-not $ok){throw 'ASSERT_STOP'}}
function Exec($c,[string]$sql){$cmd=$c.CreateCommand();try{$cmd.CommandTimeout=20;$cmd.CommandText=$sql;[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}
function Scalar($c,[string]$sql){$cmd=$c.CreateCommand();try{$cmd.CommandTimeout=20;$cmd.CommandText=$sql;return $cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function SqlCode($e){while($null -ne $e){if($e -is [Microsoft.Data.SqlClient.SqlException]){return [int]$e.Number};$e=$e.InnerException};return -1}
function Expect547($c,[string]$sql){$rejected=$false;try{Exec $c $sql}catch{if((SqlCode $_.Exception)-ne 547){throw 'WRONG_REJECTION'};$rejected=$true};Assert $rejected}
function Pass([string]$id){$cases.Add(@{id=$id;status='PASS'});Write-Output "ImportCompatibilityCase=$id; Status=PASS;"}
try{
 Assert ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_REPOSITORY -eq 'Terry4410/field-visit-mileage-system')
 Assert ($env:GITHUB_REF -eq 'refs/heads/work/uat010-import-confirming-20261024')
 Assert (-not [string]::IsNullOrEmpty($env:LOCAL_SQL_PASSWORD))
 $root=(Resolve-Path "$PSScriptRoot/../..").Path
 Set-Location $root
 $blob=(& git rev-parse ($base+':'+$sqlPath) 2>$null)
 Assert ($LASTEXITCODE -eq 0 -and $blob -eq $oldBlob)
 $oldSql=((& git show ($base+':'+$sqlPath) 2>$null)-join "`n")+"`n"
 Assert ($LASTEXITCODE -eq 0)
 $newSql=[IO.File]::ReadAllText((Join-Path $root $sqlPath))
 $needle="N'Previewed',N'Confirmed',N'PartiallyFailed',"
 Assert ([regex]::Matches($oldSql,[regex]::Escape($needle)).Count -eq 1)
 Assert ($newSql.Replace("`r`n","`n") -eq $oldSql.Replace($needle,"N'Previewed',N'Confirming',N'Confirmed',N'PartiallyFailed',"))
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop | Out-Null
 $builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
 $builder['Data Source']='127.0.0.1,1433';$builder['Initial Catalog']='master'
 $builder['User ID']='sa';$builder['Password']=$env:LOCAL_SQL_PASSWORD
 $builder['Encrypt']=$true;$builder['TrustServerCertificate']=$true
 $builder['Pooling']=$false;$builder['Connect Timeout']=3;$builder['ConnectRetryCount']=0
 Assert ($builder.DataSource -eq '127.0.0.1,1433' -and $builder.InitialCatalog -eq 'master')
 for($i=0;$i -lt 25;$i++){
  $candidate=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
  try{$candidate.Open();$admin=$candidate;break}catch{$candidate.Dispose();Start-Sleep -Seconds 1}
 }
 Assert ($null -ne $admin)
 Assert ((Scalar $admin "SELECT CASE WHEN CONVERT(int,SERVERPROPERTY('EngineEdition'))=3 AND CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))=16 AND CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE N'Developer Edition%' THEN 1 ELSE 0 END") -eq 1)
 function Fixture([bool]$existingConfirming){
  $name='OptionCIsolated_'+[guid]::NewGuid().ToString('N')
  Assert ($name -cmatch '^OptionCIsolated_[0-9a-f]{32}$')
  Exec $admin "CREATE DATABASE [$name];"
  $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($builder.ConnectionString);$b['Initial Catalog']=$name
  $c=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString);$c.Open();$connections.Add($c)
  Exec $c @'
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(400) NOT NULL,AppliedAt datetime2 NOT NULL,AppliedBy nvarchar(128) NOT NULL);
INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'SYNTHETIC_PHASE020_ONLY',CONVERT(datetime2,N'2020-01-01'),N'isolated-fixture');
CREATE TABLE dbo.ImportBatches(ImportBatchId uniqueidentifier NOT NULL PRIMARY KEY,Status nvarchar(30) NOT NULL CONSTRAINT CK_ImportBatches_Status CHECK(Status IN(N'Previewed',N'Confirming',N'Confirmed',N'PartiallyFailed')));
CREATE TABLE dbo.ImportBatchItems(ImportBatchItemId bigint IDENTITY NOT NULL PRIMARY KEY,ImportBatchId uniqueidentifier NOT NULL,Status nvarchar(30) NOT NULL CONSTRAINT CK_ImportBatchItems_Status CHECK(Status IN(N'Valid',N'Error',N'Applied',N'Failed')),CONSTRAINT FK_ImportBatchItems_Batch FOREIGN KEY(ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId));
INSERT dbo.ImportBatches VALUES(CONVERT(uniqueidentifier,N'00000000-0000-0000-0000-000000000001'),N'Previewed');
'@
  if($existingConfirming){Exec $c "INSERT dbo.ImportBatches VALUES(CONVERT(uniqueidentifier,N'00000000-0000-0000-0000-000000000002'),N'Confirming');"}
  return $c
 }
 # Frozen OLD SQL is a negative control; it is not changed or reclassified.
 $d=Fixture $true
 Expect547 $d $oldSql
 Assert ((Scalar $d 'SELECT COUNT(*) FROM dbo.SchemaVersions') -eq 1)
 Assert ((Scalar $d "SELECT COUNT(*) FROM dbo.ImportBatches WHERE Status=N'Confirming'") -eq 1)
 Pass 'IC01_OLD_DDL_REJECTS_EXISTING_CONFIRMING'
 $d=Fixture $false;Exec $d $oldSql
 Expect547 $d "UPDATE dbo.ImportBatches SET Status=N'Confirming' WHERE Status=N'Previewed';"
 Pass 'IC02_OLD_020_REJECTS_EXISTING_WRITER'
 # Patched SQL exercises only this demonstrated phase020 compatibility delta.
 $d=Fixture $true
 $before=[string](Scalar $d "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2)")
 Exec $d $newSql
 Assert ((Scalar $d "SELECT COUNT(*) FROM dbo.ImportBatches WHERE Status=N'Confirming'") -eq 1)
 Pass 'IC03_PATCH_PRESERVES_EXISTING_CONFIRMING_ROW'
 $claim="UPDATE dbo.ImportBatches SET Status=N'Confirming' WHERE ImportBatchId=CONVERT(uniqueidentifier,N'00000000-0000-0000-0000-000000000001') AND Status=N'Previewed'; SELECT @@ROWCOUNT;"
 Assert ((Scalar $d $claim) -eq 1);Assert ((Scalar $d $claim) -eq 0)
 Pass 'IC04_PATCH_ACCEPTS_EXISTING_CLAIM_PATTERN'
 Exec $d "INSERT dbo.ImportBatches VALUES(NEWID(),N'Previewed'),(NEWID(),N'Confirmed'),(NEWID(),N'PartiallyFailed'); INSERT dbo.ImportBatchItems(ImportBatchId,Status) SELECT b.ImportBatchId,s.Status FROM (SELECT TOP(1) ImportBatchId FROM dbo.ImportBatches ORDER BY ImportBatchId) b CROSS JOIN (VALUES(N'Valid'),(N'Error'),(N'Applied'),(N'Failed')) s(Status);"
 Pass 'IC05_PREVIOUS_ALLOWED_STATUSES_RETAINED'
 Exec $d "INSERT dbo.ImportBatches VALUES(NEWID(),N'PartiallySubmittedForReview'),(NEWID(),N'SubmittedForReview'); INSERT dbo.ImportBatchItems(ImportBatchId,Status) SELECT TOP(1) ImportBatchId,N'SubmittedForReview' FROM dbo.ImportBatches ORDER BY ImportBatchId;"
 Pass 'IC06_REVIEW_STATUSES_STILL_ACCEPTED'
 Expect547 $d "INSERT dbo.ImportBatches VALUES(NEWID(),N'UNAPPROVED_UNKNOWN');"
 Expect547 $d "INSERT dbo.ImportBatchItems(ImportBatchId,Status) SELECT TOP(1) ImportBatchId,N'UNAPPROVED_UNKNOWN' FROM dbo.ImportBatches ORDER BY ImportBatchId;"
 Pass 'IC07_UNKNOWN_STATUSES_STILL_REJECTED'
 $after=[string](Scalar $d "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010' ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2)")
 Assert ($before -eq $after)
 Assert ((Scalar $d "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N'1.8.0-020',N'1.8.0-EXCEL-CANONICAL-STATUS')") -eq 2)
 Assert ((Scalar $d 'SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1') -eq 0)
 Pass 'IC08_HISTORY_MARKER_AND_TRUST_PRESERVED'
 Assert ($cases.Count -eq 8)
 $out=Join-Path $root 'artifacts/import-confirming';[void][IO.Directory]::CreateDirectory($out)
 $receipt=@{contract='uat010-import-confirming-delta-v1';sourceSha=$env:GITHUB_SHA;baseSha=$base;oldSqlBlob=$oldBlob;sqlSha256=(Get-FileHash (Join-Path $root $sqlPath) -Algorithm SHA256).Hash.ToLowerInvariant();caseCount=8;cases=$cases.ToArray();localSql2022DeveloperOnly=$true;azureAccess=$false;liveSqlAccess=$false;promotionPackageAligned=$false;releaseGate='HOLD'}
 [IO.File]::WriteAllText((Join-Path $out 'result.json'),($receipt|ConvertTo-Json -Depth 6)+"`n",[Text.UTF8Encoding]::new($false))
 Write-Output 'IMPORT_CONFIRMING_DELTA=PASS; Cases=8; ReleaseGate=HOLD; AzureAccess=NONE;'
}catch{
 Write-Output 'IMPORT_CONFIRMING_DELTA=HOLD; Details=SUPPRESSED;'
 exit 1
}finally{
 foreach($c in $connections){$c.Dispose()}
 if($null -ne $admin){$admin.Dispose()}
}
