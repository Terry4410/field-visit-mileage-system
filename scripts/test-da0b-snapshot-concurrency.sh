#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

image="${DA0B_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
container_name="fieldvisit-da0b-${GITHUB_RUN_ID:-local}-$$"
sa_password='Da0b!SnapshotSafe2026_TestOnly'
tmp_dir="$(mktemp -d)"
sqlcmd_path=''

cleanup() {
  docker rm -f "$container_name" >/dev/null 2>&1 || true
  rm -rf "$tmp_dir"
}
trap cleanup EXIT INT TERM

command -v docker >/dev/null 2>&1 || {
  echo 'D-A0b real DB regression requires Docker.' >&2
  exit 1
}

echo "DA0B_SQL_IMAGE=$image"
docker run -d --name "$container_name" \
  -e ACCEPT_EULA=Y \
  -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$sa_password" \
  "$image" >/dev/null

for candidate in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
  if docker exec "$container_name" test -x "$candidate" >/dev/null 2>&1; then
    sqlcmd_path="$candidate"
    break
  fi
done

if [[ -z "$sqlcmd_path" ]]; then
  echo 'sqlcmd was not found in the disposable SQL Server image.' >&2
  docker logs "$container_name" >&2 || true
  exit 1
fi

sqlcmd_base=(docker exec -i "$container_name" "$sqlcmd_path" -S localhost -U sa -P "$sa_password" -C -b -r1)

ready=0
for _ in $(seq 1 90); do
  if "${sqlcmd_base[@]}" -d master -Q 'SET NOCOUNT ON; SELECT 1;' >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done
if [[ "$ready" != 1 ]]; then
  echo 'Disposable SQL Server did not become ready.' >&2
  docker logs "$container_name" >&2 || true
  exit 1
fi

sql_file() {
  local database="$1"
  local file="$2"
  "${sqlcmd_base[@]}" -d "$database" < "$file"
}

sql_with_session_options_and_file() {
  local database="$1"
  local file="$2"
  {
    cat database/development/v1.8.0/compat/session-options.sql
    cat "$file"
  } | "${sqlcmd_base[@]}" -d "$database"
}

sql_query() {
  local database="$1"
  local query="$2"
  "${sqlcmd_base[@]}" -d "$database" -Q "$query"
}

sql_scalar() {
  local database="$1"
  local query="$2"
  docker exec -i "$container_name" "$sqlcmd_path" \
    -S localhost -U sa -P "$sa_password" -C -b -r1 -h -1 -W \
    -d "$database" -Q "$query"
}

quote_resource() {
  printf '%s' "$1" | sed "s/'/''/g"
}

wait_lock_held() {
  local database="$1"
  local resource="$2"
  local escaped result
  escaped="$(quote_resource "$resource")"
  for _ in $(seq 1 150); do
    result="$(sql_scalar "$database" "SET NOCOUNT ON; SELECT APPLOCK_TEST(N'public', N'$escaped', N'Exclusive', N'Session');" 2>/dev/null \
      | tr -d '\r' | grep -E '^[[:space:]]*[01][[:space:]]*$' | tail -n1 | tr -d '[:space:]' || true)"
    if [[ "$result" == 0 ]]; then
      return 0
    fi
    sleep 0.1
  done
  echo "Timed out waiting for deterministic barrier lock: $resource" >&2
  return 1
}

create_database() {
  local database="$1"
  sql_query master "
IF DB_ID(N'$database') IS NOT NULL
BEGIN
    ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$database];
END;" >/dev/null
  sql_query master "CREATE DATABASE [$database];" >/dev/null
  sql_query master "ALTER DATABASE [$database] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;" >/dev/null
  sql_query master "ALTER DATABASE [$database] SET ALLOW_SNAPSHOT_ISOLATION ON;" >/dev/null

  sql_file "$database" scripts/fixtures/da0b-prereq.sql >/dev/null
  sql_with_session_options_and_file "$database" database/migrations/1800_004_location_governance/Up.sql >/dev/null
  sql_file "$database" database/migrations/1800_004_location_governance/Verify.sql >/dev/null

  sql_query "$database" "
