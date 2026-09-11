#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

image="${DB_WORK1_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
container_name="fieldvisit-dbw1-${GITHUB_RUN_ID:-local}-$$"
sa_password='DbWork1!SnapshotSafe2026_TestOnly'
tmp_dir="$(mktemp -d)"
sqlcmd_path=''
session_options='database/development/v1.8.0/compat/session-options.sql'
fixture='scripts/fixtures/db-work1-prereq.sql'
up='database/migrations/1800_005_project_visit_rate_lifecycle/Up.sql'
verify='database/migrations/1800_005_project_visit_rate_lifecycle/Verify.sql'
main_db="FieldVisitDBW1_${GITHUB_RUN_ID:-local}_$$"
physical_delete_escapes=0

cleanup() {
  docker rm -f "$container_name" >/dev/null 2>&1 || true
  rm -rf "$tmp_dir"
}
trap cleanup EXIT INT TERM
fail() { echo "DB-WORK1-FAIL: $*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "Docker is required."

echo "DBW1_SQL_IMAGE=$image"
docker run -d --name "$container_name" \
  -e ACCEPT_EULA=Y \
  -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$sa_password" \
  -p 127.0.0.1::1433 \
  "$image" >/dev/null

for candidate in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
  if docker exec "$container_name" test -x "$candidate" >/dev/null 2>&1; then
    sqlcmd_path="$candidate"; break
  fi
done
[[ -n "$sqlcmd_path" ]] || { docker logs "$container_name" >&2 || true; fail "sqlcmd not found."; }

sqlcmd_base=(docker exec -i "$container_name" "$sqlcmd_path" -S localhost -U sa -P "$sa_password" -C -I -b -r1)
ready=0
for _ in $(seq 1 90); do
  if "${sqlcmd_base[@]}" -d master -Q 'SET NOCOUNT ON; SELECT 1;' >/dev/null 2>&1; then ready=1; break; fi
  sleep 1
done
[[ "$ready" == 1 ]] || { docker logs "$container_name" >&2 || true; fail "SQL Server did not become ready."; }

host_port="$(docker port "$container_name" 1433/tcp | head -n1 | awk -F: '{print $NF}')"
[[ "$host_port" =~ ^[0-9]+$ ]] || fail "Could not determine mapped SQL Server port."
echo "DBW1_SQLSERVER_VERSION=$("${sqlcmd_base[@]}" -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));" | tr -d '\r' | tail -n1)"

sql_file() { local database="$1" file="$2"; "${sqlcmd_base[@]}" -d "$database" < "$file"; }
sql_apply() { local database="$1" file="$2"; { cat "$session_options"; cat "$file"; } | "${sqlcmd_base[@]}" -d "$database"; }
sql_query() { local database="$1" query="$2"; { cat "$session_options"; printf '%s\n' "$query"; } | "${sqlcmd_base[@]}" -d "$database"; }
sql_scalar() {
  local database="$1" query="$2"
  { cat "$session_options"; printf '%s\n' "$query"; } \
    | "${sqlcmd_base[@]}" -h -1 -W -d "$database" \
    | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -n1
}
wait_lock_held() {
  local database="$1" resource="$2" escaped result
  escaped="${resource//\'/\'\'}"
  for _ in $(seq 1 150); do
    result="$(sql_scalar "$database" "SET NOCOUNT ON; SELECT APPLOCK_TEST(N'public',N'$escaped',N'Exclusive',N'Session');" 2>/dev/null || true)"
    [[ "$result" == 0 ]] && return 0
    sleep 0.1
  done
  fail "Timed out waiting for barrier lock: $resource"
}
create_database() {
  local database="$1"
  "${sqlcmd_base[@]}" -d master -Q "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END; CREATE DATABASE [$database]; ALTER DATABASE [$database] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE; ALTER DATABASE [$database] SET ALLOW_SNAPSHOT_ISOLATION ON;" >/dev/null
  sql_file "$database" "$fixture" >/dev/null
  sql_apply "$database" "$up" >/dev/null
  sql_file "$database" "$verify" >/dev/null
  [[ "$(sql_scalar "$database" "SET NOCOUNT ON; SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name=DB_NAME();")" == 1 ]] || fail "$database RCSI is not ON."
  [[ "$(sql_scalar "$database" "SET NOCOUNT ON; SELECT CASE WHEN snapshot_isolation_state_desc=N'ON' THEN 1 ELSE 0 END FROM sys.databases WHERE name=DB_NAME();")" == 1 ]] || fail "$database SNAPSHOT is not ON."
}
mk_session() { local path="$1" body="$2"; { cat "$session_options"; printf '%s\n' "$body"; } > "$path"; }
parse_outcome() {
  local log="$1" marker="$2"
  tr -d '\r' < "$log" | grep -E "^${marker}=(COMMIT|ERROR:[0-9]+)$" | tail -n1 || true
}
require_commit() { local line="$1" label="$2"; [[ "$line" == *=COMMIT ]] || { echo "$line" >&2; fail "$label expected COMMIT."; }; }
require_error() {
  local line="$1" label="$2"; shift 2
  [[ "$line" =~ =ERROR:([0-9]+)$ ]] || { echo "$line" >&2; fail "$label expected SQL error."; }
  local number="${BASH_REMATCH[1]}" allowed
  for allowed in "$@"; do [[ "$number" == "$allowed" ]] && return 0; done
  fail "$label unexpected SQL error $number."
}

