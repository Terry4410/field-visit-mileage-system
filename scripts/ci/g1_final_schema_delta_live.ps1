Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stage = 'PRE_SQL'
$connection = $null
$command = $null

function Stop-G1([string]$classification) {
    $allowed = @('PrerequisiteRejected','TargetRejected','HashRejected','ProviderFailure','ResultContractRejected','DeadlineExceeded')
    if ($classification -notin $allowed) { $classification = 'ProviderFailure' }
    Write-Output "G1Delta=STOP; Stage=$stage; Classification=$classification; Details=SUPPRESSED;"
    exit 1
}

function Invoke-Table([object]$conn, [string]$sql, [hashtable]$parameters = @{}) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 30
    foreach ($name in $parameters.Keys) {
        $p = $cmd.Parameters.Add($name, [System.Data.SqlDbType]::NVarChar, -1)
        $p.Value = [string]$parameters[$name]
    }
    $table = [System.Data.DataTable]::new()
    try {
        $reader = $cmd.ExecuteReader()
        try { $table.Load($reader) } finally { $reader.Dispose() }
        return ,$table
    } finally {
        $cmd.Dispose()
    }
}

function Read-CodeStatus([System.Data.DataTable]$table) {
    if ($table.Columns.Count -ne 2 -or $table.Columns[0].ColumnName -cne 'code' -or
        $table.Columns[1].ColumnName -cne 'status' -or $table.Rows.Count -lt 1) {
        throw 'result-contract'
    }
    $result = @{}
    foreach ($row in $table.Rows) {
        $code = [string]$row.code
        $status = [string]$row.status
        if ($code -cnotmatch '^[A-Z0-9_]+$' -or $status -notin @('PASS','FAIL','UNKNOWN')) { throw 'result-contract' }
        if ($result.ContainsKey($code)) { throw 'result-contract' }
        $result[$code] = $status
    }
    return $result
}

