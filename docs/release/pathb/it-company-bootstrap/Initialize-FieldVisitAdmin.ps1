# IT review candidate. Windows PowerShell 5.1 / System.Data.SqlClient.
# DBA must validate tenant/object identity and authorization independently.
[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='High')]
param(
 [Parameter(Mandatory)][ValidatePattern('^tcp:[A-Za-z0-9.-]+,[0-9]{1,5}$')][string]$Endpoint,
 [Parameter(Mandatory)][ValidateLength(1,128)][string]$ServerName,
 [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9_-]{0,127}$')][string]$DatabaseName,
 [Parameter(Mandatory)][ValidateLength(1,50)][string]$OrganizationCode,
 [Parameter(Mandatory)][ValidateLength(1,200)][string]$OrganizationName,
 [Parameter(Mandatory)][ValidateLength(1,50)][string]$EmployeeNo,
 [Parameter(Mandatory)][ValidateLength(1,200)][string]$DisplayName,
 [Parameter(Mandatory)][ValidateLength(1,320)][string]$Email,
 [Parameter(Mandatory)][guid]$TenantId,
 [Parameter(Mandatory)][guid]$ObjectId,
 [Parameter(Mandatory)][datetime]$HireDate
)
$ErrorActionPreference='Stop'
if (!$PSCmdlet.ShouldProcess("$Endpoint / $DatabaseName","Initialize one approved organization and first Entra Admin in empty database")) { return }
$b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$b.DataSource=$Endpoint; $b.InitialCatalog=$DatabaseName
$b.IntegratedSecurity=$true; $b.Encrypt=$true; $b.TrustServerCertificate=$false
$b.ConnectTimeout=15; $b.Pooling=$false
$c=New-Object System.Data.SqlClient.SqlConnection($b.ConnectionString)
try {
 $c.Open()
 $cmd=$c.CreateCommand()
 $cmd.CommandTimeout=30
 $cmd.CommandText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'first-admin.sql'))
 $values=@{
 ServerName=$ServerName; DatabaseName=$DatabaseName; OrganizationCode=$OrganizationCode
 OrganizationName=$OrganizationName; EmployeeNo=$EmployeeNo; DisplayName=$DisplayName; Email=$Email
 TenantId=$TenantId; ObjectId=$ObjectId; HireDate=$HireDate.Date
 }
 foreach($k in $values.Keys) {
  if($k -in @('TenantId','ObjectId')) {$p=$cmd.Parameters.Add("@$k",[Data.SqlDbType]::UniqueIdentifier)}
  elseif($k -eq 'HireDate') {$p=$cmd.Parameters.Add("@$k",[Data.SqlDbType]::Date)}
  else {$p=$cmd.Parameters.Add("@$k",[Data.SqlDbType]::NVarChar,-1)}
  $p.Value=$values[$k]
 }
 $r=$cmd.ExecuteReader()
 try {
  if(!$r.Read()){throw 'Bootstrap readback missing; do not retry'}
  Write-Output ("FIRST_ADMIN_BOOTSTRAP_PASS UserId={0} OrganizationId={1}" -f $r.GetInt32(0),$r.GetInt32(1))
 } finally {$r.Dispose()}
} finally {$c.Dispose()}
