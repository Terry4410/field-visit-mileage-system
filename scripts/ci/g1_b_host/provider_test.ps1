param([switch]$Worker)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
$phase='ENVIRONMENT'
try {
 if($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'Terry4410/field-visit-mileage-system' -or $env:G1_TEST_ONLY -cne '1'){throw 'STOP'}
 $phase='MODULE_IMPORT'
 Import-Module SqlServer -RequiredVersion 22.4.5.1 -ErrorAction Stop *> $null
 # Hard-coded loopback and synthetic disposable container credentials only.
 $phase='CONNECTION_BUILDER'
 $builder=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
 $builder.DataSource='127.0.0.1,1433';$builder.InitialCatalog='master';$builder.UserID='sa';$builder.Password=$env:G1_DISPOSABLE_PASSWORD
 $builder.Encrypt=$false;$builder.TrustServerCertificate=$true;$builder.Pooling=$false;$builder.ConnectRetryCount=0;$builder.ConnectTimeout=2
 if(-not $builder.Password -or $builder.DataSource -cne '127.0.0.1,1433' -or $builder.ConnectRetryCount -ne 0){throw 'STOP'}
 if($Worker){
  $c=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
  try{$c.Open();[IO.File]::WriteAllText($env:G1_WORKER_STARTED,'STARTED');$q=$c.CreateCommand();$q.CommandTimeout=30;$q.CommandText="WAITFOR DELAY '00:00:20'; SELECT 1";try{[void]$q.ExecuteScalar()}finally{$q.Dispose()};[IO.File]::WriteAllText($env:G1_WORKER_LATER,'LATER')}finally{$c.Dispose()};exit 0
 }
 # Setup readiness only: separate from the measured connection under test.
 $phase='SERVER_READINESS'
 $ready=$false
 for($i=0;$i -lt 45 -and -not $ready;$i++){ $p=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);try{$p.Open();$ready=$true}catch{Start-Sleep -Seconds 2}finally{$p.Dispose()} }
 if(-not $ready){throw 'STOP'}
 Write-Output 'ProviderBoundary=READINESS_PASS;'
 $phase='RUNSPACE_CONNECTION_CREATE'
 $c=[Microsoft.Data.SqlClient.SqlConnection]::new($builder.ConnectionString);$rs=$null;$ps=$null
 try{
  if($c.State -ne [Data.ConnectionState]::Closed){throw 'STOP'}
  $phase='RUNSPACE_CREATE'
  $rs=[runspacefactory]::CreateRunspace();$rs.Open();$ps=[powershell]::Create();$ps.Runspace=$rs
  [void]$ps.AddScript('param($c,$original) if(-not [object]::ReferenceEquals($c,$original) -or $c.State -ne [Data.ConnectionState]::Closed){throw "STOP"};$c.Open();$q=$c.CreateCommand();try{$q.CommandText="SELECT 1";return $q.ExecuteScalar()}finally{$q.Dispose()}').AddArgument($c).AddArgument($c)
  $phase='RUNSPACE_OPEN_AND_REFERENCE'
  $out=$ps.Invoke();if($ps.HadErrors -or $out.Count -ne 1 -or [int]$out[0] -ne 1 -or $c.State -ne [Data.ConnectionState]::Open){throw 'STOP'}
  Write-Output 'ProviderBoundary=RUNSPACE_OPEN_REFERENCE_PASS;'
  $phase='COMMAND_TIMEOUT'
  $q=$c.CreateCommand();try{$q.CommandTimeout=1;$q.CommandText="WAITFOR DELAY '00:00:04'; SELECT 1";$timed=$false;try{[void]$q.ExecuteScalar()}catch [Microsoft.Data.SqlClient.SqlException]{if($_.Exception.Number -eq -2){$timed=$true}};if(-not $timed){throw 'STOP'}}finally{$q.Dispose()}
  Write-Output 'ProviderBoundary=COMMAND_TIMEOUT_PASS;'
  $phase='COMMAND_CANCEL'
  $q=$c.CreateCommand();try{$q.CommandTimeout=15;$q.CommandText="WAITFOR DELAY '00:00:12'; SELECT 1";$ar=$q.BeginExecuteNonQuery();Start-Sleep -Milliseconds 500;$q.Cancel();$cancelled=$false;try{[void]$q.EndExecuteNonQuery($ar)}catch [Microsoft.Data.SqlClient.SqlException]{$cancelled=$true};if(-not $cancelled){throw 'STOP'}}finally{$q.Dispose()}
  Write-Output 'ProviderBoundary=COMMAND_CANCEL_PASS;'
 }finally{if($null -ne $ps){$ps.Dispose()};if($null -ne $rs){$rs.Dispose()};$c.Dispose()}
 $phase='CONNECTION_CLEANUP'
 if($c.State -ne [Data.ConnectionState]::Closed){throw 'STOP'}
 Write-Output 'ProviderBoundary=CONNECTION_CLEANUP_PASS;'
 $phase='OPEN_FAILURE'
 $bad=[Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new($builder.ConnectionString);$bad.DataSource='127.0.0.1,1';$bad.ConnectTimeout=1
 $fail=[Microsoft.Data.SqlClient.SqlConnection]::new($bad.ConnectionString);$failed=$false;try{try{$fail.Open()}catch [Microsoft.Data.SqlClient.SqlException]{$failed=$true}}finally{$fail.Dispose()};if(-not $failed -or $fail.State -ne [Data.ConnectionState]::Closed){throw 'STOP'}
 Write-Output 'ProviderBoundary=OPEN_FAILURE_CLEANUP_PASS;'
 Write-Output 'ProviderSynthetic=PASS; Cases=5; ProductionAdapterCollector=NOT_TESTED;'
}catch{
 $allowed=@('ENVIRONMENT','MODULE_IMPORT','CONNECTION_BUILDER','SERVER_READINESS','RUNSPACE_CONNECTION_CREATE','RUNSPACE_CREATE','RUNSPACE_OPEN_AND_REFERENCE','COMMAND_TIMEOUT','COMMAND_CANCEL','CONNECTION_CLEANUP','OPEN_FAILURE')
 if($phase -cnotin $allowed){$phase='UNKNOWN'}
 Write-Output "ProviderSynthetic=STOP; Phase=$phase; Details=SUPPRESSED;";exit 1
}