try {
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
        $env:GITHUB_REF -cne 'refs/heads/post-uat/v1.8.0' -or $env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or
        $env:G1_EXPECTED_SERVER -cne 'sql-fieldvisit-jpe-uat.database.windows.net' -or
        $env:G1_EXPECTED_DATABASE -cne 'db-fieldvisit-uat' -or
        [string]::IsNullOrWhiteSpace($env:G1_SQL_ACCESS_TOKEN)) { Stop-G1 'PrerequisiteRejected' }

    $candidate = Join-Path $env:GITHUB_WORKSPACE 'candidate/docs/release/g1-minimal-preflight'
    $paths = @{
        target = Join-Path $candidate '01-target.sql'
        catalog = Join-Path $candidate '03-catalog.sql'
        permissions = Join-Path $candidate '04-permissions.sql'
        requirements = Join-Path $candidate 'requirements.json'
        reference = Join-Path $candidate 'necessary-reference.json'
    }
    foreach ($path in $paths.Values) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Stop-G1 'PrerequisiteRejected' } }

    Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
    $builder = [Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = "tcp:$($env:G1_EXPECTED_SERVER),1433"
    $builder['Initial Catalog'] = $env:G1_EXPECTED_DATABASE
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $false
    $builder['ApplicationIntent'] = 'ReadOnly'
    $builder['Connect Timeout'] = 120
    $builder['Pooling'] = $false
    $builder['ConnectRetryCount'] = 0
    $builder['MultiSubnetFailover'] = $false
    $connection = [Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $connection.AccessToken = $env:G1_SQL_ACCESS_TOKEN
    $stage = 'OPEN'
    $connection.Open()
    if ($connection.State -ne [System.Data.ConnectionState]::Open) { Stop-G1 'ProviderFailure' }

    $stage = 'TARGET'
    $target = Invoke-Table $connection (Get-Content -LiteralPath $paths.target -Raw)
    $targetStatus = Read-CodeStatus $target
    $targetRejected = $false
    foreach ($required in @('TARGET','VISIBILITY','VERSION_SHAPE')) {
        $observed = if ($targetStatus.ContainsKey($required)) { [string]$targetStatus[$required] } else { 'UNKNOWN' }
        Write-Output "TargetCheck=$required; Status=$observed;"
        if ($observed -ne 'PASS') { $targetRejected = $true }
    }
    if ($targetRejected) { Stop-G1 'TargetRejected' }
    Write-Output 'Stage=Target; Status=PASS;'

    $stage = 'LEDGER'
    $ledgerSql = @"
WITH expected(VersionNumber,MigrationId) AS (
 SELECT N'1.8.0-011','011' UNION ALL SELECT N'1.8.0-012','012' UNION ALL
 SELECT N'1.8.0-013','013' UNION ALL SELECT N'1.8.0-014','014' UNION ALL
 SELECT N'1.8.0-015','015' UNION ALL SELECT N'1.8.0-016','016' UNION ALL
 SELECT N'1.8.0-017','017' UNION ALL SELECT N'1.8.0-018','018' UNION ALL
 SELECT N'1.8.0-019','019' UNION ALL SELECT N'1.8.0-020','020'
)
SELECT e.MigrationId, CONVERT(int,COUNT_BIG(s.VersionNumber)) AS MarkerCount
FROM expected e LEFT JOIN dbo.SchemaVersions s ON s.VersionNumber=e.VersionNumber
GROUP BY e.MigrationId ORDER BY e.MigrationId;
"@
    $ledger = Invoke-Table $connection $ledgerSql
    if ($ledger.Columns.Count -ne 2 -or $ledger.Columns[0].ColumnName -cne 'MigrationId' -or
        $ledger.Columns[1].ColumnName -cne 'MarkerCount' -or $ledger.Rows.Count -ne 10) { Stop-G1 'ResultContractRejected' }
    $markers = @{}
    foreach ($row in $ledger.Rows) {
        $id = [string]$row.MigrationId
        $count = [int]$row.MarkerCount
        if ($id -cnotmatch '^0(1[1-9]|20)$' -or $markers.ContainsKey($id) -or $count -lt 0) { Stop-G1 'ResultContractRejected' }
        $markers[$id] = if ($count -eq 0) {'ABSENT'} elseif ($count -eq 1) {'PRESENT_ONCE'} else {'DUPLICATE'}
    }
    if ($markers['013'] -ne 'ABSENT') {
        Write-Output "Migration=013; Marker=$($markers['013']); Contract=RESERVED; Decision=UNKNOWN;"
    } else {
        Write-Output 'Migration=013; Marker=ABSENT; Contract=RESERVED; Decision=NOT_REQUIRED;'
    }

    $stage = 'CATALOG'
    $reference = Get-Content -LiteralPath $paths.reference -Raw | ConvertFrom-Json -Depth 100
    $catalogSql = Get-Content -LiteralPath $paths.catalog -Raw
    $decisions = @{}
    $catalogMigration = 'NONE'
    foreach ($id in @('011','012','014','016','017','018','019','020')) {
        $catalogMigration = $id
        $subset = [ordered]@{
            columns = @($reference.columns | Where-Object migration -eq $id)
            checks = @($reference.checks | Where-Object migration -eq $id)
            indexes = @($reference.indexes | Where-Object migration -eq $id)
            foreignKeys = @($reference.foreignKeys | Where-Object migration -eq $id)
        }
        $requirements = @($subset.columns | ForEach-Object { [ordered]@{table=$_.table;column=$_.column} })
        $table = Invoke-Table $connection $catalogSql @{
            '@requirements' = (ConvertTo-Json -InputObject $requirements -Depth 10 -Compress)
            '@reference' = ($subset | ConvertTo-Json -Depth 100 -Compress)
        }
        $status = Read-CodeStatus $table
        $requiredCodes = @('REQUIRED_COLUMNS','TRUSTED_RELATIONSHIPS','ENABLED_INDEXES','SCOPED_COLUMN_FACETS','SCOPED_FK_MAPPINGS','SCOPED_INDEX_KEYS','SCOPED_CHECK_DEFINITIONS')
        $contract = 'PASS'
        foreach ($code in $requiredCodes) {
            if (-not $status.ContainsKey($code)) { Stop-G1 'ResultContractRejected' }
            if ($status[$code] -eq 'FAIL') { $contract = 'FAIL' }
            elseif ($status[$code] -eq 'UNKNOWN' -and $contract -ne 'FAIL') { $contract = 'UNKNOWN' }
        }
        $decisions[$id] = $contract
    }

    $stage = 'SPECIAL_CONTRACTS'
    $specialSql = @"
SELECT code,status FROM (VALUES
 ('M015',CASE WHEN OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NOT NULL
   AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents') AND name=N'CK_B3_ChangeRequestEvents_EventType' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%GeocodeEvidenceRecorded%' AND definition LIKE N'%DuplicateReviewEvidenceRecorded%')
   AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents') AND name=N'CK_B3_ChangeRequestEvents_StageEvidenceEventType' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%GeocodeEvidenceRecorded%' AND definition LIKE N'%DuplicateReviewEvidenceRecorded%')
   THEN 'PASS' ELSE 'FAIL' END),
 ('M020',CASE WHEN COL_LENGTH(N'dbo.ImportBatches',N'Status')=60 AND COL_LENGTH(N'dbo.ImportBatchItems',N'Status')=60
   AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatches') AND name=N'CK_ImportBatches_Status' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%SubmittedForReview%')
   AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatchItems') AND name=N'CK_ImportBatchItems_Status' AND is_disabled=0 AND is_not_trusted=0 AND definition LIKE N'%SubmittedForReview%')
   THEN 'PASS' ELSE 'FAIL' END)
) c(code,status) ORDER BY code;
"@
    $special = Read-CodeStatus (Invoke-Table $connection $specialSql)
    if (-not $special.ContainsKey('M015') -or -not $special.ContainsKey('M020')) { Stop-G1 'ResultContractRejected' }
    $decisions['015'] = $special['M015']
    if ($special['M020'] -eq 'FAIL') { $decisions['020'] = 'FAIL' }

    foreach ($id in @('011','012','014','015','016','017','018','019','020')) {
        $marker = $markers[$id]
        $contract = $decisions[$id]
        $decision = if ($marker -eq 'PRESENT_ONCE' -and $contract -eq 'PASS') {'NOT_REQUIRED'}
                    elseif ($marker -eq 'ABSENT' -and $contract -eq 'FAIL') {'REQUIRED'}
                    else {'UNKNOWN'}
        Write-Output "Migration=$id; Marker=$marker; Contract=$contract; Decision=$decision;"
    }

    $stage = 'PERMISSIONS'
    $permissionStatus = Read-CodeStatus (Invoke-Table $connection (Get-Content -LiteralPath $paths.permissions -Raw))
    foreach ($code in @('NO_DATABASE_CONTROL','NO_DATABASE_ALTER','NO_CREATE_TABLE','NO_SCHEMA_ALTER','NO_REGISTRY_INSERT','NO_REGISTRY_UPDATE','NO_REGISTRY_DELETE')) {
        if (-not $permissionStatus.ContainsKey($code) -or $permissionStatus[$code] -ne 'PASS') { Stop-G1 'ResultContractRejected' }
    }
    Write-Output 'DiagnosticPermissions=PASS; ApiPrincipal=UNKNOWN;'
    Write-Output "G1Delta=PASS; SourceSha=$($env:GITHUB_SHA); CandidateSha=$($env:G1_CANDIDATE_SHA);"
} catch {
    if ($stage -eq 'CATALOG') {
        $kind = 'RUNTIME'
        if ($_.Exception -is [Microsoft.Data.SqlClient.SqlException]) {
            $kind = 'SQL_OTHER'
            if ($_.Exception.Number -in @(102,156,207,208,4104)) { $kind = 'SQL_SYNTAX_BINDING' }
            elseif ($_.Exception.Number -in @(13607,13609)) { $kind = 'SQL_JSON' }
            elseif ($_.Exception.Number -in @(229,297)) { $kind = 'SQL_PERMISSION' }
            elseif ($_.Exception.Number -in @(1205,1222,-2)) { $kind = 'SQL_TRANSIENT_OR_TIMEOUT' }
        }
        $id = if ($catalogMigration -in @('011','012','014','016','017','018','019','020')) { $catalogMigration } else { 'NONE' }
        Write-Output "CatalogFailure=STOP; Migration=$id; Class=$kind; Details=SUPPRESSED;"
    }
    Stop-G1 'ProviderFailure'
} finally {
    if ($null -ne $connection) {
        try { if ($connection.State -ne [System.Data.ConnectionState]::Closed) { $connection.Close() } } catch {}
        try { $connection.Dispose() } catch {}
    }
    $env:G1_SQL_ACCESS_TOKEN = $null
}
