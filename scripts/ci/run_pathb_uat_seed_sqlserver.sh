#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
sql="$root/database/uat/FieldVisit_PathB_UAT_ThreeRole_Seed_FINAL_CANDIDATE.sql"
schema="$root/scripts/ci/pathb_uat_seed_schema.sql"
container=pathb-seed-sql
sqlcmd=(docker exec -i "$container" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PATHB_SQL_PASSWORD" -C -I -b)
create_db(){ "${sqlcmd[@]}" -d master -Q "IF DB_ID(N'$1') IS NOT NULL DROP DATABASE [$1]; CREATE DATABASE [$1];" >/dev/null; "${sqlcmd[@]}" -d "$1" <"$schema" >/dev/null; }
session_marker="EXEC sys.sp_set_session_context @key=N'PATHB_DISPOSABLE_CI',@value=N'd6c220877b9976b6478d85b5171d4e06293595ae';"
run_seed(){ { echo "$session_marker"; cat "$sql"; } | "${sqlcmd[@]}" -d "$1"; }
readback(){
  { echo "$session_marker"; cat "$root/database/uat/FieldVisit_PathB_UAT_ThreeRole_Seed_READBACK.sql"; } |
    "${sqlcmd[@]}" -h-1 -W -s '|' -d "$1"
}
expect_seed_failure(){
  local expected="$1" output
  shift
  if output=$("$@" 2>&1); then
    echo "Expected failure: $expected" >&2
    return 1
  fi
  grep -Fq "$expected" <<< "$output" || { printf '%s\n' "$output" >&2; return 1; }
}
counts(){ "${sqlcmd[@]}" -h-1 -W -d "$1" -Q "SET NOCOUNT ON;SELECT (SELECT COUNT(*) FROM dbo.Organizations)+(SELECT COUNT(*) FROM dbo.Teams)+(SELECT COUNT(*) FROM dbo.Roles)+(SELECT COUNT(*) FROM dbo.Users)+(SELECT COUNT(*) FROM dbo.UserIdentityProfiles)+(SELECT COUNT(*) FROM dbo.UserEmploymentPeriods)+(SELECT COUNT(*) FROM dbo.UserRoleAssignments)+(SELECT COUNT(*) FROM dbo.UserTeamAssignments);" | tr -d '\r[:space:]'; }

create_db PathBSeedCI_Positive
applied=$(run_seed PathBSeedCI_Positive)
grep -q PATHB_UAT_THREE_ROLE_SEED_APPLIED <<< "$applied"
grep -q "|1|1|3|3|3|3|3|3|PASS$" <<< "$(readback PathBSeedCI_Positive)"
test "$(counts PathBSeedCI_Positive)" = 20
# Effective login data chain and role aliases.
test "$("${sqlcmd[@]}" -h-1 -W -d PathBSeedCI_Positive -Q "SET NOCOUNT ON;DECLARE @d date=CONVERT(date,DATEADD(MINUTE,480,SYSUTCDATETIME()));SELECT COUNT(*) FROM dbo.Users u JOIN dbo.UserIdentityProfiles i ON i.UserId=u.UserId JOIN dbo.UserEmploymentPeriods e ON e.UserId=u.UserId JOIN dbo.UserRoleAssignments a ON a.UserId=u.UserId JOIN dbo.Roles r ON r.RoleId=a.RoleId JOIN dbo.UserTeamAssignments t ON t.UserId=u.UserId WHERE u.IsActive=1 AND i.UserType=N'Internal' AND i.IdentityProvider=N'Demo' AND i.EmploymentId IS NULL AND e.EmploymentStatus=N'Active' AND e.EffectiveFrom<=@d AND (e.EffectiveTo IS NULL OR e.EffectiveTo>=@d) AND a.EffectiveFrom<=@d AND (a.EffectiveTo IS NULL OR a.EffectiveTo>=@d) AND t.IsPrimary=1 AND t.EffectiveFrom<=@d AND (t.EffectiveTo IS NULL OR t.EffectiveTo>=@d) AND ((u.EmployeeNo=N'pilotv01' AND r.RoleCode=N'visitor') OR (u.EmployeeNo=N'pilotl01' AND r.RoleCode=N'leader') OR (u.EmployeeNo=N'pilota01' AND r.RoleCode=N'admin'));" | tr -d '\r[:space:]')" = 3

# Duplicate execution must fail without changing the committed 20 rows.
expect_seed_failure PATHB_SEED_NAMESPACE_COLLISION_HOLD run_seed PathBSeedCI_Positive
test "$(counts PathBSeedCI_Positive)" = 20

# Wrong target / absent CI marker must fail before writes.
create_db PathBSeedCI_Negative
expect_seed_failure PATHB_SEED_EXACT_TARGET_HOLD "${sqlcmd[@]}" -d PathBSeedCI_Negative <"$sql"
test "$(counts PathBSeedCI_Negative)" = 0

# Mid-batch constraint failure must roll every earlier insert back.
create_db PathBSeedCI_Rollback
"${sqlcmd[@]}" -d PathBSeedCI_Rollback -Q "ALTER TABLE dbo.Users ADD CONSTRAINT CK_Test_PartialFailure CHECK(EmployeeNo NOT LIKE N'pilot%');" >/dev/null
expect_seed_failure CK_Test_PartialFailure run_seed PathBSeedCI_Rollback
test "$(counts PathBSeedCI_Rollback)" = 0

# An independently held application lock must produce immediate collision HOLD.
create_db PathBSeedCI_Collision
( "${sqlcmd[@]}" -d PathBSeedCI_Collision -Q "BEGIN TRAN;DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'FieldVisit.PathB.UAT.ThreeRoleSeed.v1',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;WAITFOR DELAY '00:00:06';ROLLBACK;" >/dev/null ) & blocker=$!
sleep 1
expect_seed_failure PATHB_SEED_CONCURRENT_EXECUTION_HOLD run_seed PathBSeedCI_Collision
test "$(counts PathBSeedCI_Collision)" = 0
wait "$blocker"
create_db PathBSeedCI_Ambient
expect_seed_failure PATHB_SEED_REQUIRES_AUTOCOMMIT_SESSION_HOLD "${sqlcmd[@]}" -d PathBSeedCI_Ambient < <(
  printf '%s\n' 'BEGIN TRANSACTION;'
  cat "$sql"
)
test "$(counts PathBSeedCI_Ambient)" = 0

# An effective assignment to the wrong role must not yield a PASS receipt.
"${sqlcmd[@]}" -d PathBSeedCI_Positive -Q "
  UPDATE dbo.UserRoleAssignments
  SET RoleId=(SELECT RoleId FROM dbo.Roles WHERE RoleCode=N'leader')
  WHERE UserId=(SELECT UserId FROM dbo.Users WHERE EmployeeNo=N'pilotv01');
" >/dev/null
grep -q "|HOLD$" <<< "$(readback PathBSeedCI_Positive)"
echo PATHB_UAT_SEED_SQLSERVER_CONTRACT=PASS
