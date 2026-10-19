# Local SQL Server 2022 provider proof; NEVER calls the production Azure wrapper.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$c=$null
try {
  if($env:GITHUB_ACTIONS -cne 'true' -or [string]::IsNullOrWhiteSpace($env:SCHEMA_TEST_SQL_PASSWORD)){throw 'guard'}
  Import-Module SqlServer -RequiredVersion 22.4.5.1
  $tokens=$null; $errors=$null
  $ast=[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'r3_connection_only_wake.ps1'),[ref]$tokens,[ref]$errors)
  if($errors.Count -ne 0){throw 'syntax'}
  $text=$ast.Extent.Text
  if(@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Value -ceq 'Open'},$true)).Count -ne 1){throw 'count'}
  foreach($required in ('$builder.ConnectTimeout=120','$builder.ConnectRetryCount=0','$builder.Pooling=$false','$builder.TrustServerCertificate=$false')){
    if(-not $text.Contains($required)){throw 'bounds'}
  }
  $b=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
  $b.DataSource='localhost,1433'; $b.InitialCatalog='master'
  $b.UserID='sa'; $b.Password=$env:SCHEMA_TEST_SQL_PASSWORD
  $b.ConnectTimeout=120; $b.ConnectRetryCount=0; $b.Pooling=$false
  $b['Encrypt']='True'; $b.TrustServerCertificate=$true # Disposable TLS certificate only.
  $b.ApplicationIntent=[Microsoft.Data.SqlClient.ApplicationIntent]::ReadOnly
  $c=[Microsoft.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
  $c.Open()
  if($c.State -ne [System.Data.ConnectionState]::Open){throw 'connection'}
  $c.Dispose(); $c=$null
  Write-Output 'Connection-only provider proof: 1 passed, 0 failed (disposable only; no SQL command)'
} catch {
  Write-Output 'STOP: connection-only disposable verification failed; details suppressed.'
  exit 1
} finally {if($null -ne $c){$c.Dispose()}}
