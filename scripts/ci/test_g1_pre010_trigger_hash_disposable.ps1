# Disposable SQL Server only. Never runs against UAT/Azure. Prints allowlisted statuses.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$stage='BOUNDARY'
$master=$null
$db=$null
$privateDdl=$null
$dbName=$null
$privateEvidence=$null
function SqlCmd([object]$connection,[string]$sql,[object]$sourceDdl=$null) {
    $cmd=$connection.CreateCommand()
    $cmd.CommandText=$sql
    $cmd.CommandTimeout=30
    if($null -ne $sourceDdl) {
        $p=$cmd.Parameters.Add('@SourceDdl',[System.Data.SqlDbType]::NVarChar,-1)
        $p.Value=[string]$sourceDdl
    }
    try { return $cmd.ExecuteScalar() } finally { $cmd.Dispose() }
}
try {
    if($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_EVENT_NAME -cne 'pull_request' -or
       $env:GITHUB_HEAD_REF -cne 'work/g1-executable-gap-closure-20261023' -or
       [string]::IsNullOrWhiteSpace($env:SCHEMA_TEST_SQL_PASSWORD) -or
       $env:SCHEMA_TEST_SQL_SERVER -cne 'localhost,1433' -or
       $env:SCHEMA_TEST_SQL_DATABASE -cne 'master') { throw 'boundary' }
    $candidate=Join-Path $env:GITHUB_WORKSPACE 'candidate'
    if(-not (Test-Path -LiteralPath $candidate -PathType Container)){throw 'candidate'}
    $privateDdl=Join-Path $env:RUNNER_TEMP ('g1-trigger-'+[guid]::NewGuid().ToString('N')+'.sql')
    $stage='SOURCE_PIN'
    & python3 scripts/ci/g1_pre010_trigger_ddl.py --candidate-root $candidate --selected-trigger TR_Locations_SelectedGeocodingAttempt --private-output $privateDdl
    if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $privateDdl -PathType Leaf)){throw 'source'}
    $ddl=[IO.File]::ReadAllText($privateDdl,[Text.UTF8Encoding]::new($false))
    $stage='DISPOSABLE_SQL'
    Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
    $builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source']='localhost,1433'
    $builder['Initial Catalog']='master'
    $builder['User ID']='sa'
    $builder['Password']=$env:SCHEMA_TEST_SQL_PASSWORD
    $builder['Encrypt']=$true
    $builder['TrustServerCertificate']=$true
    $builder['Connect Timeout']=10
    $builder['Pooling']=$false
    $builder['ConnectRetryCount']=0
    $master=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $master.Open()
    $dbName='G1TriggerProof_'+[guid]::NewGuid().ToString('N').Substring(0,12)
    $null=SqlCmd $master "CREATE DATABASE [$dbName];"
    $builder['Initial Catalog']=$dbName
    $db=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $db.Open()
    $null=SqlCmd $db @'
CREATE TABLE dbo.Locations(LocationId int NOT NULL PRIMARY KEY, SelectedGeocodingAttemptId bigint NULL);
CREATE TABLE dbo.GeocodingAttempts(GeocodingAttemptId bigint NOT NULL PRIMARY KEY, LocationId int NOT NULL, Status nvarchar(20) NOT NULL);
'@
    $null=SqlCmd $db $ddl
    $stage='SOURCE_DEFINITION'
    $verdict=SqlCmd $db @'
SELECT CASE WHEN tr.is_disabled=0 AND tr.is_instead_of_trigger=0
 AND (SELECT COUNT(*) FROM sys.trigger_events e WHERE e.object_id=tr.object_id)=2
 AND EXISTS(SELECT 1 FROM sys.trigger_events e WHERE e.object_id=tr.object_id AND e.type_desc=N'INSERT')
 AND EXISTS(SELECT 1 FROM sys.trigger_events e WHERE e.object_id=tr.object_id AND e.type_desc=N'UPDATE')
 AND HASHBYTES('SHA2_256',CONVERT(varbinary(max),OBJECT_DEFINITION(tr.object_id)))=
     HASHBYTES('SHA2_256',CONVERT(varbinary(max),@SourceDdl))
 AND HASHBYTES('SHA2_256',CONVERT(varbinary(max),OBJECT_DEFINITION(tr.object_id)))<>
     HASHBYTES('SHA2_256',CONVERT(varbinary(max),@SourceDdl+N' '))
 THEN N'PASS' ELSE N'STOP' END