IF NOT EXISTS
(
    SELECT 1 FROM sys.databases
    WHERE name = DB_NAME()
      AND is_read_committed_snapshot_on = 1
      AND snapshot_isolation_state_desc = N'ON'
)
    THROW 53901, N'D-A0b disposable DB isolation options are not both enabled.', 1;" >/dev/null
}

assert_zero_invariant_violations() {
  local database="$1"
  local case_id="$2"
  sql_query "$database" "
DECLARE @BusinessToday date = CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));
IF EXISTS
(
    SELECT 1
    FROM dbo.Locations l
    JOIN dbo.DeploymentSiteLocationAssignments a ON a.LocationId = l.LocationId
    WHERE l.IsActive = 0
      AND (a.EffectiveTo IS NULL OR a.EffectiveTo >= @BusinessToday)
)
    THROW 53902, N'$case_id final invariant violation detected.', 1;" >/dev/null
}

setup_location_site() {
  local database="$1" location_id="$2" site_id="$3" case_id="$4"
  sql_query "$database" "
INSERT dbo.Locations(LocationId, OrganizationId, LocationCode, LocationName, Address, IsActive)
VALUES($location_id, 1, N'L$location_id', N'$case_id Location', N'Test', 1);
INSERT dbo.DeploymentSites(DeploymentSiteId) VALUES($site_id);" >/dev/null
}

setup_historical_assignment() {
  local database="$1" location_id="$2" site_id="$3"
  sql_query "$database" "
DECLARE @BusinessToday date = CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));
INSERT dbo.DeploymentSiteLocationAssignments
    (DeploymentSiteId, LocationId, EffectiveFrom, EffectiveTo, ChangeReason)
VALUES
    ($site_id, $location_id, DATEADD(DAY, -10, @BusinessToday), DATEADD(DAY, -1, @BusinessToday), N'D-A0b historical setup');" >/dev/null
}

run_assignment_first() {
  local database="$1" case_id="$2" isolation="$3" effective_to_sql="$4" location_id="$5" site_id="$6"
  local ready_resource="DA0B.$case_id.Ready"
  local release_resource="DA0B.$case_id.Release"
  local winner_sql="$tmp_dir/${case_id}-winner.sql"
  local loser_sql="$tmp_dir/${case_id}-loser.sql"
  local winner_log="$tmp_dir/${case_id}-winner.log"
  local loser_log="$tmp_dir/${case_id}-loser.log"

  setup_location_site "$database" "$location_id" "$site_id" "$case_id"

  cat > "$winner_sql" <<SQL
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @GateResult int;
EXEC @GateResult = sys.sp_getapplock
    @Resource=N'$release_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @GateResult < 0 THROW 53910, N'$case_id winner could not acquire release gate.', 1;
DECLARE @WaitStarted datetime2(3)=SYSUTCDATETIME();
WHILE APPLOCK_TEST(N'public', N'$ready_resource', N'Exclusive', N'Session') <> 0
BEGIN
    IF DATEDIFF(MILLISECOND, @WaitStarted, SYSUTCDATETIME()) > 30000
        THROW 53911, N'$case_id winner timed out waiting for ready gate.', 1;
    WAITFOR DELAY '00:00:00.100';
END;
DECLARE @BusinessToday date = CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));
BEGIN TRANSACTION;
INSERT dbo.DeploymentSiteLocationAssignments
    (DeploymentSiteId, LocationId, EffectiveFrom, EffectiveTo, ChangeReason)
VALUES
    ($site_id, $location_id, DATEADD(DAY, -10, @BusinessToday), $effective_to_sql, N'$case_id winner assignment');
COMMIT TRANSACTION;
PRINT N'DA0B_WINNER_COMMIT';
EXEC sys.sp_releaseapplock @Resource=N'$release_resource', @LockOwner=N'Session';
SQL

  cat > "$loser_sql" <<SQL
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL $isolation;
BEGIN TRANSACTION;
DECLARE @SnapshotActive bit = (SELECT IsActive FROM dbo.Locations WHERE LocationId=$location_id);
DECLARE @SnapshotAssignments int = (SELECT COUNT(*) FROM dbo.DeploymentSiteLocationAssignments WHERE LocationId=$location_id);
IF @SnapshotActive <> 1 OR @SnapshotAssignments <> 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53912, N'$case_id loser did not establish the expected pre-winner snapshot.', 1;
END;
DECLARE @ReadyResult int;
EXEC @ReadyResult = sys.sp_getapplock
    @Resource=N'$ready_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @ReadyResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53913, N'$case_id loser could not acquire ready gate.', 1;
