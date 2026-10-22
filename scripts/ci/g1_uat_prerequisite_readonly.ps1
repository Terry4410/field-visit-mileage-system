# Fixed-source SELECT-only G1 UAT 010 data preflight. Never prints SQL data, metadata or identity.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$stage='GUARD'
$conn=$null
function Stop-G1Preflight([string]$classification) {
 if ($classification -notin @('PrerequisiteRejected','TargetRejected','PermissionRejected','BaselineRejected','ProviderFailure','ResultRejected')) {$classification='ProviderFailure'}
 Write-Output "G1Preflight=STOP; Stage=$stage; Class=$classification; Details=SUPPRESSED;"
 exit 1
}
function Query-Table([object]$connection,[string]$sql) {
 $cmd=$connection.CreateCommand()
 $cmd.CommandText=$sql
 $cmd.CommandTimeout=30
 $dt=[System.Data.DataTable]::new()
 try {
  $rd=$cmd.ExecuteReader()
  try {$dt.Load($rd)} finally {$rd.Dispose()}
  return ,$dt
 } finally {$cmd.Dispose()}
}
function Read-Statuses([System.Data.DataTable]$table,[string[]]$expected) {
 if ($table.Columns.Count -ne 2 -or $table.Columns[0].ColumnName -cne 'code' -or
     $table.Columns[1].ColumnName -cne 'status' -or $table.Rows.Count -ne $expected.Count) {throw 'invalid-shape'}
 $found=@{}
 foreach($row in $table.Rows) {
  $key=[string]$row.code
  $value=[string]$row.status
  if ($key -cnotmatch '^[A-Z0-9_]+$' -or $value -notin @('PASS','FAIL','UNKNOWN') -or $found.ContainsKey($key)) {throw 'invalid-value'}
  $found[$key]=$value
 }
 if (@($found.Keys | Where-Object {$_ -notin $expected}).Count -gt 0) {throw 'unexpected-code'}
 foreach($key in $expected) {if (-not $found.ContainsKey($key)) {throw 'missing-code'}}
 return $found
}
try {
 if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
     $env:GITHUB_REF -cne 'refs/heads/post-uat/v1.8.0' -or
     $env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or
     $env:G1_EXPECTED_SERVER -cne 'sql-fieldvisit-jpe-uat.database.windows.net' -or
     $env:G1_EXPECTED_DATABASE -cne 'db-fieldvisit-uat' -or
     [string]::IsNullOrWhiteSpace($env:G1_SQL_ACCESS_TOKEN)) {Stop-G1Preflight 'PrerequisiteRejected'}
 $root=Join-Path $env:GITHUB_WORKSPACE 'candidate'
 $target=Join-Path $root 'docs/release/g1-minimal-preflight/01-target.sql'
 $catalog=Join-Path $root 'docs/release/g1-execution/promotion/CatalogContract.sql'
 $reference=Join-Path $root 'docs/release/g1-execution/promotion/reference.json'
 $preflight=Join-Path $env:GITHUB_WORKSPACE 'scripts/ci/g1_uat_prerequisite_readonly.sql'
 foreach($p in @($target,$catalog,$reference,$preflight)) {
  if (-not (Test-Path -LiteralPath $p -PathType Leaf)) {Stop-G1Preflight 'PrerequisiteRejected'}
 }
 $ref=Get-Content -LiteralPath $reference -Raw | ConvertFrom-Json
 if ($ref.contract -cne 'g1-scoped-promotion-reference-v1' -or
     $ref.initial -cnotmatch '^[0-9A-F]{64}$' -or
     $ref.fixture -cne 'SYNTHETIC_SCOPED_MIGRATION_CONTRACT_NOT_UAT_OR_150_BASELINE') {Stop-G1Preflight 'PrerequisiteRejected'}
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
 $b['Data Source']="tcp:$($env:G1_EXPECTED_SERVER),1433"
 $b['Initial Catalog']=$env:G1_EXPECTED_DATABASE
 $b['Encrypt']=$true
 $b['TrustServerCertificate']=$false
 $b['ApplicationIntent']='ReadOnly'
 $b['Connect Timeout']=120
 $b['Pooling']=$false
 $b['ConnectRetryCount']=0
 $conn=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
 $conn.AccessToken=$env:G1_SQL_ACCESS_TOKEN
 $stage='OPEN'
 $conn.Open()
 $stage='TARGET'
 $checks=Read-Statuses (Query-Table $conn (Get-Content -LiteralPath $target -Raw)) @('TARGET','VISIBILITY','VERSION_SHAPE')
 foreach($code in @('TARGET','VISIBILITY','VERSION_SHAPE')) {if ($checks[$code] -ne 'PASS') {Stop-G1Preflight 'TargetRejected'}}
 Write-Output 'PreflightTarget=PASS;'
 $stage='DATA_PRIVILEGE'
 $p=Query-Table $conn @"
