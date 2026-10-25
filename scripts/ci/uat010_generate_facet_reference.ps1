# Nondeploy: derive an exact six-facet *scoped* reference from a disposable
# SQL Server 2022 database using the accepted 1700_005 historical migration.
# Never reads/writes Azure, shared UAT, secrets, employees, or live schema.
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$case='PREFLIGHT'
$admin=$null
$db=$null
$created=$false
$dbName='OptionCIsolated_'+[guid]::NewGuid().ToString('N')
function StopIf([bool]$condition,[string]$code) { if($condition){throw $code} }
function Execute($connection,[string]$sql) {
    $cmd=$connection.CreateCommand()
    try { $cmd.CommandTimeout=90; $cmd.CommandText=$sql; [void]$cmd.ExecuteNonQuery() }
    finally { $cmd.Dispose() }
}
function Scalar($connection,[string]$sql) {
    $cmd=$connection.CreateCommand()
    try { $cmd.CommandTimeout=30; $cmd.CommandText=$sql; return $cmd.ExecuteScalar() }
    finally { $cmd.Dispose() }
}
try {
    Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
    $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:E1_R1_SQL_CONNECTION)
    StopIf ($env:GITHUB_ACTIONS -ne 'true' -or
        $env:GITHUB_REPOSITORY -ne 'Terry4410/field-visit-mileage-system' -or
        $b.DataSource -notin @('localhost,1433','127.0.0.1,1433') -or
        $b.InitialCatalog -ne 'master' -or $b.UserID -ne 'sa' -or
        $b.IntegratedSecurity) 'G1_FACET_LOCAL_ONLY_STOP'
    $root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $historyPath='database/migrations/1700_005_people_bulk_confirm_claim/Up.sql'
    $historyBlob=(& git hash-object -- $historyPath).Trim()
    StopIf ($historyBlob -ne 'ff6347a93ad72e0c6ad5f7883834471f052e1cf5') 'G1_FACET_HISTORY_BLOB_STOP'
    $fixture=Get-Content (Join-Path $PSScriptRoot 'run_full_candidate_migration.ps1') -Raw
    $fixtureMatch=[regex]::Matches($fixture,"(?s)\`$boot=@'\r?\n(.*?)\r?\n'@")
    StopIf ($fixtureMatch.Count -ne 1) 'G1_FACET_FIXTURE_SCOPE_STOP'
    $boot=[string]$fixtureMatch[0].Groups[1].Value
    StopIf ([string]::IsNullOrWhiteSpace($boot)) 'G1_FACET_FIXTURE_EMPTY_STOP'
    $b['Pooling']=$false
    $b['Connect Timeout']=5
    $b['ConnectRetryCount']=0
    $admin=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
    $admin.Open()
    StopIf ([int](Scalar $admin "SELECT CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))") -ne 16 -or
        [int](Scalar $admin "SELECT CONVERT(int,SERVERPROPERTY(N'EngineEdition'))") -ne 3 -or
        [string](Scalar $admin "SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'Edition'))") -notlike 'Developer Edition*') 'G1_FACET_SQL2022_DEVELOPER_STOP'
    $case='CREATE_DISPOSABLE_DATABASE'
    Execute $admin "CREATE DATABASE [$dbName]"
    $created=$true
    $case='CONNECTION_BUILDER_TARGET'
    StopIf ($dbName -notmatch '^OptionCIsolated_[0-9a-f]{32}
    $case='SQL_CONNECTION_CONSTRUCTOR'
    $db=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
    $case='SQL_CONNECTION_OPEN'
    $db.Open()
    $case='SQL_SESSION_CONTEXT'
    Execute $db "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;"
    $case='BOOTSTRAP_FROZEN_FIXTURE'
    Execute $db $boot
    StopIf ([int](Scalar $db "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010'") -ne 1) 'G1_FACET_FIXTURE_010_STOP'
    # Only disposable 1.7.0-004 marker is synthetic; the accepted 1700_005
    # migration itself runs unchanged, including its CHECK/ledger transaction.
    $case='INSERT_DISPOSABLE_LEGACY_PRECONDITION'
    Execute $db "INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'1.7.0-004',N'DISPOSABLE_FACET_PREREQUISITE',DATEADD(day,-1,SYSUTCDATETIME()),N'isolated');"
    $case='ACTUAL_1700_005_MIGRATION'
    Execute $db ([IO.File]::ReadAllText((Join-Path $root $historyPath)))
    StopIf ([int](Scalar $db "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.7.0-005'") -ne 1) 'G1_FACET_HISTORY_MARKER_STOP'
    StopIf ([int](Scalar $db "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatches') AND name=N'CK_ImportBatches_Status' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%Confirming%'") -ne 1) 'G1_FACET_CONFIRMING_CHECK_STOP'

    $case='SCOPED_FACET_REFERENCE'
    $out=Join-Path $root 'artifacts/uat010-corrected020'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $queryPath=Join-Path $out 'facet-offline-temporary.sql'
    $rowsPath=Join-Path $out 'facet-rows-temporary.json'
    $refPath=Join-Path $out 'scoped-historical-check-facets.json'
    $statusPath=Join-Path $out 'scoped-live-status-NOT-EXECUTED.sql'
    StopIf ((Test-Path $queryPath) -or (Test-Path $rowsPath) -or
        (Test-Path $refPath) -or (Test-Path $statusPath)) 'G1_FACET_REFUSE_OVERWRITE'
    & python3 scripts/ci/uat010_catalog_facets.py --mode offline-sql --output $queryPath
    StopIf ($LASTEXITCODE -ne 0) 'G1_FACET_SOURCE_GUARD_STOP'
    $command=$db.CreateCommand()
    $command.CommandTimeout=30
    $command.CommandText=[IO.File]::ReadAllText($queryPath)
    $rows=[Collections.Generic.List[object]]::new()
    $reader=$null
    try {
        $reader=$command.ExecuteReader()
        while($reader.Read()){
            $rows.Add(@{Facet=[string]$reader.GetString(0);Digest=[string]$reader.GetString(1)})
        }
    } finally {
        if($null -ne $reader){$reader.Dispose()}
        $command.Dispose()
    }
    StopIf ($rows.Count -ne 6) 'G1_FACET_ROWS_COUNT_STOP'
    [IO.File]::WriteAllText($rowsPath,(ConvertTo-Json -InputObject $rows.ToArray() -Depth 3))
    & python3 scripts/ci/uat010_catalog_facets.py --mode seal --input $rowsPath --output $refPath
    StopIf ($LASTEXITCODE -ne 0) 'G1_FACET_SEAL_STOP'
    & python3 scripts/ci/uat010_catalog_facets.py --mode live-status-sql --input $refPath --output $statusPath
    StopIf ($LASTEXITCODE -ne 0) 'G1_FACET_STATUS_SOURCE_STOP'
    Remove-Item $rowsPath,$queryPath -Force
    $case='PASS'
    Write-Output 'G1FacetSource1700_005=PASS; ScopedSixFacetReference=SEALED_NONDEPLOY; RealUatCompared=NO; FullSchema=HOLD; G1=HOLD;'
} catch {
    $exception=$_.Exception
    $category=if($exception -is [Microsoft.Data.SqlClient.SqlException]) {'SQL_PROVIDER'}
              elseif($exception -is [System.ArgumentException]) {'ARGUMENT'}
              elseif($exception -is [System.Management.Automation.ParameterBindingException]) {'BINDING'}
              elseif($exception -is [System.Management.Automation.RuntimeException]) {'RUNTIME'}
              else {'OTHER'}
    $providerCode=if($category -eq 'SQL_PROVIDER') {[string]$exception.Number} else {'N_A'}
    Write-Output "G1FacetSource1700_005=HOLD; Stage=$case; Category=$category; ProviderNumber=$providerCode; Details=SUPPRESSED; LiveUatAccess=NONE;"
    throw 'G1_FACET_DISPOSABLE_TEST_STOP'
} finally {
    if($null -ne $db){$db.Dispose()}
    if($created -and $null -ne $admin -and $admin.State -eq [System.Data.ConnectionState]::Open){
        try {
            Execute $admin "ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]"
        } catch {
            Write-Output 'G1FacetCleanup=STOP; Details=SUPPRESSED;'
            throw 'G1_FACET_LOCAL_CLEANUP_STOP'
        }
    }
    if($null -ne $admin){$admin.Dispose()}
}
) 'G1_FACET_DB_NAME_SCOPE_STOP'
    $b['Initial Catalog']=$dbName
    $case='SQL_CONNECTION_CONSTRUCTOR'
    $db=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
    $case='SQL_CONNECTION_OPEN'
    $db.Open()
    $case='SQL_SESSION_CONTEXT'
    Execute $db "EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;"
    $case='BOOTSTRAP_FROZEN_FIXTURE'
    Execute $db $boot
    StopIf ([int](Scalar $db "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010'") -ne 1) 'G1_FACET_FIXTURE_010_STOP'
    # Only disposable 1.7.0-004 marker is synthetic; the accepted 1700_005
    # migration itself runs unchanged, including its CHECK/ledger transaction.
    $case='INSERT_DISPOSABLE_LEGACY_PRECONDITION'
    Execute $db "INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'1.7.0-004',N'DISPOSABLE_FACET_PREREQUISITE',DATEADD(day,-1,SYSUTCDATETIME()),N'isolated');"
    $case='ACTUAL_1700_005_MIGRATION'
    Execute $db ([IO.File]::ReadAllText((Join-Path $root $historyPath)))
    StopIf ([int](Scalar $db "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.7.0-005'") -ne 1) 'G1_FACET_HISTORY_MARKER_STOP'
    StopIf ([int](Scalar $db "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatches') AND name=N'CK_ImportBatches_Status' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%Confirming%'") -ne 1) 'G1_FACET_CONFIRMING_CHECK_STOP'

    $case='SCOPED_FACET_REFERENCE'
    $out=Join-Path $root 'artifacts/uat010-corrected020'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $queryPath=Join-Path $out 'facet-offline-temporary.sql'
    $rowsPath=Join-Path $out 'facet-rows-temporary.json'
    $refPath=Join-Path $out 'scoped-historical-check-facets.json'
    $statusPath=Join-Path $out 'scoped-live-status-NOT-EXECUTED.sql'
    StopIf ((Test-Path $queryPath) -or (Test-Path $rowsPath) -or
        (Test-Path $refPath) -or (Test-Path $statusPath)) 'G1_FACET_REFUSE_OVERWRITE'
    & python3 scripts/ci/uat010_catalog_facets.py --mode offline-sql --output $queryPath
    StopIf ($LASTEXITCODE -ne 0) 'G1_FACET_SOURCE_GUARD_STOP'
    $command=$db.CreateCommand()
    $command.CommandTimeout=30
    $command.CommandText=[IO.File]::ReadAllText($queryPath)
    $rows=[Collections.Generic.List[object]]::new()
    $reader=$null
    try {
        $reader=$command.ExecuteReader()
        while($reader.Read()){
            $rows.Add(@{Facet=[string]$reader.GetString(0);Digest=[string]$reader.GetString(1)})
        }
    } finally {
        if($null -ne $reader){$reader.Dispose()}
        $command.Dispose()
    }
    StopIf ($rows.Count -ne 6) 'G1_FACET_ROWS_COUNT_STOP'
    [IO.File]::WriteAllText($rowsPath,(ConvertTo-Json -InputObject $rows.ToArray() -Depth 3))
    & python3 scripts/ci/uat010_catalog_facets.py --mode seal --input $rowsPath --output $refPath
    StopIf ($LASTEXITCODE -ne 0) 'G1_FACET_SEAL_STOP'
    & python3 scripts/ci/uat010_catalog_facets.py --mode live-status-sql --input $refPath --output $statusPath
    StopIf ($LASTEXITCODE -ne 0) 'G1_FACET_STATUS_SOURCE_STOP'
    Remove-Item $rowsPath,$queryPath -Force
    $case='PASS'
    Write-Output 'G1FacetSource1700_005=PASS; ScopedSixFacetReference=SEALED_NONDEPLOY; RealUatCompared=NO; FullSchema=HOLD; G1=HOLD;'
} catch {
    $exception=$_.Exception
    $category=if($exception -is [Microsoft.Data.SqlClient.SqlException]) {'SQL_PROVIDER'}
              elseif($exception -is [System.ArgumentException]) {'ARGUMENT'}
              elseif($exception -is [System.Management.Automation.ParameterBindingException]) {'BINDING'}
              elseif($exception -is [System.Management.Automation.RuntimeException]) {'RUNTIME'}
              else {'OTHER'}
    $providerCode=if($category -eq 'SQL_PROVIDER') {[string]$exception.Number} else {'N_A'}
    Write-Output "G1FacetSource1700_005=HOLD; Stage=$case; Category=$category; ProviderNumber=$providerCode; Details=SUPPRESSED; LiveUatAccess=NONE;"
    throw 'G1_FACET_DISPOSABLE_TEST_STOP'
} finally {
    if($null -ne $db){$db.Dispose()}
    if($created -and $null -ne $admin -and $admin.State -eq [System.Data.ConnectionState]::Open){
        try {
            Execute $admin "ALTER DATABASE [$dbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$dbName]"
        } catch {
            Write-Output 'G1FacetCleanup=STOP; Details=SUPPRESSED;'
            throw 'G1_FACET_LOCAL_CLEANUP_STOP'
        }
    }
    if($null -ne $admin){$admin.Dispose()}
}