END;
DECLARE @ReleaseResult int;
EXEC @ReleaseResult = sys.sp_getapplock
    @Resource=N'$release_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @ReleaseResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53914, N'$case_id loser could not pass release gate.', 1;
END;
BEGIN TRY
    UPDATE dbo.Locations SET IsActive=0 WHERE LocationId=$location_id;
    COMMIT TRANSACTION;
    PRINT N'DA0B_LOSER_COMMIT';
END TRY
BEGIN CATCH
    DECLARE @ErrorNumber int=ERROR_NUMBER();
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'DA0B_LOSER_ROLLBACK ERROR=', @ErrorNumber);
END CATCH;
EXEC sys.sp_releaseapplock @Resource=N'$ready_resource', @LockOwner=N'Session';
EXEC sys.sp_releaseapplock @Resource=N'$release_resource', @LockOwner=N'Session';
SQL

  "${sqlcmd_base[@]}" -d "$database" < "$winner_sql" >"$winner_log" 2>&1 &
  local winner_pid=$!
  wait_lock_held "$database" "$release_resource" || { kill "$winner_pid" 2>/dev/null || true; return 1; }
  "${sqlcmd_base[@]}" -d "$database" < "$loser_sql" >"$loser_log" 2>&1 &
  local loser_pid=$!

  local winner_status=0 loser_status=0
  wait "$winner_pid" || winner_status=$?
  wait "$loser_pid" || loser_status=$?
  if [[ "$winner_status" != 0 || "$loser_status" != 0 ]]; then
    cat "$winner_log" >&2
    cat "$loser_log" >&2
    return 1
  fi
  grep -Fq 'DA0B_WINNER_COMMIT' "$winner_log" || { cat "$winner_log" >&2; return 1; }
  grep -Fq 'DA0B_LOSER_ROLLBACK' "$loser_log" || { cat "$loser_log" >&2; return 1; }
  ! grep -Fq 'DA0B_LOSER_COMMIT' "$loser_log" || { cat "$loser_log" >&2; return 1; }

  sql_query "$database" "
IF (SELECT IsActive FROM dbo.Locations WHERE LocationId=$location_id) <> 1
    THROW 53915, N'$case_id loser Location mutation was not fully rolled back.', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.DeploymentSiteLocationAssignments WHERE LocationId=$location_id AND DeploymentSiteId=$site_id)
    THROW 53916, N'$case_id winner assignment did not remain committed.', 1;" >/dev/null
  assert_zero_invariant_violations "$database" "$case_id"
  echo "$case_id=PASS"
}