SELECT code,status FROM (VALUES
(N'LOCATIONS',CASE WHEN HAS_PERMS_BY_NAME(N'dbo.Locations',N'OBJECT',N'SELECT')=1 THEN N'PASS' ELSE N'UNKNOWN' END),
(N'REFERENCES',CASE WHEN HAS_PERMS_BY_NAME(N'dbo.Organizations',N'OBJECT',N'SELECT')=1
 AND HAS_PERMS_BY_NAME(N'dbo.Teams',N'OBJECT',N'SELECT')=1
 AND HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'SELECT')=1 THEN N'PASS' ELSE N'UNKNOWN' END),
(N'IMPORT',CASE WHEN HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'SELECT')=1
 AND HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'SELECT')=1 THEN N'PASS' ELSE N'UNKNOWN' END),
(N'VERSIONS',CASE WHEN HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'SELECT')=1 THEN N'PASS' ELSE N'UNKNOWN' END)
) q(code,status) ORDER BY code;
"@
 $permission=Read-Statuses $p @('LOCATIONS','REFERENCES','IMPORT','VERSIONS')
 foreach($code in @('LOCATIONS','REFERENCES','IMPORT','VERSIONS')) {if ($permission[$code] -ne 'PASS') {Stop-G1Preflight 'PermissionRejected'}}
 Write-Output 'PreflightReadPermissions=PASS; PrincipalRole=DIAGNOSTIC_ONLY;'
 $stage='BASE_CATALOG'
 $hash=Query-Table $conn (Get-Content -LiteralPath $catalog -Raw)
 if ($hash.Rows.Count -ne 1 -or $hash.Columns.Count -ne 1) {Stop-G1Preflight 'ResultRejected'}
 if ([string]$hash.Rows[0][0] -cnotmatch '^[0-9a-fA-F]{64}$' -or
     ([string]$hash.Rows[0][0]).ToUpperInvariant() -cne $ref.initial) {
  Write-Output 'PreflightCheck=SCOPED_INITIAL_CATALOG; Status=FAIL;'
  Stop-G1Preflight 'BaselineRejected'
 }
 Write-Output 'PreflightCheck=SCOPED_INITIAL_CATALOG; Status=PASS;'
 $stage='PRECHECK'
 $expected=@('BASE_OBJECTS','LATEST_PREDECESSOR_010','UNASSIGNED_MARKERS','NEW_MANAGED_OBJECTS_ABSENT',
             '014_PUBLISHED_SOURCE','014_AUTHORITY_REFERENCES','020_IMPORT_EXISTING_ROWS','020_IMPORT_STATUS_WIDTH')
 $statuses=Read-Statuses (Query-Table $conn (Get-Content -LiteralPath $preflight -Raw)) $expected
 $bad=$false
 foreach($code in $expected) {
  $value=[string]$statuses[$code]
  Write-Output "PreflightCheck=$code; Status=$value;"
  if($value -ne 'PASS') {$bad=$true}
 }
 if($bad){Stop-G1Preflight 'BaselineRejected'}
 Write-Output "G1Preflight=PASS; SourceSha=$($env:GITHUB_SHA); CandidateSha=$($env:G1_CANDIDATE_SHA); PrincipalMigration=UNKNOWN; PrincipalAPI=UNKNOWN;"
} catch {
 Stop-G1Preflight 'ProviderFailure'
} finally {
 if($null -ne $conn) {
  try {$conn.Close()} catch {}
  try {$conn.Dispose()} catch {}
 }
 $env:G1_SQL_ACCESS_TOKEN=$null
}
