# Called only with the SAME already-open fixed-UAT diagnostic SqlConnection.
# No credentials or raw metadata are printed; no new connection or SQL write.
Set-StrictMode -Version Latest
function Invoke-G1Pre010PrivateCollect {
    param([object]$Connection,[string]$CandidateRoot,[string]$SourceSha)
    $cmd=$null;$private=$null;$reader=$null
    try {
        if($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REF -cne 'refs/heads/post-uat/v1.8.0' -or
           $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
           $env:GITHUB_SHA -cne $SourceSha -or $SourceSha -cnotmatch '^[0-9a-f]{40}$' -or
           [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or
           $Connection.GetType().FullName -cne 'Microsoft.Data.SqlClient.SqlConnection' -or
           $Connection.State -ne [Data.ConnectionState]::Open -or
           -not (Test-Path -LiteralPath $CandidateRoot -PathType Container)) {return 'STOP'}
        $commandText=@'
SET NOCOUNT ON;
IF DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat'
 OR CONVERT(sysname,SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-uat'
 THROW 51990,N'G1_FIXED_TARGET_STOP',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')
 THROW 51992,N'G1_PREDECESSOR_STOP',1;
-- Historical application of 010 is not the same as current UAT010.
IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC,VersionNumber DESC) <> N'1.8.0-010'
 THROW 51992,N'G1_PREDECESSOR_STOP',1;
IF COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'VIEW DEFINITION'),0)<>1
 THROW 51993,N'G1_METADATA_VISIBILITY_STOP',1;
SELECT
(SELECT s.name AS SchemaName,t.name AS TableName,tr.name AS TriggerName,
 CONVERT(bit,tr.is_disabled) AS IsDisabled,
 CONVERT(bit,tr.is_instead_of_trigger) AS IsInsteadOfTrigger,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',
     CONVERT(varbinary(max),OBJECT_DEFINITION(tr.object_id))),2) AS DefinitionSha256,
 (SELECT STRING_AGG(CONVERT(nvarchar(30),ev.type_desc),N',')
    WITHIN GROUP(ORDER BY ev.type_desc)
    FROM sys.trigger_events ev WHERE ev.object_id=tr.object_id) AS Events
 FROM sys.triggers tr JOIN sys.tables t ON t.object_id=tr.parent_id
 JOIN sys.schemas s ON s.schema_id=t.schema_id
 WHERE tr.is_ms_shipped=0 FOR JSON PATH,INCLUDE_NULL_VALUES) AS TriggerJson,
(SELECT N'dbo' AS SchemaName,t.name AS TableName,c.name AS ColumnName,
    ty.name AS SqlType,CONVERT(int,c.max_length) AS MaxLengthBytes,
    CONVERT(int,c.scale) AS Scale,CONVERT(bit,c.is_nullable) AS IsNullable,
    CONVERT(bit,c.is_identity) AS IsIdentity
 FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
 JOIN sys.schemas s ON s.schema_id=t.schema_id
 JOIN sys.types ty ON ty.user_type_id=c.user_type_id
 WHERE s.name=N'dbo' AND t.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
                                 N'GeocodingAttempts',N'RouteCalculationAttempts')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS ColumnJson,
(SELECT ss.name AS SchemaName,pt.name AS TableName,fk.name AS ForeignKeyName,
 pc.name AS ParentColumn,rt.name AS ReferencedTable,rc.name AS ReferencedColumn,
 CONVERT(bit,fk.is_disabled) AS IsDisabled,CONVERT(bit,fk.is_not_trusted) AS IsNotTrusted
 FROM sys.foreign_keys fk
 JOIN sys.tables pt ON pt.object_id=fk.parent_object_id
 JOIN sys.schemas ss ON ss.schema_id=pt.schema_id
 JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id
 JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
 JOIN sys.tables rt ON rt.object_id=fk.referenced_object_id
 JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
 WHERE ss.name=N'dbo' AND pt.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
 N'GeocodingAttempts',N'RouteCalculationAttempts')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS ForeignKeyJson,
(SELECT ss.name AS SchemaName,pt.name AS TableName,ck.name AS CheckConstraintName,
 CONVERT(bit,CASE WHEN ck.definition IS NULL THEN 0 ELSE 1 END) AS DefinitionVisible,
 CONVERT(bit,ck.is_disabled) AS IsDisabled,CONVERT(bit,ck.is_not_trusted) AS IsNotTrusted
 FROM sys.check_constraints ck
 JOIN sys.tables pt ON pt.object_id=ck.parent_object_id
 JOIN sys.schemas ss ON ss.schema_id=pt.schema_id
 WHERE ss.name=N'dbo' AND pt.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
 N'GeocodingAttempts',N'RouteCalculationAttempts')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS CheckJson,
