$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$case='INIT';$admin=$null;$probe=$null;$dbName='';$created=$false
function Need([bool]$ok){if(-not $ok){throw 'API_ACL_ASSERTION_STOP'}}
function Exec([object]$conn,[string]$sql){$cmd=$conn.CreateCommand();try{$cmd.CommandTimeout=20;$cmd.CommandText=$sql;[void]$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}
function Scalar([object]$conn,[string]$sql){$cmd=$conn.CreateCommand();try{$cmd.CommandTimeout=20;$cmd.CommandText=$sql;return $cmd.ExecuteScalar()}finally{$cmd.Dispose()}}
function Positive([string]$table,[string]$action,[string]$sql){
  $case=$table+'_'+$action
  $value=Scalar $probe ("EXECUTE AS USER=N'FieldVisitRuntimeTest'; DECLARE @n int=0; "+$sql+" SET @n=@@ROWCOUNT; REVERT; SELECT @n;")
  Need ([int]$value -eq 1)
  Write-Output ("ACL_PositiveDml="+$table+"/"+$action+"; RowsAffected=1; Status=PASS;")
}
function Refused([string]$table,[string]$action,[string]$sql){
  $case=$table+'_'+$action
  $quoted=$sql.Replace("'","''")
  $value=Scalar $probe ("EXECUTE AS USER=N'FieldVisitRuntimeTest'; DECLARE @e int=0; BEGIN TRY EXEC sys.sp_executesql N'"+$quoted+"'; SET @e=0; END TRY BEGIN CATCH SET @e=ERROR_NUMBER(); END CATCH; REVERT; SELECT @e;")
  Need ([int]$value -eq 229)
  Write-Output ("ACL_HistoryDml="+$table+"/"+$action+"; SqlError=229; Status=PASS;")
}
try{
  $case='ENVIRONMENT'
  Need ($env:GITHUB_ACTIONS -eq 'true')
  Need (-not $env:UAT_SQL_CONNECTION -and -not $env:PRODUCTION_SQL_CONNECTION)
  Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
  $builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
  Need ($builder.DataSource -in @('localhost,1433','127.0.0.1,1433') -and $builder.InitialCatalog -ceq 'master' -and $builder.UserID -ceq 'sa' -and -not $builder.IntegratedSecurity)
  $builder['Pooling']=$false;$builder['Connect Timeout']=5;$builder['ConnectRetryCount']=0
  $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$admin.Open()
  $dbName='ApiAclIsolated_'+[guid]::NewGuid().ToString('N').ToLowerInvariant()
  Need ($dbName -match '^ApiAclIsolated_[0-9a-f]{32}$')
  Exec $admin "CREATE DATABASE [$dbName];";$created=$true
  $builder['Initial Catalog']=$dbName;$probe=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$probe.Open()

  $matrixPath=Join-Path (Resolve-Path '.').Path 'docs/release/g1-execution/promotion/API-RUNTIME-SQL-CRUD-MATRIX.csv'
  $matrix=Import-Csv -LiteralPath $matrixPath
  Need ($matrix.Count -ge 20)
  $tables=@($matrix | ForEach-Object { $_.table } | Sort-Object -Unique)
  Need ($tables.Count -eq $matrix.Count)
  foreach($t in $tables){Need ($t -match '^[A-Za-z][A-Za-z0-9_]{0,127}$')}

  $ddl=[System.Collections.Generic.List[string]]::new()
  foreach($t in $tables){$ddl.Add("CREATE TABLE dbo.[$t](AclProbeId int IDENTITY(1,1) NOT NULL PRIMARY KEY, Marker nvarchar(100) NOT NULL); INSERT dbo.[$t](Marker) VALUES(N'admin-seed');")}
  $ddl.Add("CREATE USER [FieldVisitRuntimeTest] WITHOUT LOGIN;")
  foreach($row in $matrix){
    $t=$row.table
    if($row.SELECT -eq 'YES'){ $ddl.Add("GRANT SELECT ON OBJECT::dbo.[$t] TO [FieldVisitRuntimeTest];") }
    $perms=[System.Collections.Generic.List[string]]::new();if($row.INSERT -eq 'YES'){$perms.Add('INSERT')};if($row.UPDATE -eq 'YES'){$perms.Add('UPDATE')};if($row.DELETE -eq 'YES'){$perms.Add('DELETE')}
    if($perms.Count -gt 0){$ddl.Add("GRANT $($perms -join ', ') ON OBJECT::dbo.[$t] TO [FieldVisitRuntimeTest];")}
    if($row.schema_phase -like 'PROTECTED*'){$ddl.Add("DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.[$t] TO [FieldVisitRuntimeTest];")}
  }
  Exec $probe ($ddl -join "`n")

  $case='RUNTIME_IDENTITY'
  $identity=Scalar $probe "EXECUTE AS USER=N'FieldVisitRuntimeTest'; SELECT CONCAT(USER_NAME(),N'|',ISNULL(IS_ROLEMEMBER(N'db_owner'),0),N'|',ISNULL(IS_ROLEMEMBER(N'db_datawriter'),0)); REVERT;"
  Need ([string]$identity -ceq 'FieldVisitRuntimeTest|0|0')
  Write-Output 'ACL_RuntimePrincipal=NON_SA; db_owner=0; db_datawriter=0; Status=PASS;'

  foreach($row in $matrix){
    $t=$row.table
    if($row.SELECT -eq 'YES'){
      $n=Scalar $probe "EXECUTE AS USER=N'FieldVisitRuntimeTest'; SELECT COUNT(*) FROM dbo.[$t]; REVERT;"
      Need ([int]$n -ge 1)
    }
    if($row.INSERT -eq 'YES'){Positive $t 'INSERT' "INSERT dbo.[$t](Marker) VALUES(N'runtime-insert');"}
    if($row.UPDATE -eq 'YES'){Positive $t 'UPDATE' "UPDATE dbo.[$t] SET Marker=N'runtime-update' WHERE AclProbeId=(SELECT MIN(AclProbeId) FROM dbo.[$t]);"}
    if($row.DELETE -eq 'YES'){Positive $t 'DELETE' "DELETE TOP (1) FROM dbo.[$t];"}
  }
  $protected=@($matrix | Where-Object {$_.schema_phase -like 'PROTECTED*'})
  Need ($protected.Count -ge 3)
  foreach($row in $protected){
    $t=$row.table
    Refused $t 'INSERT' "INSERT dbo.[$t](Marker) VALUES(N'forbidden');"
    Refused $t 'UPDATE' "UPDATE dbo.[$t] SET Marker=N'forbidden' WHERE AclProbeId=(SELECT MIN(AclProbeId) FROM dbo.[$t]);"
    Refused $t 'DELETE' "DELETE TOP (1) FROM dbo.[$t];"
  }
  $case='RECEIPT'
  $out='artifacts/uat010-api-acl';New-Item -ItemType Directory -Force -Path $out | Out-Null
  $matrixSha=(Get-FileHash -Algorithm SHA256 -LiteralPath $matrixPath).Hash.ToLowerInvariant()
  $candidatePath=Join-Path (Resolve-Path '.').Path 'docs/release/g1-execution/promotion/API-RUNTIME-SQL-GRANT-CANDIDATE.sql'
  $candidateSha=(Get-FileHash -Algorithm SHA256 -LiteralPath $candidatePath).Hash.ToLowerInvariant()
  $receipt=[ordered]@{contract='api-runtime-acl-matrix-isolated-v1';sourceSha=$env:GITHUB_SHA;matrixSha256=$matrixSha;grantCandidateSha256=$candidateSha;runtimePrincipal='FieldVisitRuntimeTest';runtimePrincipalIsSa=$false;dbOwner=$false;dbDatawriter=$false;positiveDmlRowsAffectedOne=$true;protectedHistoryDmlErrors=229;matrixTables=$tables.Count;azureAccess=$false;liveSqlAccess=$false;livePermissionCutover='HOLD';uncoveredDynamicApiPaths='See API-RUNTIME-SQL-CRUD-MATRIX.md'}
  $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'receipt.json') -Encoding utf8
  Write-Output ('ApiRuntimeAclIsolatedSql2022=PASS; Tables='+$tables.Count+'; PositiveDml=OBSERVED_ROWS; ProtectedHistoryDml=DENIED; LiveCutover=HOLD;')
}catch{
  Write-Output ('ApiRuntimeAclCase='+$case+'; Status=FAIL; Details=SUPPRESSED;')
  exit 1
}finally{
  if($null -ne $probe){try{Exec $probe 'REVERT;'}catch{};$probe.Dispose()}
  if($null -ne $admin){if($created -and $dbName -match '^ApiAclIsolated_[0-9a-f]{32}$'){try{Exec $admin "ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName];"}catch{}};$admin.Dispose()}
}
