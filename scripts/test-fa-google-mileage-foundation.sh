#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
authoritative_up="database/migrations/1800_007_mileage_google_governance/Up.sql"
node scripts/scan-sql-add-column-order.mjs "$authoritative_up"
echo "FA_1800_007_AUTHORITATIVE_ADD_COLUMN_SCAN=PASS"
image="${FA_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
container_name="fieldvisit-fa-${GITHUB_RUN_ID:-local}-$$"
sa_password='Fa!GoogleMileage2026_TestOnly'
database_name="FieldVisitFA_${GITHUB_RUN_ID:-local}_$$"
sqlcmd_path=''
cleanup(){ docker rm -f "$container_name" >/dev/null 2>&1 || true; }
trap cleanup EXIT INT TERM
fail(){ echo "FA-REAL-SQL-FAIL: $*" >&2; exit 1; }
command -v docker >/dev/null 2>&1 || fail "Docker is required."
command -v dotnet >/dev/null 2>&1 || fail ".NET SDK is required."
docker run -d --name "$container_name" -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$sa_password" -p 127.0.0.1::1433 "$image" >/dev/null
for _ in $(seq 1 90); do
  for candidate in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
    if docker exec "$container_name" test -x "$candidate" >/dev/null 2>&1; then sqlcmd_path="$candidate"; break 2; fi
  done
  sleep 1
done
[[ -n "$sqlcmd_path" ]] || fail "sqlcmd unavailable."
sqlcmd=(docker exec -i "$container_name" "$sqlcmd_path" -S localhost -U sa -P "$sa_password" -C -I -b -r1)
for _ in $(seq 1 90); do "${sqlcmd[@]}" -d master -Q 'SET NOCOUNT ON; SELECT 1;' >/dev/null 2>&1 && break; sleep 1; done
"${sqlcmd[@]}" -d master -Q "CREATE DATABASE [$database_name]; ALTER DATABASE [$database_name] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [$database_name] SET ALLOW_SNAPSHOT_ISOLATION ON;" >/dev/null
port="$(docker port "$container_name" 1433/tcp | head -n1 | awk -F: '{print $NF}')"
[[ "$port" =~ ^[0-9]+$ ]] || fail "mapped port missing"
FA_SQL_CONNECTION="Server=127.0.0.1,$port;Database=$database_name;User Id=sa;Password=$sa_password;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False" \
  dotnet run --project backend/tests/FieldVisit.FA.Integration.Tests/FieldVisit.FA.Integration.Tests.csproj --configuration Release