run_serialized_pair() {
  local case_id="$1" body_a="$2" body_b="$3"
  local barrier="DBW1.$case_id.A_READY"
  local a_sql="$tmp_dir/${case_id}_a.sql" b_sql="$tmp_dir/${case_id}_b.sql"
  local a_log="$tmp_dir/${case_id}_a.log" b_log="$tmp_dir/${case_id}_b.log"
  mk_session "$a_sql" "
SET NOCOUNT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  $body_a
  DECLARE @br int;
  EXEC @br=sys.sp_getapplock @Resource=N'$barrier',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
  IF @br<0 THROW 54901,N'barrier A lock failed',1;
  WAITFOR DELAY '00:00:02';
  COMMIT TRANSACTION;
  PRINT N'DBW1_${case_id}_A=COMMIT';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  PRINT N'DBW1_${case_id}_A=ERROR:'+CONVERT(nvarchar(20),ERROR_NUMBER());
END CATCH;"
  mk_session "$b_sql" "
SET NOCOUNT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  $body_b
  COMMIT TRANSACTION;
  PRINT N'DBW1_${case_id}_B=COMMIT';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  PRINT N'DBW1_${case_id}_B=ERROR:'+CONVERT(nvarchar(20),ERROR_NUMBER());
END CATCH;"
  "${sqlcmd_base[@]}" -d "$main_db" < "$a_sql" >"$a_log" 2>&1 & local pid_a=$!
  wait_lock_held "$main_db" "$barrier"
  "${sqlcmd_base[@]}" -d "$main_db" < "$b_sql" >"$b_log" 2>&1 & local pid_b=$!
  wait "$pid_a" || true; wait "$pid_b" || true
  local oa ob
  oa="$(parse_outcome "$a_log" "DBW1_${case_id}_A")"; ob="$(parse_outcome "$b_log" "DBW1_${case_id}_B")"
  [[ -n "$oa" && -n "$ob" ]] || { cat "$a_log" "$b_log" >&2; fail "$case_id missing machine outcomes."; }
  echo "$oa"; echo "$ob"; CASE_A="$oa"; CASE_B="$ob"
}
assert_series() {
  local org_pred="$1" vehicle="$2" expected="$3" label="$4" actual
  actual="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.MileageRateRules WHERE $org_pred AND VehicleType=N'$vehicle' AND IsActive=1;")"
  [[ "$actual" == "$expected" ]] || fail "$label active row count expected $expected got $actual."
}
assert_global_invariants() {
  local dup overlap derived terminal illegal
  dup="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM (SELECT OrganizationId,VehicleType,EffectiveFrom FROM dbo.MileageRateRules WHERE IsActive=1 GROUP BY OrganizationId,VehicleType,EffectiveFrom HAVING COUNT(*)>1)d;")"
  overlap="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.MileageRateRules a JOIN dbo.MileageRateRules b ON ((a.OrganizationId=b.OrganizationId) OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL)) AND a.VehicleType=b.VehicleType AND a.MileageRateRuleId<b.MileageRateRuleId AND a.IsActive=1 AND b.IsActive=1 AND a.EffectiveFrom<=COALESCE(b.EffectiveTo,CONVERT(date,'99991231')) AND b.EffectiveFrom<=COALESCE(a.EffectiveTo,CONVERT(date,'99991231'));")"
  derived="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM (SELECT EffectiveTo,LEAD(EffectiveFrom) OVER(PARTITION BY OrganizationId,VehicleType ORDER BY EffectiveFrom,MileageRateRuleId) NextFrom FROM dbo.MileageRateRules WHERE IsActive=1)x WHERE NextFrom IS NOT NULL AND (EffectiveTo IS NULL OR EffectiveTo<>DATEADD(day,-1,NextFrom));")"
  terminal="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM (SELECT EffectiveTo,LEAD(EffectiveFrom) OVER(PARTITION BY OrganizationId,VehicleType ORDER BY EffectiveFrom,MileageRateRuleId) NextFrom FROM dbo.MileageRateRules WHERE IsActive=1)x WHERE NextFrom IS NULL AND EffectiveTo IS NOT NULL;")"
  illegal="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.MileageRateRules WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN(N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2,N'CAR' COLLATE Latin1_General_100_BIN2);")"
  echo "DBW1_DUPLICATE_START_VIOLATIONS=$dup"
  echo "DBW1_OVERLAP_VIOLATIONS=$overlap"
  echo "DBW1_DERIVED_VIOLATIONS=$derived"
  echo "DBW1_TERMINAL_VIOLATIONS=$terminal"
  echo "DBW1_ILLEGAL_VEHICLE_VIOLATIONS=$illegal"
  echo "DBW1_PHYSICAL_DELETE_ESCAPES=$physical_delete_escapes"
  [[ "$dup" == 0 && "$overlap" == 0 && "$derived" == 0 && "$terminal" == 0 && "$illegal" == 0 && "$physical_delete_escapes" == 0 ]] || fail "Final MileageRate invariant violations are non-zero."
}