CASE WHEN EXISTS(
 SELECT 1 FROM sys.check_constraints ch
 WHERE ch.parent_object_id=OBJECT_ID(N'dbo.RouteCalculationAttempts')
 AND ch.name=N'CK_RouteCalculationAttempts_Reason'
 AND ch.is_disabled=0 AND ch.is_not_trusted=0
 AND ch.definition LIKE N'%BackgroundMileageJob%')
 THEN N'PASS' ELSE N'UNKNOWN' END AS ReasonCheck;
'@
        $cmd=$Connection.CreateCommand()
        $cmd.CommandText=$commandText;$cmd.CommandTimeout=30
        $reader=$cmd.ExecuteReader()
        if(-not $reader.Read() -or $reader.FieldCount -ne 5 -or
           $reader.GetName(0) -cne 'TriggerJson' -or
           $reader.GetName(1) -cne 'ColumnJson' -or
           $reader.GetName(2) -cne 'ForeignKeyJson' -or
           $reader.GetName(3) -cne 'CheckJson' -or
           $reader.GetName(4) -cne 'ReasonCheck' -or
           $reader.IsDBNull(0) -or $reader.IsDBNull(1) -or $reader.IsDBNull(2) -or
           $reader.IsDBNull(3) -or $reader.IsDBNull(4)){return 'STOP'}
        $rawJson=$reader.GetString(0)
        $columnJson=$reader.GetString(1)
        $fkJson=$reader.GetString(2)
        $ckJson=$reader.GetString(3)
        $reason=$reader.GetString(4)
        if($rawJson.Length -gt 220000 -or $columnJson.Length -gt 100000 -or
           $fkJson.Length -gt 100000 -or $ckJson.Length -gt 100000 -or
           $reason -cnotin @('PASS','UNKNOWN')){return 'STOP'}
        if($reader.Read() -or $reader.NextResult()){return 'STOP'}
        $parsed=$rawJson|ConvertFrom-Json -Depth 20
        $parsedColumns=$columnJson|ConvertFrom-Json -Depth 20
        $parsedFks=$fkJson|ConvertFrom-Json -Depth 20
        $parsedChecks=$ckJson|ConvertFrom-Json -Depth 20
        $actual=[ordered]@{
          ContractVersion='g1-pre010-private-observed-v1'
          SourceSha=$SourceSha
          Provenance='READONLY_UAT_CAPTURE'
          Target=[ordered]@{ServerName='sql-fieldvisit-jpe-uat';DatabaseName='db-fieldvisit-uat'}
          SchemaVersion='1.8.0-010'
          ReasonCheck=$reason
          Triggers=@($parsed)
          Columns=@($parsedColumns)
          ForeignKeys=@($parsedFks)
          Checks=@($parsedChecks)
        }
        $private=Join-Path $env:RUNNER_TEMP ('g1-uat010-'+[guid]::NewGuid().ToString('N')+'.json')
        [IO.File]::WriteAllText($private,($actual|ConvertTo-Json -Depth 30 -Compress),[Text.UTF8Encoding]::new($false))
        $result=@(& python3 (Join-Path $env:GITHUB_WORKSPACE 'scripts/ci/g1_pre010_private_compare.py') --candidate-root $CandidateRoot --private-actual $private --protected-sha $SourceSha --mode live 2>$null)
        if($LASTEXITCODE -ne 0 -or $result.Count -ne 1 -or
           $result[0] -cne 'G1Pre010Private=PASS; Mode=LIVE; StructuralScope=21_TRIGGERS; SourceDefinitionHashes=21; NewTableColumnFacets=47; NewTableFKs=13; NewTableChecks=13; CheckDefinitionParity=UNKNOWN; PhysicalStoredObjectProof=007_ONLY; Base15=UNKNOWN; History=UNKNOWN; ReleaseGate=HOLD;') {return 'STOP'}
        return 'PASS'
    } catch {
        return 'STOP'
    } finally {
        if($null -ne $reader){try{$reader.Dispose()}catch{}}
        if($null -ne $cmd){try{$cmd.Dispose()}catch{}}
        if($null -ne $private){Remove-Item -LiteralPath $private -Force -ErrorAction SilentlyContinue}
    }
}
