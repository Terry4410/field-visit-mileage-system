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
                                 N'GeocodingAttempts',N'RouteCalculationAttempts',N'MileageGovernanceEvents')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS ColumnJson,
(SELECT ss.name AS SchemaName,pt.name AS TableName,fk.name AS ForeignKeyName,
 pc.name AS ParentColumn,rt.name AS ReferencedTable,rc.name AS ReferencedColumn,
 fk.delete_referential_action_desc AS DeleteAction,
 fk.update_referential_action_desc AS UpdateAction,
 CONVERT(bit,fk.is_not_for_replication) AS IsNotForReplication,
 CONVERT(bit,fk.is_disabled) AS IsDisabled,CONVERT(bit,fk.is_not_trusted) AS IsNotTrusted
 FROM sys.foreign_keys fk
 JOIN sys.tables pt ON pt.object_id=fk.parent_object_id
 JOIN sys.schemas ss ON ss.schema_id=pt.schema_id
 JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id
 JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
 JOIN sys.tables rt ON rt.object_id=fk.referenced_object_id
 JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
 WHERE ss.name=N'dbo' AND pt.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
 N'GeocodingAttempts',N'RouteCalculationAttempts',N'MileageGovernanceEvents')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS ForeignKeyJson,
(SELECT ss.name AS SchemaName,pt.name AS TableName,ck.name AS CheckConstraintName,
 CONVERT(bit,CASE WHEN ck.definition IS NULL THEN 0 ELSE 1 END) AS DefinitionVisible,
 CONVERT(bit,ck.is_disabled) AS IsDisabled,CONVERT(bit,ck.is_not_trusted) AS IsNotTrusted
 FROM sys.check_constraints ck
 JOIN sys.tables pt ON pt.object_id=ck.parent_object_id
 JOIN sys.schemas ss ON ss.schema_id=pt.schema_id
 WHERE ss.name=N'dbo' AND pt.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
 N'GeocodingAttempts',N'RouteCalculationAttempts',N'MileageGovernanceEvents')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS CheckJson,
(SELECT sch.name AS SchemaName,t.name AS TableName,i.name AS IndexName,
 (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name + N':' +
     CASE WHEN ic.is_descending_key=1 THEN N'DESC' ELSE N'ASC' END),N',')
     WITHIN GROUP (ORDER BY ic.key_ordinal)
  FROM sys.index_columns ic JOIN sys.columns c
    ON c.object_id=ic.object_id AND c.column_id=ic.column_id
  WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
    AND ic.key_ordinal>0 AND ic.is_included_column=0) AS KeyColumns,
 COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',')
     WITHIN GROUP (ORDER BY c.name)
  FROM sys.index_columns ic JOIN sys.columns c
    ON c.object_id=ic.object_id AND c.column_id=ic.column_id
  WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
    AND ic.is_included_column=1),N'') AS IncludeColumns,
 CONVERT(bit,i.is_unique) AS IsUnique,
 CONVERT(bit,i.is_primary_key) AS IsPrimaryKey,
 CONVERT(bit,i.is_unique_constraint) AS IsUniqueConstraint,
 CONVERT(bit,i.is_disabled) AS IsDisabled,
 CONVERT(bit,i.has_filter) AS HasFilter,
 i.filter_definition AS FilterDefinition
 FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id
 JOIN sys.schemas sch ON sch.schema_id=t.schema_id
 WHERE sch.name=N'dbo' AND i.index_id>0 AND i.is_hypothetical=0
  AND t.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
                N'GeocodingAttempts',N'RouteCalculationAttempts',N'MileageGovernanceEvents')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS IndexJson,