run_location_first_insert() {
  local database="$1" case_id="$2" isolation="$3" effective_to_sql="$4" location_id="$5" site_id="$6"
  local ready_resource="DA0B.$case_id.Ready"
  local release_resource="DA0B.$case_id.Release"
  local winner_sql="$tmp_dir/${case_id}-winner.sql"
  local loser_sql="$tmp_dir/${case_id}-loser.sql"
  local winner_log="$tmp_dir/${case_id}-winner.log"
  local loser_log="$tmp_dir/${case_id}-loser.log"

  setup_location_site "$database" "$location_id" "$site_id" "$case_id"

  cat > "$winner_sql" <<SQL
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @GateResult int;
EXEC @GateResult = sys.sp_getapplock
    @Resource=N'$release_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @GateResult < 0 THROW 53920, N'$case_id winner could not acquire release gate.', 1;
DECLARE @WaitStarted datetime2(3)=SYSUTCDATETIME();
WHILE APPLOCK_TEST(N'public', N'$ready_resource', N'Exclusive', N'Session') <> 0
BEGIN
    IF DATEDIFF(MILLISECOND, @WaitStarted, SYSUTCDATETIME()) > 30000
        THROW 53921, N'$case_id winner timed out waiting for ready gate.', 1;
    WAITFOR DELAY '00:00:00.100';
END;
BEGIN TRANSACTION;
UPDATE dbo.Locations SET IsActive=0 WHERE LocationId=$location_id;
COMMIT TRANSACTION;
PRINT N'DA0B_WINNER_COMMIT';
EXEC sys.sp_releaseapplock @Resource=N'$release_resource', @LockOwner=N'Session';
SQL

  cat > "$loser_sql" <<SQL
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL $isolation;
BEGIN TRANSACTION;
DECLARE @SnapshotActive bit = (SELECT IsActive FROM dbo.Locations WHERE LocationId=$location_id);
IF @SnapshotActive <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53922, N'$case_id loser did not establish the expected active Location snapshot.', 1;
END;
DECLARE @ReadyResult int;
EXEC @ReadyResult = sys.sp_getapplock
    @Resource=N'$ready_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @ReadyResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53923, N'$case_id loser could not acquire ready gate.', 1;
END;
DECLARE @ReleaseResult int;
EXEC @ReleaseResult = sys.sp_getapplock
    @Resource=N'$release_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @ReleaseResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53924, N'$case_id loser could not pass release gate.', 1;
END;
DECLARE @BusinessToday date = CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));
BEGIN TRY
    INSERT dbo.DeploymentSiteLocationAssignments
        (DeploymentSiteId, LocationId, EffectiveFrom, EffectiveTo, ChangeReason)
    VALUES
        ($site_id, $location_id, DATEADD(DAY, -10, @BusinessToday), $effective_to_sql, N'$case_id loser assignment');
    COMMIT TRANSACTION;
    PRINT N'DA0B_LOSER_COMMIT';
END TRY
BEGIN CATCH
    DECLARE @ErrorNumber int=ERROR_NUMBER();
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'DA0B_LOSER_ROLLBACK ERROR=', @ErrorNumber);
END CATCH;
EXEC sys.sp_releaseapplock @Resource=N'$ready_resource', @LockOwner=N'Session';
EXEC sys.sp_releaseapplock @Resource=N'$release_resource', @LockOwner=N'Session';
SQL

  "${sqlcmd_base[@]}" -d "$database" < "$winner_sql" >"$winner_log" 2>&1 &
  local winner_pid=$!
  wait_lock_held "$database" "$release_resource" || { kill "$winner_pid" 2>/dev/null || true; return 1; }
  "${sqlcmd_base[@]}" -d "$database" < "$loser_sql" >"$loser_log" 2>&1 &
  local loser_pid=$!

  local winner_status=0 loser_status=0
  wait "$winner_pid" || winner_status=$?
  wait "$loser_pid" || loser_status=$?
  if [[ "$winner_status" != 0 || "$loser_status" != 0 ]]; then
    cat "$winner_log" >&2
    cat "$loser_log" >&2
    return 1
  fi
  grep -Fq 'DA0B_WINNER_COMMIT' "$winner_log" || { cat "$winner_log" >&2; return 1; }
  grep -Fq 'DA0B_LOSER_ROLLBACK' "$loser_log" || { cat "$loser_log" >&2; return 1; }
  ! grep -Fq 'DA0B_LOSER_COMMIT' "$loser_log" || { cat "$loser_log" >&2; return 1; }

  sql_query "$database" "
IF (SELECT IsActive FROM dbo.Locations WHERE LocationId=$location_id) <> 0
    THROW 53925, N'$case_id winner Location inactivation did not remain committed.', 1;
IF EXISTS(SELECT 1 FROM dbo.DeploymentSiteLocationAssignments WHERE LocationId=$location_id AND DeploymentSiteId=$site_id)
    THROW 53926, N'$case_id loser assignment insert was not fully rolled back.', 1;" >/dev/null
  assert_zero_invariant_violations "$database" "$case_id"
  echo "$case_id=PASS"
}