create_database "$main_db"
echo "DBW1_RCSI=ON"
echo "DBW1_SNAPSHOT=ON"
echo "DBW1_INDEPENDENT_SESSIONS=YES"

run_serialized_pair "R01_SAME_START" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1001,N'A',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1);" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1001,N'B',N'MOTORCYCLE',2.6,'2026-01-01',NULL,1);"
require_commit "$CASE_A" "R01 A"; require_error "$CASE_B" "R01 B" 2601 2627
assert_series "OrganizationId=1001" MOTORCYCLE 1 R01
echo "DBW1_RACE_01=PASS WINNER=A LOSER=B_ROLLBACK"

run_serialized_pair "R02_DIFFERENT_START" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1002,N'A',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1);" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1002,N'B',N'MOTORCYCLE',2.6,'2026-07-01',NULL,1);"
require_commit "$CASE_A" "R02 A"; require_commit "$CASE_B" "R02 B"
[[ "$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT CONVERT(varchar(10),EffectiveTo,23) FROM dbo.MileageRateRules WHERE OrganizationId=1002 AND EffectiveFrom='2026-01-01';")" == "2026-06-30" ]] || fail "R02 derived boundary wrong."
echo "DBW1_RACE_02=PASS WINNER=BOTH LOSER=NONE"

seed3="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1003,N'Seed',N'MOTORCYCLE',2.4,'2026-01-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
run_serialized_pair "R03_CREATE_UPDATE" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1003,N'Future',N'MOTORCYCLE',2.8,'2026-07-01',NULL,1);" \
"UPDATE dbo.MileageRateRules SET EffectiveFrom='2026-02-01',EffectiveTo=NULL WHERE MileageRateRuleId=$seed3;"
require_commit "$CASE_A" "R03 A"; require_commit "$CASE_B" "R03 B"
echo "DBW1_RACE_03=PASS WINNER=BOTH LOSER=NONE"

