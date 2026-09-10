#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${DA1B_SQL_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
SA_PASSWORD="${DA1B_SQL_PASSWORD:-DA1b_Strict_2026!Sql}"
DB_NAME="FieldVisitDA1b"
CONTAINER="da1b-location-governance-${GITHUB_RUN_ID:-local}-$$"

command -v docker >/dev/null 2>&1 || { echo "D-A1b SQL integration requires Docker; refusing silent skip." >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || { echo "D-A1b SQL integration requires dotnet; refusing silent skip." >&2; exit 1; }
cleanup(){ docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT INT TERM

echo "DA1B_SQL_IMAGE=$IMAGE"
docker run -d --rm --name "$CONTAINER" -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$SA_PASSWORD" -p 127.0.0.1::1433 "$IMAGE" >/dev/null

SQLCMD=""
for _ in $(seq 1 90); do
  if docker exec "$CONTAINER" test -x /opt/mssql-tools18/bin/sqlcmd >/dev/null 2>&1; then SQLCMD=/opt/mssql-tools18/bin/sqlcmd; break; fi
  if docker exec "$CONTAINER" test -x /opt/mssql-tools/bin/sqlcmd >/dev/null 2>&1; then SQLCMD=/opt/mssql-tools/bin/sqlcmd; break; fi
  sleep 1
done
[[ -n "$SQLCMD" ]] || { echo "sqlcmd unavailable in D-A1b SQL image." >&2; exit 1; }
for _ in $(seq 1 90); do
  if docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -I -Q "SELECT 1" >/dev/null 2>&1; then break; fi
  sleep 1
done
docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -I -b -Q "SELECT 1" >/dev/null

docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -I -b -Q "CREATE DATABASE [$DB_NAME]; ALTER DATABASE [$DB_NAME] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [$DB_NAME] SET ALLOW_SNAPSHOT_ISOLATION ON;"
docker cp "$ROOT/database/development/v1.8.0/compat/session-options.sql" "$CONTAINER:/tmp/session-options.sql"
docker cp "$ROOT/scripts/fixtures/da0b-prereq.sql" "$CONTAINER:/tmp/da0b-prereq.sql"
docker cp "$ROOT/database/migrations/1800_004_location_governance/Up.sql" "$CONTAINER:/tmp/1800_004.sql"
docker cp "$ROOT/scripts/fixtures/da1b-runtime-prereq.sql" "$CONTAINER:/tmp/da1b-runtime-prereq.sql"
for target in da0b-prereq 1800_004 da1b-runtime-prereq; do
  docker exec "$CONTAINER" sh -c "cat /tmp/session-options.sql /tmp/${target}.sql > /tmp/${target}.with-session-options.sql"
  docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -I -b -d "$DB_NAME" -i "/tmp/${target}.with-session-options.sql"
done

docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -I -b -d "$DB_NAME" -Q "SELECT 'DA1B_SQL_VERSION=' + CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) + '|' + CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(100)) + '|' + CAST(SERVERPROPERTY('Edition') AS nvarchar(100)); SELECT 'DA1B_READ_COMMITTED_SNAPSHOT=' + CASE WHEN is_read_committed_snapshot_on=1 THEN 'ON' ELSE 'OFF' END + '|DA1B_ALLOW_SNAPSHOT_ISOLATION=' + CASE WHEN snapshot_isolation_state=1 THEN 'ON' ELSE 'OFF' END FROM sys.databases WHERE name=DB_NAME();" -h -1 -W

PORT_LINE="$(docker port "$CONTAINER" 1433/tcp | head -n1)"
PORT="${PORT_LINE##*:}"
[[ "$PORT" =~ ^[0-9]+$ ]] || { echo "Unable to resolve disposable SQL Server host port." >&2; exit 1; }
export DA1B_CONNECTION_STRING="Server=127.0.0.1,$PORT;Database=$DB_NAME;User Id=sa;Password=$SA_PASSWORD;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15"

echo "DA1B_REPOSITORY_INTEGRATION=START"
dotnet run --project "$ROOT/backend/tests/FieldVisit.DA1b.Integration.Tests/FieldVisit.DA1b.Integration.Tests.csproj" --configuration Release
echo "D-A1b real SQL Server repository integration regression passed."