run_location_first_update() {
  local database="$1" case_id="$2" isolation="$3" effective_to_sql="$4" location_id="$5" site_id="$6"
  local ready_resource="DA0B.$case_id.Ready"
  local release_resource="DA0B.$case_id.Release"
  local winner_sql="$tmp_dir/${case_id}-winner.sql"
  local loser_sql="$tmp_dir/${case_id}-loser.sql"
  local winner_log="$tmp_dir/${case_id}-winner.log"
  local loser_log="$tmp_dir/${case_id}-loser.log"

  setup_location_site "$database" "$location_id" "$site_id" "$case_id"
  setup_historical_assignment "$database" "$location_id" "$site_id"

  cat > "$winner_sql" <<SQL
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @GateResult int;
EXEC @GateResult = sys.sp_getapplock
    @Resource=N'$release_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @GateResult < 0 THROW 53930, N'$case_id winner could not acquire release gate.', 1;
DECLARE @WaitStarted datetime2(3)=SYSUTCDATETIME();
WHILE APPLOCK_TEST(N'public', N'$ready_resource', N'Exclusive', N'Session') <> 0
BEGIN
    IF DATEDIFF(MILLISECOND, @WaitStarted, SYSUTCDATETIME()) > 30000
        THROW 53931, N'$case_id winner timed out waiting for ready gate.', 1;
    WAITFOR DELAY '00:00:00.100';
END;
BEGIN TRANSACTION;
UPDATE dbo.Locations SET IsActive=0 WHERE LocationId=$location_id;
COMMIT TRANSACTION;
PRINT N'DA0B_WINNER_COMMIT';
EXEC sys.sp_releaseapplock @Resource=N'$release_resource', @LockOwner=N'Session';
SQL

  cat > "$loser_sql" <<SQL
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL $isolation;
BEGIN TRANSACTION;
DECLARE @BusinessToday date = CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));
DECLARE @SnapshotActive bit = (SELECT IsActive FROM dbo.Locations WHERE LocationId=$location_id);
DECLARE @SnapshotHistorical date = (SELECT EffectiveTo FROM dbo.DeploymentSiteLocationAssignments WHERE LocationId=$location_id AND DeploymentSiteId=$site_id);
IF @SnapshotActive <> 1 OR @SnapshotHistorical >= @BusinessToday
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53932, N'$case_id loser did not establish expected historical pre-winner snapshot.', 1;
END;
DECLARE @ReadyResult int;
EXEC @ReadyResult = sys.sp_getapplock
    @Resource=N'$ready_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @ReadyResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53933, N'$case_id loser could not acquire ready gate.', 1;
END;
DECLARE @ReleaseResult int;
EXEC @ReleaseResult = sys.sp_getapplock
    @Resource=N'$release_resource', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
IF @ReleaseResult < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 53934, N'$case_id loser could not pass release gate.', 1;
END;
BEGIN TRY
    UPDATE dbo.DeploymentSiteLocationAssignments
    SET EffectiveTo=$effective_to_sql, ChangeReason=N'$case_id loser update'
    WHERE LocationId=$location_id AND DeploymentSiteId=$site_id;
    COMMIT TRANSACTION;
    PRINT N'DA0B_LOSER_COMMIT';
END TRY
BEGIN CATCH
    DECLARE @ErrorNumber int=ERROR_NUMBER();
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'DA0B_LOSER_ROLLBACK ERROR=', @ErrorNumber);
END CATCH;
EXEC sys.sp_releaseapplock @Resource=N'$ready_resource', @LockOwner=N'Session';
EXEC sys.sp_releaseapplock @Resource=N'$release_resource', @LockOwner=N'Session';
SQL

  "${sqlcmd_base[@]}" -d "$database" < "$winner_sql" >"$winner_log" 2>&1 &
  local winner_pid=$!
  wait_lock_held "$database" "$release_resource" || { kill "$winner_pid" 2>/dev/null || true; return 1; }
  "${sqlcmd_base[@]}" -d "$database" < "$loser_sql" >"$loser_log" 2>&1 &
  local loser_pid=$!

  local winner_status=0 loser_status=0
  wait "$winner_pid" || winner_status=$?
  wait "$loser_pid" || loser_status=$?
  if [[ "$winner_status" != 0 || "$loser_status" != 0 ]]; then
    cat "$winner_log" >&2
    cat "$loser_log" >&2
    return 1
  fi
  grep -Fq 'DA0B_WINNER_COMMIT' "$winner_log" || { cat "$winner_log" >&2; return 1; }
  grep -Fq 'DA0B_LOSER_ROLLBACK' "$loser_log" || { cat "$loser_log" >&2; return 1; }
  ! grep -Fq 'DA0B_LOSER_COMMIT' "$loser_log" || { cat "$loser_log" >&2; return 1; }

  sql_query "$database" "