sql_query "$main_db" "INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1004,N'One',N'MOTORCYCLE',2.1,'2026-01-01',NULL,1),(1004,N'Two',N'MOTORCYCLE',2.2,'2026-07-01',NULL,1);" >/dev/null
id41="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT MileageRateRuleId FROM dbo.MileageRateRules WHERE OrganizationId=1004 AND EffectiveFrom='2026-01-01';")"
id42="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT MileageRateRuleId FROM dbo.MileageRateRules WHERE OrganizationId=1004 AND EffectiveFrom='2026-07-01';")"
run_serialized_pair "R04_UPDATE_UPDATE" \
"UPDATE dbo.MileageRateRules SET EffectiveFrom='2026-02-01',EffectiveTo=NULL WHERE MileageRateRuleId=$id41;" \
"UPDATE dbo.MileageRateRules SET EffectiveFrom='2026-08-01',EffectiveTo=NULL WHERE MileageRateRuleId=$id42;"
require_commit "$CASE_A" "R04 A"; require_commit "$CASE_B" "R04 B"
echo "DBW1_RACE_04=PASS WINNER=BOTH LOSER=NONE"

id5="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1005,N'Old',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
run_serialized_pair "R05_DEACTIVATE_CREATE" \
"UPDATE dbo.MileageRateRules SET IsActive=0,InactivatedAt=SYSUTCDATETIME(),InactivatedByUserId=99 WHERE MileageRateRuleId=$id5;" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1005,N'New',N'MOTORCYCLE',2.7,'2026-01-01',NULL,1);"
require_commit "$CASE_A" "R05 A"; require_commit "$CASE_B" "R05 B"
assert_series "OrganizationId=1005" MOTORCYCLE 1 R05
echo "DBW1_RACE_05=PASS WINNER=BOTH LOSER=NONE"

id6="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1006,N'Inactive',N'MOTORCYCLE',2.5,'2026-01-01',NULL,0); SELECT CONVERT(int,SCOPE_IDENTITY());")"
run_serialized_pair "R06_REACTIVATE_CREATE" \
"UPDATE dbo.MileageRateRules SET IsActive=1,InactivatedAt=NULL,InactivatedByUserId=NULL,EffectiveTo=NULL WHERE MileageRateRuleId=$id6;" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1006,N'Concurrent',N'MOTORCYCLE',2.9,'2026-01-01',NULL,1);"
require_commit "$CASE_A" "R06 A"; require_error "$CASE_B" "R06 B" 2601 2627
echo "DBW1_RACE_06=PASS WINNER=A LOSER=B_ROLLBACK"

id7="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1007,N'Seed',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
bar7a='DBW1.R07.A_READY'; bar7b='DBW1.R07.B_DONE'
mk_session "$tmp_dir/r07a.sql" "
SET NOCOUNT ON; SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @seen int=(SELECT COUNT(*) FROM dbo.MileageRateRules WHERE OrganizationId=1007 AND VehicleType=N'MOTORCYCLE');
 DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$bar7a',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
 WHILE APPLOCK_TEST(N'public',N'$bar7b',N'Exclusive',N'Session')<>0 WAITFOR DELAY '00:00:00.100';
 UPDATE dbo.MileageRateRules SET RatePerKm=3.1,EffectiveTo=NULL WHERE MileageRateRuleId=$id7;
 COMMIT; PRINT N'DBW1_R07_A=COMMIT';
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; PRINT N'DBW1_R07_A=ERROR:'+CONVERT(nvarchar(20),ERROR_NUMBER()); END CATCH;"
mk_session "$tmp_dir/r07b.sql" "
SET NOCOUNT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1007,N'B',N'MOTORCYCLE',2.7,'2026-07-01',NULL,1);
 COMMIT;
 DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$bar7b',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
 PRINT N'DBW1_R07_B=COMMIT'; WAITFOR DELAY '00:00:02';
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; PRINT N'DBW1_R07_B=ERROR:'+CONVERT(nvarchar(20),ERROR_NUMBER()); END CATCH;"
"${sqlcmd_base[@]}" -d "$main_db" < "$tmp_dir/r07a.sql" >"$tmp_dir/r07a.log" 2>&1 & p7a=$!
wait_lock_held "$main_db" "$bar7a"
"${sqlcmd_base[@]}" -d "$main_db" < "$tmp_dir/r07b.sql" >"$tmp_dir/r07b.log" 2>&1 & p7b=$!
wait "$p7a" || true; wait "$p7b" || true
o7a="$(parse_outcome "$tmp_dir/r07a.log" DBW1_R07_A)"; o7b="$(parse_outcome "$tmp_dir/r07b.log" DBW1_R07_B)"
require_error "$o7a" "R07 A" 3960; require_commit "$o7b" "R07 B"
echo "$o7a"; echo "$o7b"; echo "DBW1_RACE_07=PASS WINNER=B LOSER=A_ROLLBACK"

