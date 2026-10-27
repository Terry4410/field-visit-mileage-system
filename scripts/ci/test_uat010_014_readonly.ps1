param()
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$case='INIT';$admin=$null;$c=$null;$dbName=''
function Require([bool]$condition){ if(-not $condition){throw 'G1_014_ISOLATED_ASSERTION_STOP'} }
function Exec([object]$conn,[string]$sql){
  $cmd=$conn.CreateCommand()
  try { $cmd.CommandTimeout=25; $cmd.CommandText=$sql; [void]$cmd.ExecuteNonQuery() }
  finally { $cmd.Dispose() }
}
function RunRead([object]$conn,[string]$query) {
  $cmd=$conn.CreateCommand()
  try {
    $cmd.CommandTimeout=25;$cmd.CommandText=$query
    $reader=$cmd.ExecuteReader()
    try {
      Require ($reader.FieldCount -eq 2 -and $reader.GetName(0) -ceq 'code' -and $reader.GetName(1) -ceq 'status')
      $rows=[ordered]@{}
      while($reader.Read()){
        Require ($rows.Count -lt 2 -and -not $reader.IsDBNull(0) -and -not $reader.IsDBNull(1))
        $key=[string]$reader.GetString(0)
        Require ($key -in @('014_APPROVED_MASTER_AUTHORITY','014_APPROVED_MASTER_FIELDS') -and -not $rows.Contains($key))
        $state=[string]$reader.GetString(1)
        Require ($state -in @('PASS','HOLD'));$rows[$key]=$state
      }
      Require (-not $reader.NextResult() -and $rows.Count -eq 2)
      return $rows
    } finally { $reader.Dispose() }
  } finally { $cmd.Dispose() }
}
function Check([object]$conn,[string]$sql,[string]$expectedAuthority,[string]$expectedFields){
  $rows=RunRead $conn $sql
  Require ($rows['014_APPROVED_MASTER_AUTHORITY'] -ceq $expectedAuthority)
  Require ($rows['014_APPROVED_MASTER_FIELDS'] -ceq $expectedFields)
  Write-Output ('UAT010014Case='+$case+'; Status=PASS;')
}
function ExpectRefusal([object]$conn,[string]$sql,[int]$code){
  $cmd=$conn.CreateCommand()
  try {
    $cmd.CommandText=$sql;$cmd.CommandTimeout=25
    $observed=0
    try {$reader=$cmd.ExecuteReader();$reader.Dispose()}catch{
      $e=$_.Exception
      while($null -ne $e){
        if($e -is [Microsoft.Data.SqlClient.SqlException]){$observed=[int]$e.Number;break}
        $e=$e.InnerException
      }
    }
    Require ($observed -eq $code)
    Write-Output ('UAT010014Case='+$case+'; Status=PASS;')
  }finally{$cmd.Dispose()}
}
try{
  $case='ENVIRONMENT'
  Require ($env:GITHUB_ACTIONS -eq 'true')
  Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
  $builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
  Require ($builder.DataSource -in @('localhost,1433','127.0.0.1,1433') -and $builder.InitialCatalog -ceq 'master' -and $builder.UserID -ceq 'sa' -and -not $builder.IntegratedSecurity)
  Require (-not $env:UAT_SQL_CONNECTION -and -not $env:PRODUCTION_SQL_CONNECTION)
  $builder['Pooling']=$false;$builder['Connect Timeout']=5;$builder['ConnectRetryCount']=0
  $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$admin.Open()
  $dbName='OptionCIsolated_'+[guid]::NewGuid().ToString('N').ToLowerInvariant()
  Require ($dbName -match '^OptionCIsolated_[0-9a-f]{32}$')
  Exec $admin "CREATE DATABASE [$dbName];"
  $builder['Initial Catalog']=$dbName
  $c=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$c.Open()
  Exec $c "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;"
  Exec $c @'
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) PRIMARY KEY,Description nvarchar(500),AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200));
CREATE TABLE dbo.Organizations(OrganizationId int PRIMARY KEY);
CREATE TABLE dbo.Teams(TeamId int PRIMARY KEY, OrganizationId int NOT NULL);
CREATE TABLE dbo.Users(UserId int PRIMARY KEY, OrganizationId int NOT NULL);
CREATE TABLE dbo.Locations(LocationId int PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NULL,CreatedByUserId int NOT NULL,LocationType nvarchar(20) NOT NULL,LocationName nvarchar(200) NULL,City nvarchar(100) NULL,District nvarchar(100) NULL,Address nvarchar(500) NULL,PlusCode nvarchar(100) NULL,TaxId nvarchar(50) NULL,MasterNote nvarchar(1000) NULL,ApprovalStatus nvarchar(30) NOT NULL,IsActive bit NOT NULL,RowVersion rowversion NOT NULL);
INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'fixture',SYSUTCDATETIME(),N'isolated');
INSERT dbo.Organizations VALUES(1),(2);
INSERT dbo.Teams VALUES(1,1),(2,2);
INSERT dbo.Users VALUES(1,1),(2,2);
INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,LocationType,LocationName,Address,ApprovalStatus,IsActive) VALUES(10,1,1,1,N'Customer',N'Valid location',N'Valid address',N'Approved',1);
'@
  $sql=[IO.File]::ReadAllText((Join-Path (Resolve-Path '.').Path 'artifacts/uat010-014/014-preflight.sql'))
  $case='ValidSource';Check $c $sql 'PASS' 'PASS'
  $case='BlankMaster'
  Exec $c 'BEGIN TRANSACTION; UPDATE dbo.Locations SET LocationName=N'''' WHERE LocationId=10;'
  try{Check $c $sql 'PASS' 'HOLD'}finally{Exec $c 'ROLLBACK TRANSACTION;'}
  $case='MissingCreator'
  Exec $c 'BEGIN TRANSACTION; UPDATE dbo.Locations SET CreatedByUserId=999 WHERE LocationId=10;'
  try{Check $c $sql 'HOLD' 'PASS'}finally{Exec $c 'ROLLBACK TRANSACTION;'}
  $case='ForeignTeam'
  Exec $c 'BEGIN TRANSACTION; UPDATE dbo.Locations SET TeamId=2 WHERE LocationId=10;'
  try{Check $c $sql 'HOLD' 'PASS'}finally{Exec $c 'ROLLBACK TRANSACTION;'}
  $case='NullableTeamAllowed'
  Exec $c 'BEGIN TRANSACTION; UPDATE dbo.Locations SET TeamId=NULL WHERE LocationId=10;'
  try{Check $c $sql 'PASS' 'PASS'}finally{Exec $c 'ROLLBACK TRANSACTION;'}
  $case='InactiveExcluded'
  Exec $c 'BEGIN TRANSACTION; UPDATE dbo.Locations SET LocationName=N'''',CreatedByUserId=0,IsActive=0 WHERE LocationId=10;'
  try{Check $c $sql 'PASS' 'PASS'}finally{Exec $c 'ROLLBACK TRANSACTION;'}
  $case='Unexpected011Marker'
  Exec $c "BEGIN TRANSACTION; INSERT dbo.SchemaVersions VALUES(N'1.8.0-011',N'fixture',DATEADD(second,1,SYSUTCDATETIME()),N'isolated');"
  try{ExpectRefusal $c $sql 52142}finally{Exec $c 'ROLLBACK TRANSACTION;'}
  $case='UnoptedDisposableRefused'
  Exec $c "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=NULL;"
  try{ExpectRefusal $c $sql 52140}finally{Exec $c "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;"}
  Write-Output 'UAT010014DisposableSql=PASS; Cases=8; Target=LOCAL; LiveG1=HOLD;'
}catch{
  Write-Output ('UAT010014Case='+$case+'; Status=FAIL; Details=SUPPRESSED;')
  exit 1
}finally{
  if($null -ne $c){$c.Dispose()}
  if($null -ne $admin){
    if($dbName -match '^OptionCIsolated_[0-9a-f]{32}$'){
      try{Exec $admin "ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName];"}catch{}
    }
    $admin.Dispose()
  }
}