DECLARE @BusinessToday date = CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));
IF (SELECT IsActive FROM dbo.Locations WHERE LocationId=$location_id) <> 0
    THROW 53935, N'$case_id winner Location inactivation did not remain committed.', 1;
IF NOT EXISTS
(
    SELECT 1 FROM dbo.DeploymentSiteLocationAssignments
    WHERE LocationId=$location_id AND DeploymentSiteId=$site_id AND EffectiveTo < @BusinessToday
)
    THROW 53936, N'$case_id loser assignment update was not fully rolled back.', 1;" >/dev/null
  assert_zero_invariant_violations "$database" "$case_id"
  echo "$case_id=PASS"
}

verify_rejects_removed_hint() {
  local case_suffix="$1" trigger_name="$2" replacement="$3"
  local database="DA0BVerify${case_suffix}"
  local log="$tmp_dir/verify-${case_suffix}.log"
  create_database "$database"

  sql_query "$database" "
DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.$trigger_name'));
IF @Definition IS NULL THROW 53940, N'SC-019 could not read trigger definition.', 1;
SET @Definition=REPLACE(@Definition, N'CREATE TRIGGER', N'ALTER TRIGGER');
SET @Definition=REPLACE(@Definition, N'WITH (UPDLOCK, HOLDLOCK)', N'$replacement');
IF @Definition LIKE N'%WITH (UPDLOCK, HOLDLOCK)%'
    THROW 53941, N'SC-019 mutation failed to remove the selected locking protection.', 1;
EXEC sys.sp_executesql @Definition;" >/dev/null

  local status=0
  sql_file "$database" database/migrations/1800_004_location_governance/Verify.sql >"$log" 2>&1 || status=$?
  if [[ "$status" == 0 ]] || ! grep -Fq '53724' "$log"; then
    echo "SC-019 Verify did not fail closed for $trigger_name -> $replacement" >&2
    cat "$log" >&2
    return 1
  fi
  sql_query master "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];" >/dev/null
}

# SC-018: source/static fingerprints must carry both serialization layers in canonical and development SQL.
for file in \
  database/migrations/1800_004_location_governance/Up.sql \
  database/development/v1.8.0/compat/1800_004.dev.sql; do
  normalized="$(tr -d '[:space:]' < "$file")"
  [[ "$normalized" == *"JOINdbo.DeploymentSiteLocationAssignmentsaWITH(UPDLOCK,HOLDLOCK)ONa.LocationId=i.LocationId"* ]]
  [[ "$normalized" == *"JOINdbo.LocationslWITH(UPDLOCK,HOLDLOCK)ONl.LocationId=i.LocationId"* ]]
  [[ "$normalized" == *"FieldVisit.LocationDeploymentAssignmentActiveInvariant"* ]]
  [[ "$normalized" == *"@LockMode=N''Exclusive''"* ]]
  [[ "$normalized" == *"@LockOwner=N''Transaction''"* ]]
  [[ "$normalized" == *"@LockTimeout=10000"* ]]
done
grep -Fq "joindbo.deploymentsitelocationassignmentsawith(updlock,holdlock)" database/migrations/1800_004_location_governance/Verify.sql
grep -Fq "joindbo.locationslwith(updlock,holdlock)" database/migrations/1800_004_location_governance/Verify.sql
echo 'DA0B-SC-018=PASS'

server_version="$(sql_scalar master "SET NOCOUNT ON; SELECT CONCAT(CONVERT(varchar(128),SERVERPROPERTY('ProductVersion')), N'|', CONVERT(varchar(128),SERVERPROPERTY('ProductLevel')), N'|', CONVERT(varchar(128),SERVERPROPERTY('Edition')));" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -n1)"
echo "DA0B_SQL_VERSION=$server_version"