id8="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1008,N'Seed',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
bar8b='DBW1.R08.B_READY'; bar8a='DBW1.R08.A_DONE'
mk_session "$tmp_dir/r08b.sql" "
SET NOCOUNT ON; SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @seen int=(SELECT COUNT(*) FROM dbo.MileageRateRules WHERE OrganizationId=1008 AND VehicleType=N'MOTORCYCLE');
 DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$bar8b',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
 WHILE APPLOCK_TEST(N'public',N'$bar8a',N'Exclusive',N'Session')<>0 WAITFOR DELAY '00:00:00.100';
 UPDATE dbo.MileageRateRules SET RatePerKm=3.2,EffectiveTo=NULL WHERE MileageRateRuleId=$id8;
 COMMIT; PRINT N'DBW1_R08_B=COMMIT';
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; PRINT N'DBW1_R08_B=ERROR:'+CONVERT(nvarchar(20),ERROR_NUMBER()); END CATCH;"
mk_session "$tmp_dir/r08a.sql" "
SET NOCOUNT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1008,N'A',N'MOTORCYCLE',2.8,'2026-07-01',NULL,1);
 COMMIT;
 DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$bar8a',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
 PRINT N'DBW1_R08_A=COMMIT'; WAITFOR DELAY '00:00:02';
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; PRINT N'DBW1_R08_A=ERROR:'+CONVERT(nvarchar(20),ERROR_NUMBER()); END CATCH;"
"${sqlcmd_base[@]}" -d "$main_db" < "$tmp_dir/r08b.sql" >"$tmp_dir/r08b.log" 2>&1 & p8b=$!
wait_lock_held "$main_db" "$bar8b"
"${sqlcmd_base[@]}" -d "$main_db" < "$tmp_dir/r08a.sql" >"$tmp_dir/r08a.log" 2>&1 & p8a=$!
wait "$p8a" || true; wait "$p8b" || true
o8a="$(parse_outcome "$tmp_dir/r08a.log" DBW1_R08_A)"; o8b="$(parse_outcome "$tmp_dir/r08b.log" DBW1_R08_B)"
require_commit "$o8a" "R08 A"; require_error "$o8b" "R08 B" 3960
echo "$o8a"; echo "$o8b"; echo "DBW1_RACE_08=PASS WINNER=A LOSER=B_ROLLBACK"

id9="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1009,N'Seed',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
run_serialized_pair "R09_RCSI_READ_COMMITTED" \
"UPDATE dbo.MileageRateRules SET RatePerKm=3.5,EffectiveTo=NULL WHERE MileageRateRuleId=$id9;" \
"SET LOCK_TIMEOUT 1000; DECLARE @v decimal(10,2); SELECT @v=RatePerKm FROM dbo.MileageRateRules WHERE MileageRateRuleId=$id9; IF @v<>2.50 THROW 54909,N'RCSI did not expose prior committed version',1;"
require_commit "$CASE_A" "R09 A"; require_commit "$CASE_B" "R09 B"
echo "DBW1_RACE_09=PASS WINNER=A_WRITE B=RCSI_NONBLOCKING_READ"

