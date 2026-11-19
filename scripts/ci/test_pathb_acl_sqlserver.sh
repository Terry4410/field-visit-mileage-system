#!/usr/bin/env bash
set -euo pipefail
container=pathb-acl-sql
sqlcmd=(docker exec -i "$container" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PATHB_SQL_PASSWORD" -C -I -b -V 16)
root="$(cd "$(dirname "$0")/../.." && pwd)"
"${sqlcmd[@]}" -d master -Q 'CREATE DATABASE PathBACLDisposable;' >/dev/null
"${sqlcmd[@]}" -d PathBACLDisposable < "$root/scripts/ci/pathb_acl_behavior.sql" >/dev/null
execsql(){ "${sqlcmd[@]}" -d PathBACLDisposable -Q "$1"; }
expect_sql_error(){
  local output
  if output=$(execsql "$1" 2>&1);then printf 'Expected SQL denial/failure, got success: %s\n' "$1" >&2;exit 1;fi
  grep -Eq 'permission was denied|Cannot find the object|does not exist' <<< "$output" || { printf '%s\n' "$output" >&2;exit 1; }
}
expect_sql_error "EXECUTE AS USER=N'pathb_login_test';SELECT COUNT(*) FROM dbo.UserIdentityProfiles;REVERT;"
test "$(execsql "SET NOCOUNT ON; SELECT IS_ROLEMEMBER(N'db_datareader',N'pathb_login_test')+IS_ROLEMEMBER(N'db_owner',N'pathb_login_test');" | tr -d '\r[:space:]')" = '0'
execsql "SET XACT_ABORT ON;BEGIN TRY BEGIN TRANSACTION;GRANT SELECT ON OBJECT::dbo.Organizations TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.NotAnExistingTable TO [pathb_login_test];COMMIT TRANSACTION;END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;END CATCH;" >/dev/null
expect_sql_error "EXECUTE AS USER=N'pathb_login_test';SELECT COUNT(*) FROM dbo.Organizations;REVERT;"
execsql "SET XACT_ABORT ON;BEGIN TRANSACTION;GRANT SELECT ON OBJECT::dbo.Organizations TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.Roles TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.Teams TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.UserEmploymentPeriods TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.UserIdentityProfiles TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.UserRoleAssignments TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.Users TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.UserTeamAssignments TO [pathb_login_test];COMMIT TRANSACTION;" >/dev/null
for name in Organizations Roles Teams UserEmploymentPeriods UserIdentityProfiles UserRoleAssignments Users UserTeamAssignments;do
  execsql "EXECUTE AS USER=N'pathb_login_test';SELECT COUNT(*) FROM dbo.[$name];REVERT;" >/dev/null
  expect_sql_error "EXECUTE AS USER=N'pathb_outsider_test';SELECT COUNT(*) FROM dbo.[$name];REVERT;"
done
execsql "DENY SELECT ON OBJECT::dbo.UserIdentityProfiles TO [pathb_login_test];" >/dev/null
expect_sql_error "EXECUTE AS USER=N'pathb_login_test';SELECT COUNT(*) FROM dbo.UserIdentityProfiles;REVERT;"
execsql "REVOKE SELECT ON OBJECT::dbo.UserIdentityProfiles TO [pathb_login_test];GRANT SELECT ON OBJECT::dbo.UserIdentityProfiles TO [pathb_login_test];DENY SELECT ON OBJECT::dbo.UserIdentityProfiles(SecretTest) TO [pathb_login_test];" >/dev/null
expect_sql_error "EXECUTE AS USER=N'pathb_login_test';SELECT SecretTest FROM dbo.UserIdentityProfiles;REVERT;"
test "$(execsql "SET NOCOUNT ON;SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals u ON p.grantee_principal_id=u.principal_id WHERE u.name=N'pathb_login_test' AND p.permission_name=N'SELECT' AND (p.class=0 OR p.class=3);" | tr -d '\r[:space:]')" = 0
printf 'PATHB_ACL_DISPOSABLE_SQLSERVER_BEHAVIOR=PASS\n'
