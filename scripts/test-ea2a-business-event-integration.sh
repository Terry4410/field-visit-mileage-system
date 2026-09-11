#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

image="${EA2A_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
container_name="fieldvisit-ea2a-${GITHUB_RUN_ID:-local}-$$"
sa_password='Ea2a!BusinessEvent2026_TestOnly'
database_name="FieldVisitEA2A_${GITHUB_RUN_ID:-local}_$$"
sqlcmd_path=''

cleanup() {
  docker rm -f "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
fail() { echo "EA2A-REAL-SQL-FAIL: $*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "Docker is required."
echo "EA2A_SQL_IMAGE=$image"
docker run -d --name "$container_name" \
  -e ACCEPT_EULA=Y \
  -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$sa_password" \
  -p 127.0.0.1::1433 \
  "$image" >/dev/null

ready=0
for _ in $(seq 1 90); do
  if [[ -z "$sqlcmd_path" ]]; then
    for candidate in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
      if docker exec "$container_name" test -x "$candidate" >/dev/null 2>&1; then
        sqlcmd_path="$candidate"
        break
      fi
    done
  fi
  if [[ -n "$sqlcmd_path" ]]; then
    sqlcmd=(docker exec -i "$container_name" "$sqlcmd_path" -S localhost -U sa -P "$sa_password" -C -I -b -r1)
    if "${sqlcmd[@]}" -d master -Q 'SET NOCOUNT ON; SELECT 1;' >/dev/null 2>&1; then
      ready=1
      break
    fi
  fi
  sleep 1
done
[[ "$ready" == 1 ]] || { docker logs "$container_name" >&2 || true; fail "SQL Server did not become ready or sqlcmd was unavailable."; }

host_port="$(docker port "$container_name" 1433/tcp | head -n1 | awk -F: '{print $NF}')"
[[ "$host_port" =~ ^[0-9]+$ ]] || fail "mapped port missing"
echo "EA2A_SQLSERVER_VERSION=$("${sqlcmd[@]}" -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));" | tr -d '\r' | tail -n1)"
"${sqlcmd[@]}" -d master -Q "CREATE DATABASE [$database_name]; ALTER DATABASE [$database_name] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE; ALTER DATABASE [$database_name] SET ALLOW_SNAPSHOT_ISOLATION ON;" >/dev/null

connection_string="Server=127.0.0.1,$host_port;Database=$database_name;User Id=sa;Password=$sa_password;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
EA2A_SQL_CONNECTION="$connection_string" \
  dotnet run --project backend/tests/FieldVisit.EA2A.Integration.Tests/FieldVisit.EA2A.Integration.Tests.csproj --configuration Release