run_serialized_pair "R10_GLOBAL_ORG" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(NULL,N'Global',N'CAR',3.5,'2026-03-01',NULL,1);" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1010,N'Org',N'CAR',3.6,'2026-03-01',NULL,1);"
require_commit "$CASE_A" "R10 A"; require_commit "$CASE_B" "R10 B"
echo "DBW1_RACE_10=PASS WINNER=BOTH LOSER=NONE"

run_serialized_pair "R11_ORGA_ORGB" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1011,N'OrgA',N'CAR',3.5,'2026-04-01',NULL,1);" \
"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(2011,N'OrgB',N'CAR',3.6,'2026-04-01',NULL,1);"
require_commit "$CASE_A" "R11 A"; require_commit "$CASE_B" "R11 B"
echo "DBW1_RACE_11=PASS WINNER=BOTH LOSER=NONE"

sql_query "$main_db" "INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1012,N'One',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1),(1012,N'Two',N'MOTORCYCLE',2.8,'2026-07-01',NULL,1);" >/dev/null
id12="$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT MileageRateRuleId FROM dbo.MileageRateRules WHERE OrganizationId=1012 AND EffectiveFrom='2026-01-01';")"
sql_query "$main_db" "UPDATE dbo.MileageRateRules SET EffectiveTo='2099-12-31' WHERE MileageRateRuleId=$id12;" >/dev/null
[[ "$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT CONVERT(varchar(10),EffectiveTo,23) FROM dbo.MileageRateRules WHERE MileageRateRuleId=$id12;")" == "2026-06-30" ]] || fail "R12 arbitrary EffectiveTo persisted."
echo "DBW1_R12_A=COMMIT"; echo "DBW1_R12_B=N/A"; echo "DBW1_RACE_12=PASS WINNER=DB_AUTHORITY LOSER=CALLER_EFFECTIVETO"

id13="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1013,N'Protected',N'CAR',3.0,'2026-01-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
set +e
delete_log="$(sql_query "$main_db" "DELETE FROM dbo.MileageRateRules WHERE MileageRateRuleId=$id13;" 2>&1)"; delete_rc=$?
set -e
if [[ "$delete_rc" == 0 ]]; then physical_delete_escapes=$((physical_delete_escapes+1)); fail "R13 physical DELETE escaped."; fi
grep -q '53830' <<<"$delete_log" || fail "R13 failed for unexpected reason."
[[ "$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.MileageRateRules WHERE MileageRateRuleId=$id13;")" == 1 ]] || fail "R13 protected row disappeared."
echo "DBW1_R13_A=ERROR:53830"; echo "DBW1_R13_B=N/A"; echo "DBW1_RACE_13=PASS WINNER=PROTECTION LOSER=DELETE_ROLLBACK"

sql_query "$main_db" "INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(NULL,N'Multi Global',N'MOTORCYCLE',2.1,'2027-01-01',NULL,1),(1014,N'Multi Org M',N'MOTORCYCLE',2.2,'2027-01-01',NULL,1),(1014,N'Multi Org C',N'CAR',3.2,'2027-01-01',NULL,1),(2014,N'Multi Other',N'CAR',3.3,'2027-02-01',NULL,1);" >/dev/null
echo "DBW1_R14_A=COMMIT"; echo "DBW1_R14_B=N/A"; echo "DBW1_RACE_14=PASS WINNER=STATEMENT LOSER=NONE"

id15="$(sql_scalar "$main_db" "SET NOCOUNT ON; INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(1015,N'Move',N'MOTORCYCLE',2.5,'2026-05-01',NULL,1); SELECT CONVERT(int,SCOPE_IDENTITY());")"
sql_query "$main_db" "INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(2015,N'Existing',N'CAR',3.0,'2026-01-01',NULL,1); UPDATE dbo.MileageRateRules SET OrganizationId=2015,VehicleType=N'CAR',EffectiveFrom='2026-07-01',EffectiveTo=NULL WHERE MileageRateRuleId=$id15;" >/dev/null
[[ "$(sql_scalar "$main_db" "SET NOCOUNT ON; SELECT CONVERT(varchar(10),EffectiveTo,23) FROM dbo.MileageRateRules WHERE OrganizationId=2015 AND VehicleType=N'CAR' AND EffectiveFrom='2026-01-01';")" == "2026-06-30" ]] || fail "R15 new series not re-derived."
echo "DBW1_R15_A=COMMIT"; echo "DBW1_R15_B=N/A"; echo "DBW1_RACE_15=PASS WINNER=MOVE LOSER=NONE"

assert_global_invariants
sql_file "$main_db" "$verify" >/dev/null
echo "DBW1_RACE_FAMILIES=15/15"

export DB_WORK1_SQL_CONNECTION="Server=127.0.0.1,${host_port};Database=${main_db};User ID=sa;Password=${sa_password};Encrypt=False;TrustServerCertificate=True"
dotnet run --project backend/tests/FieldVisit.DBWork1.Integration.Tests/FieldVisit.DBWork1.Integration.Tests.csproj --configuration Release

mutation_pass=0
expect_verify_failure() {
  local id="$1" mutation="$2"
  local db="FieldVisitDBW1_MUT_${id}_${GITHUB_RUN_ID:-local}_$$"
  create_database "$db"
  sql_query "$db" "$mutation" >/dev/null
  set +e
  local out
  out="$(sql_file "$db" "$verify" 2>&1)"; local rc=$?
  set -e
  if [[ "$rc" == 0 ]]; then echo "$out" >&2; fail "Verify mutation $id was not detected."; fi
  mutation_pass=$((mutation_pass+1))
  echo "DBW1_VERIFY_MUTATION_${id}=PASS"
}
expect_verify_failure A "
DECLARE @d nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
DECLARE @m nvarchar(max)=REPLACE(@d,N'AFTER INSERT, UPDATE, DELETE',N'AFTER INSERT, UPDATE');
IF @m=@d THROW 54920,N'mutation A did not alter trigger',1;
EXEC sys.sp_executesql @m;"
expect_verify_failure B "
DECLARE @d nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
DECLARE @m nvarchar(max)=REPLACE(@d,N'@LockMode=N''Exclusive''',N'@LockMode=N''Shared''');
IF @m=@d THROW 54921,N'mutation B did not alter trigger',1;
EXEC sys.sp_executesql @m;"
expect_verify_failure C "
DECLARE @d nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
DECLARE @m nvarchar(max)=REPLACE(@d,N'WITH (UPDLOCK,HOLDLOCK)',N'WITH (HOLDLOCK)');
IF @m=@d THROW 54922,N'mutation C did not alter trigger',1;
EXEC sys.sp_executesql @m;"
expect_verify_failure D "
INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive) VALUES(9001,N'One',N'MOTORCYCLE',2.5,'2026-01-01',NULL,1),(9001,N'Two',N'MOTORCYCLE',2.6,'2026-07-01',NULL,1);
DISABLE TRIGGER dbo.TR_MileageRateRules_ProtectSeries ON dbo.MileageRateRules;
UPDATE dbo.MileageRateRules SET EffectiveTo='2026-05-31' WHERE OrganizationId=9001 AND EffectiveFrom='2026-01-01';
ENABLE TRIGGER dbo.TR_MileageRateRules_ProtectSeries ON dbo.MileageRateRules;"
expect_verify_failure E "
DECLARE @d nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
DECLARE @m nvarchar(max)=REPLACE(@d,N'(r.OrganizationId=a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))',N'ISNULL(r.OrganizationId,-1)=ISNULL(a.OrganizationId,-1)');
IF @m=@d THROW 54923,N'mutation E did not alter trigger',1;
EXEC sys.sp_executesql @m;"
[[ "$mutation_pass" == 5 ]] || fail "Verify mutation resistance incomplete."
echo "DBW1_VERIFY_MUTATION_RESISTANCE=5/5"
echo "DBW1_SQL_CONCURRENCY=15/15"