FROM sys.triggers tr
WHERE tr.name=N'TR_Locations_SelectedGeocodingAttempt'
  AND tr.parent_id=OBJECT_ID(N'dbo.Locations');
'@ $ddl
    if($verdict -cne 'PASS'){throw 'definition'}
    $stage='PRIVATE_METADATA_FIXTURE'
    $actualJson=SqlCmd $db @'
SELECT s.name AS SchemaName,t.name AS TableName,tr.name AS TriggerName,
 CONVERT(bit,tr.is_disabled) AS IsDisabled,
 CONVERT(bit,tr.is_instead_of_trigger) AS IsInsteadOfTrigger,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',
  CONVERT(varbinary(max),OBJECT_DEFINITION(tr.object_id))),2) AS DefinitionSha256,
 (SELECT STRING_AGG(CONVERT(nvarchar(30),e.type_desc),N',')
    WITHIN GROUP(ORDER BY e.type_desc)
    FROM sys.trigger_events e WHERE e.object_id=tr.object_id) AS Events
FROM sys.triggers tr
JOIN sys.tables t ON t.object_id=tr.parent_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE tr.name=N'TR_Locations_SelectedGeocodingAttempt' AND t.name=N'Locations'
FOR JSON PATH,INCLUDE_NULL_VALUES;
'@
    $parsed=@($actualJson | ConvertFrom-Json -Depth 15)
    $fixture=[ordered]@{
      ContractVersion='g1-pre010-private-observed-v1'
      SourceSha=$env:GITHUB_SHA
      Provenance='DISPOSABLE_SQL'
      SchemaVersion='DISPOSABLE'
      Triggers=$parsed
    }
    $privateEvidence=Join-Path $env:RUNNER_TEMP ('g1-private-fixture-'+[guid]::NewGuid().ToString('N')+'.json')
    [IO.File]::WriteAllText($privateEvidence,($fixture|ConvertTo-Json -Depth 15 -Compress),[Text.UTF8Encoding]::new($false))
    $observedResult=@(& python3 scripts/ci/g1_pre010_private_compare.py --candidate-root $candidate --private-actual $privateEvidence --protected-sha $env:GITHUB_SHA --mode disposable 2>$null)
    if($LASTEXITCODE -ne 0 -or $observedResult.Count -ne 1 -or
      $observedResult[0] -cne 'G1Pre010Private=PASS; Mode=DISPOSABLE; StructuralScope=007_ONLY; DefinitionProof=007_ONLY; BaseFacets=UNKNOWN; History=UNKNOWN; ReleaseGate=HOLD;'){throw 'private-positive'}
    $fixture.Triggers[0].DefinitionSha256='0'*64
    [IO.File]::WriteAllText($privateEvidence,($fixture|ConvertTo-Json -Depth 15 -Compress),[Text.UTF8Encoding]::new($false))
    $negativeResult=@(& python3 scripts/ci/g1_pre010_private_compare.py --candidate-root $candidate --private-actual $privateEvidence --protected-sha $env:GITHUB_SHA --mode disposable 2>$null)
    if($LASTEXITCODE -eq 0 -or $negativeResult.Count -ne 1 -or
       $negativeResult[0] -cne 'G1Pre010Private=STOP; Class=007_DEFINITION_DRIFT; Details=SUPPRESSED;'){throw 'private-negative'}
    Write-Output 'G1PrivateMetadataFixture=PASS; Source=PINNED_007; RealSqlToPrivateComparator=PASS; HashTamper=STOP; UAT=NOT_ACCESSED; ReleaseGate=HOLD;'

    Write-Output 'G1TriggerSqlServer=PASS; Source=PINNED_007; DefinitionHash=UTF16LE_SQL; Events=PASS; TamperRejected=PASS; Azure=NOT_ACCESSED; ActualUat010=UNKNOWN; ReleaseGate=HOLD;'
} catch {
    Write-Output "G1TriggerSqlServer=STOP; Stage=$stage; Details=SUPPRESSED;"
    exit 1
} finally {
    if($null -ne $db){try{$db.Dispose()}catch{}}
    if($null -ne $master){
        if($null -ne $dbName){
            try{$null=SqlCmd $master "ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName];"}catch{}
        }
        try{$master.Dispose()}catch{}
    }
    if($null -ne $privateDdl){Remove-Item -LiteralPath $privateDdl -Force -ErrorAction SilentlyContinue}
    if($null -ne $privateEvidence){Remove-Item -LiteralPath $privateEvidence -Force -ErrorAction SilentlyContinue}
    Remove-Variable ddl -ErrorAction SilentlyContinue
}