(SELECT sch.name AS SchemaName,t.name AS TableName,c.name AS ColumnName,
 d.name AS DefaultName,d.definition AS Definition
 FROM sys.default_constraints d
 JOIN sys.tables t ON t.object_id=d.parent_object_id
 JOIN sys.schemas sch ON sch.schema_id=t.schema_id
 JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
 WHERE sch.name=N'dbo' AND t.name IN(N'TeamLocationNotes',N'TeamLocationNoteHistory',
                                   N'GeocodingAttempts',N'RouteCalculationAttempts',N'MileageGovernanceEvents')
 FOR JSON PATH,INCLUDE_NULL_VALUES) AS DefaultJson,
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
        if(-not $reader.Read() -or $reader.FieldCount -ne 7 -or
           $reader.GetName(0) -cne 'TriggerJson' -or
           $reader.GetName(1) -cne 'ColumnJson' -or
           $reader.GetName(2) -cne 'ForeignKeyJson' -or
           $reader.GetName(3) -cne 'CheckJson' -or
           $reader.GetName(4) -cne 'IndexJson' -or
           $reader.GetName(5) -cne 'DefaultJson' -or
           $reader.GetName(6) -cne 'ReasonCheck' -or
           $reader.IsDBNull(0) -or $reader.IsDBNull(1) -or $reader.IsDBNull(2) -or
           $reader.IsDBNull(3) -or $reader.IsDBNull(4) -or $reader.IsDBNull(5) -or
           $reader.IsDBNull(6)){return 'STOP'}
        $rawJson=$reader.GetString(0)
        $columnJson=$reader.GetString(1)
        $fkJson=$reader.GetString(2)
        $ckJson=$reader.GetString(3)
        $idxJson=$reader.GetString(4)
        $defaultJson=$reader.GetString(5)
        $reason=$reader.GetString(6)
        if($rawJson.Length -gt 220000 -or $columnJson.Length -gt 100000 -or
           $fkJson.Length -gt 100000 -or $ckJson.Length -gt 100000 -or
           $idxJson.Length -gt 100000 -or $defaultJson.Length -gt 40000 -or
           $reason -cnotin @('PASS','UNKNOWN')){return 'STOP'}
        if($reader.Read() -or $reader.NextResult()){return 'STOP'}
        $parsed=$rawJson|ConvertFrom-Json -Depth 20
        $parsedColumns=$columnJson|ConvertFrom-Json -Depth 20
        $parsedFks=$fkJson|ConvertFrom-Json -Depth 20
        $parsedChecks=$ckJson|ConvertFrom-Json -Depth 20
        $parsedIndexes=$idxJson|ConvertFrom-Json -Depth 20
        $parsedDefaults=$defaultJson|ConvertFrom-Json -Depth 20
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
          Indexes=@($parsedIndexes)
          Defaults=@($parsedDefaults)
        }
        $private=Join-Path $env:RUNNER_TEMP ('g1-uat010-'+[guid]::NewGuid().ToString('N')+'.json')
        [IO.File]::WriteAllText($private,($actual|ConvertTo-Json -Depth 30 -Compress),[Text.UTF8Encoding]::new($false))
        $result=@(& python3 (Join-Path $env:GITHUB_WORKSPACE 'scripts/ci/g1_pre010_private_compare.py') --candidate-root $CandidateRoot --private-actual $private --protected-sha $SourceSha --mode live 2>$null)
        if($LASTEXITCODE -ne 0 -or $result.Count -ne 1 -or
           $result[0] -cne 'G1Pre010Private=PASS; Mode=LIVE; StructuralScope=21_TRIGGERS; SourceDefinitionHashes=21; NewTableColumnFacets=57; NewTableFKs=17; NewTableChecks=14; CheckDefinitionParity=UNKNOWN; PhysicalStoredObjectProof=007_ONLY; Base15=UNKNOWN; History=UNKNOWN; ReleaseGate=HOLD;') {return 'STOP'}
        return 'PASS'
    } catch {
        return 'STOP'
    } finally {
        if($null -ne $reader){try{$reader.Dispose()}catch{}}
        if($null -ne $cmd){try{$cmd.Dispose()}catch{}}
        if($null -ne $private){Remove-Item -LiteralPath $private -Force -ErrorAction SilentlyContinue}
    }
}
