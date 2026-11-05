# Prepared disposable-only reference builder. Never dispatches or accesses UAT.
$ErrorActionPreference='Stop'
$baseline=$env:LEADER_RC_BASELINE_SCHEMA_SQL
$expectedHash=$env:LEADER_RC_BASELINE_SHA256
$origin=$env:LEADER_RC_BASELINE_PROVENANCE
if([string]::IsNullOrWhiteSpace($baseline) -or $expectedHash -notmatch '^[0-9a-f]{64}$' -or [string]::IsNullOrWhiteSpace($origin)){throw 'REFERENCE_HOLD_APPROVED_SCHEMA_ONLY_150_BASELINE_REQUIRED'}
if(!(Test-Path -LiteralPath $baseline -PathType Leaf)){throw 'REFERENCE_BASELINE_MISSING'}
if((Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedHash){throw 'REFERENCE_BASELINE_HASH_MISMATCH'}
$bootstrap=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $baseline))
if($bootstrap -match '(?i)EnsureCreated|CREATE\s+DATABASE|ALTER\s+DATABASE|RESTORE\s+DATABASE|(?m)^\s*USE\s|:r\s'){throw 'REFERENCE_BASELINE_UNSUPPORTED_OR_NONLOCAL_BATCH'}
# A baseline may insert schema-version metadata only; never real personnel/business seed rows.
foreach($insert in [regex]::Matches($bootstrap,'(?i)\bINSERT\s+(?:INTO\s+)?([^\s(]+)')){
 if(($insert.Groups[1].Value -replace '[\[\]]','') -notmatch '^(?i)(dbo\.)?SchemaVersions$'){throw 'REFERENCE_BASELINE_BUSINESS_INSERT_DENIED'}
}
Import-Module SqlServer -RequiredVersion 22.4.5.1
$root=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($env:LEADER_RC_SQL_CONNECTION)
if($root.DataSource -notin @('localhost,1433','127.0.0.1,1433') -or $root.InitialCatalog -ne 'master' -or $root.UserID -ne 'sa'){throw 'REFERENCE_NONLOCAL_TARGET_DENIED'}
$plan=Get-Content artifacts/reference-prerequisites.json -Raw|ConvertFrom-Json
if($plan.protectedBase -ne 'b1a507c90c577fb2c0cdd6a98ef33481d3a5f790'){throw 'REFERENCE_PROTECTED_PIN_MISMATCH'}
foreach($entry in $plan.migrationFiles){if((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.sha256){throw 'REFERENCE_MIGRATION_HASH_MISMATCH'}}
$db='LeaderRcReference_'+[guid]::NewGuid().ToString('N')
function Execute([string]$sql,[string]$catalog){
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($root.ConnectionString);$b.InitialCatalog=$catalog;$b.Pooling=$false
 $c=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
 try{$c.Open();foreach($batch in [regex]::Split($sql,'(?im)^\s*GO\s*(?:--[^\r\n]*)?\r?$')){if([string]::IsNullOrWhiteSpace($batch)){continue};$cmd=$c.CreateCommand();$cmd.CommandText=$batch;$cmd.CommandTimeout=120;[void]$cmd.ExecuteNonQuery()}}
 catch{throw 'REFERENCE_SQL_EXECUTION_FAILED_RAW_ERROR_SUPPRESSED'}finally{$c.Dispose()}
}
Execute "CREATE DATABASE [$db]" 'master'
try{
 Execute $bootstrap $db
 foreach($entry in $plan.migrationFiles){Execute ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $entry.path))) $db}
 $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($root.ConnectionString);$b.InitialCatalog=$db;$b.Pooling=$false
 $c=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
 try{
  $c.Open();$cmd=$c.CreateCommand();$cmd.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName'))";$server=[string]$cmd.ExecuteScalar()
  if($server -notmatch '^[a-zA-Z0-9_-]+$'){throw 'REFERENCE_LOCAL_SERVER_ID_INVALID'}
  $path='docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql'
  $original=[IO.File]::ReadAllText((Resolve-Path $path));$originalHash=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if($originalHash -ne '67551978bf16266ef8f38c018be87b7aadc321ee3db84d6d94b52a1538204734'){throw 'REFERENCE_INVENTORY_PIN_MISMATCH'}
  # Do not edit the original UAT inventory. Derive a query restricted to this disposable target.
  $derived=$original.Replace("@ExpectedServer sysname=N'sql-fieldvisit-jpe-uat'","@ExpectedServer sysname=N'$server'").Replace("@ExpectedDatabase sysname=N'db-fieldvisit-uat'","@ExpectedDatabase sysname=N'$db'")
  $derivedHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($derived))).ToLowerInvariant()
  $cmd=$c.CreateCommand();$cmd.CommandText=$derived;$cmd.CommandTimeout=120
  $adapter=[Microsoft.Data.SqlClient.SqlDataAdapter]::new($cmd);$data=[Data.DataSet]::new();[void]$adapter.Fill($data)
  . ./scripts/ci/uat_schema_fidelity_evidence.ps1
  Write-UatSchemaInventory $data $server $db $plan.protectedBase $derivedHash 'artifacts/protected-reference.schema.json'|Out-Null
  [ordered]@{state='REFERENCE_CHAIN_EXECUTED';protectedBase=$plan.protectedBase;baselineSha256=$expectedHash;baselineProvenance=$origin;originalInventorySha256=$originalHash;derivedLocalInventorySha256=$derivedHash;actualUatParity='HOLD';applicationPrincipalPermissions='HOLD'}|ConvertTo-Json|Set-Content artifacts/protected-reference.provenance.json
 }catch{throw 'REFERENCE_INVENTORY_FAILED_RAW_ERROR_SUPPRESSED'}finally{$c.Dispose()}
}finally{Execute "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]" 'master'}
