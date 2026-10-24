# One TLS connection. Metadata only. No raw SQL/identities/provider errors leave memory.
param([switch]$OfflineContract)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function New-G1SqlConnection([string]$AccessToken) {
    $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $b['Data Source']='tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433'
    $b['Initial Catalog']='db-fieldvisit-uat'
    $b['Encrypt']=$true; $b['TrustServerCertificate']=$false
    $b['Pooling']=$false; $b['ConnectRetryCount']=0; $b['Connect Timeout']=120
    $b['MultiSubnetFailover']=$false; $b['ApplicationIntent']='ReadOnly'
    $c=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
    $c.AccessToken=$AccessToken
    return $c
}
if($OfflineContract) {
    # Same constructor used by Live, with no Open/token/network/environment.
    Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
    $c=New-G1SqlConnection 'synthetic-offline-not-a-credential'
    try {
        $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($c.ConnectionString)
        if($c.State -ne [Data.ConnectionState]::Closed -or $b.Pooling -or $b.ConnectRetryCount -ne 0 -or
           $b.ConnectTimeout -ne 120 -or $b.TrustServerCertificate -or $b.Encrypt.ToString() -notin @('True','Mandatory','Strict') -or
           $b.InitialCatalog -cne 'db-fieldvisit-uat' -or $b.DataSource -cne 'tcp:sql-fieldvisit-jpe-uat.database.windows.net,1433') {throw 'OFFLINE_BUILDER'}
        $cmd=$c.CreateCommand()
        try {$cmd.CommandTimeout=30; if($cmd.CommandTimeout -ne 30){throw 'OFFLINE_TIMEOUT'}}
        finally {$cmd.Dispose()}
    } finally {$c.Dispose()}
    Write-Output 'SqlClientContract=PASS; ConnectionState=CLOSED; OpenCalls=0; Azure=NOT_ACCESSED;'
    exit 0
}
$connection=$null; $cmd=$null; $rows=$null; $stage='CONTEXT'; $status=1
try {
    if($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or
       $env:GITHUB_REF -cne 'refs/heads/post-uat/v1.8.0' -or $env:GITHUB_ACTOR -cne 'Terry4410' -or
       $env:GITHUB_TRIGGERING_ACTOR -cne 'Terry4410' -or $env:GITHUB_RUN_ATTEMPT -cne '1' -or
       $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or $env:RUNNER_DEBUG -ceq '1' -or
       $env:APPROVED_SHA -cne $env:GITHUB_SHA -or $env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$') {throw 'BOUNDARY'}
    Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop
    $stage='TOKEN'
    $start=[Diagnostics.ProcessStartInfo]::new('python3')
    $start.ArgumentList.Add((Join-Path $PSScriptRoot 'arm.py'))
    $start.ArgumentList.Add('--sql-token-pipe')
    $start.UseShellExecute=$false; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start)
    try {
        $outputTask=$process.StandardOutput.ReadToEndAsync()
        $errorTask=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(45000)){$process.Kill($true); throw 'TOKEN_DEADLINE'}
        $token=$outputTask.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0 -or $token.Length -gt 65536 -or $token -cnotmatch '^eyJ[A-Za-z0-9_.-]+$') {throw 'TOKEN'}
    } finally {$process.Dispose()}
    $connection=New-G1SqlConnection $token
    $token=$null
    $stage='OPEN'
    $connection.Open()
    $stage='TARGET'
    $cmd=$connection.CreateCommand(); $cmd.CommandTimeout=30
    $cmd.CommandText=@'
SELECT DB_NAME(),CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),
 CONVERT(int,SERVERPROPERTY(N'EngineEdition')),
 HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),
 IS_ROLEMEMBER(N'db_owner'),HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER'),
 HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE'),
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo'));
'@
    $rows=$cmd.ExecuteReader()
    if(-not $rows.Read() -or $rows.FieldCount -ne 8 -or [string]$rows[0] -cne 'db-fieldvisit-uat' -or
       [string]$rows[1] -cne 'sql-fieldvisit-jpe-uat' -or [int]$rows[2] -ne 5 -or
       $rows.IsDBNull(3) -or [int]$rows[3] -ne 1) {throw 'TARGET_VISIBILITY'}
    $least=(-not $rows.IsDBNull(4) -and -not $rows.IsDBNull(5) -and -not $rows.IsDBNull(6) -and
      [int]$rows[4] -eq 0 -and [int]$rows[5] -eq 0 -and [int]$rows[6] -eq 0)
    $tables=[int]$rows[7]
    $rows.Dispose(); $rows=$null; $cmd.Dispose(); $cmd=$null
    Write-Output 'SqlTarget=PASS; MetadataVisibility=PASS;'
    # Diagnostic permissions must never be reported as the actual API identity.
    Write-Output ('DiagnosticNonOwnerNoDdl='+$(if($least){'OBSERVED'}else{'NOT_LEAST_PRIVILEGE'})+'; ApiSqlPrincipal=UNKNOWN;')
    $stage='CATALOG'
    $cmd=$connection.CreateCommand(); $cmd.CommandTimeout=30
    $cmd.CommandText=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'catalog.sql') -Raw
    $rows=$cmd.ExecuteReader([System.Data.CommandBehavior]::SequentialAccess)
    if(-not $rows.Read() -or $rows.IsDBNull(0)) {throw 'CATALOG'}
    $text=$rows.GetTextReader(0)
    try {$json=$text.ReadToEnd()} finally {$text.Dispose()}
    if($json.Length -gt 8388608 -or $rows.Read()) {throw 'RESULT_LIMIT'}
    $rows.Dispose(); $rows=$null; $cmd.Dispose(); $cmd=$null
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
    $match=($tables -eq 69 -and $hash -ceq '742f2a95f7f58889d1609aceefa656030cf1f1365f10cc9c89968e89cfff0c63')
    Write-Output ('CandidateSchemaParity='+$(if($match){'PASS'}else{'HOLD'})+'; NewSchemaFirst=TRUE; RawCatalog=SUPPRESSED;')
    $stage='CAPABILITIES'
    $cmd=$connection.CreateCommand(); $cmd.CommandTimeout=30
    $cmd.CommandText=@'
SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SchemaVersions)=3 AND
 (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN
 (N'1.8.0-CREATE-LOWRISK',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-EXCEL-CANONICAL-STATUS'))=3
 THEN 1 ELSE 0 END;
'@
    $caps=[int]$cmd.ExecuteScalar()
    Write-Output ('CandidateCapabilityMarkers='+$(if($caps -eq 1){'PASS'}else{'HOLD'})+'; ActualCapabilitiesRequireSchemaParity=TRUE;')
    Write-Output 'SqlConnections=1; Retries=0; Inventory=COLLECTED; CompleteGate=REQUIRES_VERIFIED_RECEIPTS;'
    $status=0
} catch {
    # Stage has only hardcoded assignments, never provider text.
    Write-Output "Sql=HOLD; Stage=$stage; Details=SUPPRESSED; LiveG1=HOLD;"
} finally {
    if($null -ne $rows){$rows.Dispose()}
    if($null -ne $cmd){$cmd.Dispose()}
    if($null -ne $connection){$connection.Dispose()}
    $token=$null
}
exit $status