race_db='FieldVisitDA0BRace'
create_database "$race_db"
options="$(sql_scalar master "SET NOCOUNT ON; SELECT CONCAT(CASE WHEN is_read_committed_snapshot_on=1 THEN N'ON' ELSE N'OFF' END, N'|', snapshot_isolation_state_desc) FROM sys.databases WHERE name=N'$race_db';" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -n1)"
[[ "$options" == 'ON|ON' ]] || { echo "Unexpected D-A0b DB options: $options" >&2; exit 1; }
echo 'DA0B_READ_COMMITTED_SNAPSHOT=ON'
echo 'DA0B_ALLOW_SNAPSHOT_ISOLATION=ON'
echo 'DA0B_INDEPENDENT_SQL_SESSIONS=YES'
echo 'DA0B_DETERMINISTIC_BARRIERS=YES'

# Explicit SNAPSHOT root-cause closure and frozen current/future boundaries.
run_assignment_first "$race_db" DA0B-SC-001 SNAPSHOT 'DATEADD(DAY, 2, @BusinessToday)' 1001 2001
run_assignment_first "$race_db" DA0B-SC-002 SNAPSHOT '@BusinessToday' 1002 2002
run_assignment_first "$race_db" DA0B-SC-003 SNAPSHOT 'DATEADD(DAY, 1, @BusinessToday)' 1003 2003
run_assignment_first "$race_db" DA0B-SC-004 SNAPSHOT 'NULL' 1004 2004

run_location_first_insert "$race_db" DA0B-SC-006 SNAPSHOT '@BusinessToday' 1006 2006
run_location_first_update "$race_db" DA0B-SC-007 SNAPSHOT '@BusinessToday' 1007 2007
run_location_first_update "$race_db" DA0B-SC-008 SNAPSHOT 'DATEADD(DAY, 1, @BusinessToday)' 1008 2008
run_location_first_update "$race_db" DA0B-SC-009 SNAPSHOT 'NULL' 1009 2009

run_location_first_insert "$race_db" DA0B-SC-012 SNAPSHOT 'DATEADD(DAY, 2, @BusinessToday)' 1012 2012
run_assignment_first "$race_db" DA0B-SC-014 SNAPSHOT 'NULL' 1014 2014

# SC-015 independently exercises READ COMMITTED while RCSI is ON in both mutation directions.
run_assignment_first "$race_db" DA0B-SC-015-RCSI-A 'READ COMMITTED' '@BusinessToday' 1015 2015
run_location_first_insert "$race_db" DA0B-SC-015-RCSI-B 'READ COMMITTED' '@BusinessToday' 1016 2016
echo 'DA0B-SC-015=PASS'

# Every race helper asserts loser rollback + winner preservation; this is the aggregate SC-016 proof.
echo 'DA0B-SC-016=PASS'

# SC-019: actual Verify.sql must reject removal of either lock hint from either invariant read.
verify_rejects_removed_hint LUp TR_Locations_ProtectCurrentDeploymentSiteLocations 'WITH (HOLDLOCK)'
verify_rejects_removed_hint LHold TR_Locations_ProtectCurrentDeploymentSiteLocations 'WITH (UPDLOCK)'
verify_rejects_removed_hint AUp TR_DeploymentSiteLocationAssignments_ProtectActiveLocation 'WITH (HOLDLOCK)'
verify_rejects_removed_hint AHold TR_DeploymentSiteLocationAssignments_ProtectActiveLocation 'WITH (UPDLOCK)'
echo 'DA0B-SC-019=PASS'

# Direct sqlcmd sessions only; no application/API participates in the invariant proof.
echo 'DA0B-SC-020=PASS'

final_violations="$(sql_scalar "$race_db" "
SET NOCOUNT ON;
DECLARE @BusinessToday date=CONVERT(date,DATEADD(HOUR,8,SYSUTCDATETIME()));
SELECT COUNT_BIG(*)
FROM dbo.Locations l
JOIN dbo.DeploymentSiteLocationAssignments a ON a.LocationId=l.LocationId
WHERE l.IsActive=0 AND (a.EffectiveTo IS NULL OR a.EffectiveTo>=@BusinessToday);" \
  | tr -d '\r' | grep -E '^[[:space:]]*[0-9]+[[:space:]]*$' | tail -n1 | tr -d '[:space:]')"
[[ "$final_violations" == 0 ]] || { echo "D-A0b final invariant violations=$final_violations" >&2; exit 1; }
echo 'DA0B_FINAL_INVARIANT_VIOLATIONS=0'
echo 'D-A0b real SQL Server SNAPSHOT/RCSI concurrency regression passed.'
