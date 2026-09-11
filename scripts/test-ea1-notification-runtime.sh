#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"; cd "$repo_root"
image="${EA1_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
container_name="fieldvisit-ea1-${GITHUB_RUN_ID:-local}-$$"; sa_password='Ea1!RuntimeCore2026_TestOnly'; db_name="FieldVisitEA1_${GITHUB_RUN_ID:-local}_$$"; sqlcmd_path=''
cleanup(){ docker rm -f "$container_name" >/dev/null 2>&1 || true; }; trap cleanup EXIT INT TERM
fail(){ echo "EA1-RUNTIME-FAIL: $*" >&2; exit 1; }
command -v docker >/dev/null 2>&1 || fail "Docker is required."
echo "EA1_SQL_IMAGE=$image"
docker run -d --name "$container_name" -e ACCEPT_EULA=Y -e MSSQL_PID=Developer -e MSSQL_SA_PASSWORD="$sa_password" -p 127.0.0.1::1433 "$image" >/dev/null
for candidate in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do docker exec "$container_name" test -x "$candidate" >/dev/null 2>&1 && { sqlcmd_path="$candidate"; break; }; done
[[ -n "$sqlcmd_path" ]] || fail "sqlcmd not found."
sqlcmd=(docker exec -i "$container_name" "$sqlcmd_path" -S localhost -U sa -P "$sa_password" -C -I -b -r1)
for _ in $(seq 1 90); do "${sqlcmd[@]}" -d master -Q 'SET NOCOUNT ON;SELECT 1' >/dev/null 2>&1 && break; sleep 1; done
host_port="$(docker port "$container_name" 1433/tcp | head -n1 | awk -F: '{print $NF}')"; [[ "$host_port" =~ ^[0-9]+$ ]] || fail "mapped port missing"
echo "EA1_SQLSERVER_VERSION=$("${sqlcmd[@]}" -d master -h -1 -W -Q "SET NOCOUNT ON;SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));" | tr -d '\r' | tail -n1)"
"${sqlcmd[@]}" -d master -Q "CREATE DATABASE [$db_name];" >/dev/null
"${sqlcmd[@]}" -d "$db_name" <<'SQL'
SET NOCOUNT ON;
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(50) NOT NULL PRIMARY KEY,Description nvarchar(500) NOT NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
CREATE TABLE dbo.Users(UserId int NOT NULL PRIMARY KEY,OrganizationId int NULL,TeamId int NULL,EmployeeNo nvarchar(50) NULL,DisplayName nvarchar(200) NOT NULL,Email nvarchar(320) NULL,EntraObjectId uniqueidentifier NULL,IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL);
CREATE TABLE dbo.Employments(EmploymentId bigint NOT NULL PRIMARY KEY,PersonId bigint NOT NULL,OrganizationId int NOT NULL,EmployeeNo nvarchar(50) NULL,Email nvarchar(320) NULL,HireDate date NULL,TerminationDate date NULL,LegacyUserId int NULL,SourceType nvarchar(30) NOT NULL,SourceReference nvarchar(200) NULL,CreatedAt datetime2(3) NOT NULL,CreatedByUserId int NULL,UpdatedAt datetime2(3) NULL,UpdatedByUserId int NULL,RowVersion rowversion NOT NULL);
CREATE TABLE dbo.Roles(RoleId int NOT NULL PRIMARY KEY,RoleCode nvarchar(50) NOT NULL,RoleName nvarchar(100) NOT NULL,Description nvarchar(500) NULL,IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL);
CREATE TABLE dbo.EmploymentRoleAssignments(EmploymentRoleAssignmentId bigint NOT NULL PRIMARY KEY,EmploymentId bigint NOT NULL,RoleId int NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2(3) NOT NULL,RowVersion rowversion NOT NULL);
CREATE TABLE dbo.TeamLeaderAssignments(TeamLeaderAssignmentId bigint NOT NULL PRIMARY KEY,TeamId int NOT NULL,EmploymentId bigint NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2(3) NOT NULL,RowVersion rowversion NOT NULL);
CREATE TABLE dbo.TeamLeaderDelegations(TeamLeaderDelegationId bigint NOT NULL PRIMARY KEY,TeamLeaderAssignmentId bigint NOT NULL,DelegateEmploymentId bigint NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NOT NULL,Reason nvarchar(500) NULL,CreatedAt datetime2(3) NOT NULL,CreatedByUserId int NULL,RowVersion rowversion NOT NULL);
CREATE TABLE dbo.UserIdentityProfiles(UserId int NOT NULL PRIMARY KEY,EmploymentId bigint NULL,UserType nvarchar(30) NOT NULL,UserCode nvarchar(100) NOT NULL,IdentityProvider nvarchar(50) NOT NULL,EntraTenantId uniqueidentifier NULL,EntraObjectId uniqueidentifier NULL,ExternalOrganization nvarchar(200) NULL,ExternalTitle nvarchar(200) NULL,AuthorizationFrom date NULL,AuthorizationTo date NULL,CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL);
CREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint NOT NULL PRIMARY KEY);
CREATE TABLE dbo.SchemaMigrationDataBaselines(MigrationVersion nvarchar(50) NOT NULL PRIMARY KEY,VisitTripSnapshotCount bigint NOT NULL);
INSERT dbo.SchemaVersions VALUES(N'1.8.0-005',N'EA1 prerequisite',SYSUTCDATETIME(),N'EA1');
INSERT dbo.SchemaMigrationDataBaselines VALUES(N'1.8.0-001',0);
SQL
cat database/development/v1.8.0/compat/session-options.sql database/migrations/1800_006_notification_framework/Up.sql | "${sqlcmd[@]}" -d "$db_name"
cat database/development/v1.8.0/compat/session-options.sql database/migrations/1800_006_notification_framework/Verify.sql | "${sqlcmd[@]}" -d "$db_name" >/dev/null
echo "EA1_1800_006_VERIFY=PASS"
connection_string="Server=127.0.0.1,$host_port;Database=$db_name;User Id=sa;Password=$sa_password;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
EA1_SQL_CONNECTION="$connection_string" dotnet run --project backend/tests/FieldVisit.EA1.Integration.Tests/FieldVisit.EA1.Integration.Tests.csproj --configuration Release
