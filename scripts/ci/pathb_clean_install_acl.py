#!/usr/bin/env python3
"""Source-bound Path B SQL2022 runtime ACL fixture. NONDEPLOY only."""
from __future__ import annotations
import argparse, csv, json, re, shutil, subprocess
from pathlib import Path

HELPER_BLOB = "170721f3f3129b6bdfd61855df20c75ea1aaaa46"
APPDBCONTEXT_BLOB = "fff038e9b2a8ce884fddad0a44da0252baa35571"
APPDBCONTEXT_PATH = "backend/src/FieldVisit.Infrastructure/AppDbContext.cs"
PR82_BASE = "ae9ca02cf2d9539fe9bed61a073d60d63731cbb9"
PROTECTED = {"SchemaVersions", "SchemaMigrationDataBaselines", "FieldVisitMigrationLedger"}
OPTIONAL_PROTECTED = {"FieldVisitMigrationLedger"}
REQUIRED = {"010", "010_AND_POST010_OPTIONC"}
IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")

def rows_from(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    required = {"table", "schema_phase", "SELECT", "INSERT", "UPDATE", "DELETE", "status"}
    if len(rows) != 30 or not rows or not required.issubset(rows[0]):
        raise SystemExit("ACL_MATRIX_CONTRACT_STOP")
    if len({r["table"] for r in rows}) != len(rows):
        raise SystemExit("ACL_MATRIX_DUPLICATE_STOP")
    for r in rows:
        if not IDENT.fullmatch(r["table"]) or r["status"] != "SOURCE_REVIEW_CANDIDATE":
            raise SystemExit("ACL_MATRIX_ROW_STOP")
        if any(r[k] not in {"YES", "NO"} for k in ("SELECT", "INSERT", "UPDATE", "DELETE")):
            raise SystemExit("ACL_MATRIX_CRUD_STOP")
    return rows

def ef_mapped_tables(path: Path) -> list[str]:
    source = path.read_text(encoding="utf-8")
    tables = re.findall(r'ToTable\(\s*"([^"]+)"', source)
    if len(tables) != 57 or len(set(tables)) != 57 or any(not IDENT.fullmatch(x) for x in tables):
        raise SystemExit("EF_TABLE_SELECT_SOURCE_CONTRACT_STOP")
    return tables

def grant_sql(rows: list[dict[str, str]], mapped_tables: list[str]) -> str:
    out = [
        "-- Source-bound EF SELECT whitelist plus object-scoped CRUD matrix; disposable SQL2022 only.",
        "SET XACT_ABORT ON;",
        "DECLARE @object_id int;",
    ]
    for name in mapped_tables:
        out += [f"SET @object_id=OBJECT_ID(N'dbo.{name}',N'U');",
                "IF @object_id IS NULL",
                f"    THROW 51072,N'REQUIRED_EF_READ_OBJECT_MISSING:{name}',1;",
                f"GRANT SELECT ON OBJECT::dbo.[{name}] TO [fieldvisit_disposable_runtime];"]
    mapped_set = set(mapped_tables)
    for r in rows:
        name, phase = r["table"], r["schema_phase"]
        required = phase in REQUIRED or (phase == "PROTECTED" and name not in OPTIONAL_PROTECTED)
        out += [f"SET @object_id=OBJECT_ID(N'dbo.{name}',N'U');", "IF @object_id IS NULL", "BEGIN"]
        if required:
            out.append(f"    THROW 51071,N'REQUIRED_SCHEMA_OBJECT_MISSING:{name}',1;")
        else:
            out.append(f"    PRINT N'OPTIONAL_CANDIDATE_OBJECT_ABSENT:{name}';")
        out += ["END", "ELSE", "BEGIN"]
        rights = []
        if r["SELECT"] == "YES" and name not in mapped_set:
            rights.append("SELECT")
        if name not in PROTECTED:
            rights += [k for k in ("INSERT", "UPDATE", "DELETE") if r[k] == "YES"]
        if rights:
            out.append(f"    GRANT {', '.join(rights)} ON OBJECT::dbo.[{name}] TO [fieldvisit_disposable_runtime];")
        if name in PROTECTED:
            out.append(f"    DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.[{name}] TO [fieldvisit_disposable_runtime];")
        out.append("END;")
    out.append("PRINT N'PATHB_OBJECT_SCOPED_RUNTIME_GRANTS_APPLIED';")
    return "\n".join(out) + "\n"

PROBE = r'''using Microsoft.Data.SqlClient;
internal static class CleanInstallRuntimeAclProbe
{
    private static async Task DenyAsync(SqlConnection connection, string sql)
    {
        try
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 10 };
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException error) when (error.Number == 229) { return; }
        throw new InvalidOperationException("RUNTIME_HISTORY_DML_NOT_DENIED");
    }

    private static async Task<string?> WritableColumnAsync(SqlConnection connection, string objectName)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (1) QUOTENAME(c.name)
            FROM sys.columns c
            WHERE c.object_id=OBJECT_ID(@objectName,N'U')
              AND c.is_identity=0 AND c.is_computed=0
              AND c.generated_always_type=0 AND c.system_type_id<>189
            ORDER BY c.column_id;
            """, connection) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("@objectName", objectName);
        return await command.ExecuteScalarAsync() as string;
    }

    public static async Task VerifyAsync(SqlConnection connection)
    {
        await using (var rights = new SqlCommand("""
            SELECT CASE WHEN IS_SRVROLEMEMBER(N'sysadmin')=0
                AND IS_ROLEMEMBER(N'db_owner')=0
                AND IS_ROLEMEMBER(N'db_datawriter')=0
                AND IS_ROLEMEMBER(N'db_datareader')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE')=0
              THEN 1 ELSE 0 END;
            """, connection) { CommandTimeout = 10 })
        {
            if (Convert.ToInt32(await rights.ExecuteScalarAsync()) != 1)
                throw new InvalidOperationException("RUNTIME_PRINCIPAL_BROAD_RIGHTS_PRESENT");
        }

        var targets = new[]
        {
            ("dbo.SchemaVersions", false),
            ("dbo.SchemaMigrationDataBaselines", false),
            ("dbo.FieldVisitMigrationLedger", true)
        };
        var denied = 0;
        foreach (var (objectName, optional) in targets)
        {
            await using var exists = new SqlCommand("SELECT OBJECT_ID(@objectName,N'U');", connection)
                { CommandTimeout = 10 };
            exists.Parameters.AddWithValue("@objectName", objectName);
            var objectId = await exists.ExecuteScalarAsync();
            if (objectId is null or DBNull)
            {
                if (optional) continue;
                throw new InvalidOperationException("RUNTIME_REQUIRED_HISTORY_TABLE_MISSING");
            }

            var quotedTable = objectName.Replace("dbo.", "dbo.[", StringComparison.Ordinal) + "]";
            var column = await WritableColumnAsync(connection, objectName);
            if (string.IsNullOrWhiteSpace(column))
                throw new InvalidOperationException("RUNTIME_HISTORY_TEST_COLUMN_MISSING");
            var statements = new[]
            {
                $"INSERT INTO {quotedTable} DEFAULT VALUES;",
                $"UPDATE {quotedTable} SET {column}={column} WHERE 1=0;",
                $"DELETE FROM {quotedTable} WHERE 1=0;"
            };
            foreach (var statement in statements)
            {
                await DenyAsync(connection, statement);
                denied++;
            }
        }
        if (denied < 6)
            throw new InvalidOperationException("RUNTIME_HISTORY_DENIAL_COVERAGE_INCOMPLETE");
        Console.WriteLine("CLEAN_INSTALL=RUNTIME_PRINCIPAL_NO_BROAD_ROLES_AND_HISTORY_DML_229_DENY_PASS");
    }
}
'''

def prepare(repo: Path, runner: Path, grants: Path) -> None:
    helper = repo / "tools/clean-install/HttpApiSmoke.cs"
    blob = subprocess.check_output(["git", "-C", str(repo), "hash-object", str(helper)], text=True).strip()
    app_context = repo / APPDBCONTEXT_PATH
    app_context_blob = subprocess.check_output(["git", "-C", str(repo), "hash-object", str(app_context)], text=True).strip()
    ledger = json.loads((repo / "docs/release/g1-execution/promotion/integration-ledger.json").read_text())
    if blob != HELPER_BLOB or ledger["integratedFiles"].get("tools/clean-install/HttpApiSmoke.cs") != {
        "sha": HELPER_BLOB, "source": "PR48"
    }:
        raise SystemExit("IMMUTABLE_HTTP_PROBE_SOURCE_STOP")
    base_blob = subprocess.check_output(["git", "-C", str(repo), "rev-parse", f"{PR82_BASE}:{APPDBCONTEXT_PATH}"], text=True).strip()
    if app_context_blob != APPDBCONTEXT_BLOB or base_blob != APPDBCONTEXT_BLOB:
        raise SystemExit("EF_SELECT_SOURCE_BINDING_STOP")
    rows = rows_from(repo / "docs/release/g1-execution/promotion/API-RUNTIME-SQL-CRUD-MATRIX.csv")
    mapped_tables = ef_mapped_tables(app_context)
    sql = grant_sql(rows, mapped_tables)
    if re.search(r"(?i)\b(db_datawriter|db_datareader|db_owner)\b|GRANT [^;]+ ON SCHEMA::|VIEW DEFINITION", sql):
        raise SystemExit("BROAD_RUNTIME_RIGHTS_STOP")
    grants.parent.mkdir(parents=True, exist_ok=True)
    grants.write_text(sql, encoding="utf-8", newline="\n")
    runner.mkdir(parents=True, exist_ok=False)
    for source in (repo / "tools/clean-install").iterdir():
        if source.is_file():
            shutil.copy2(source, runner / source.name)

    path = runner / "HttpApiSmoke.cs"
    source = path.read_text(encoding="utf-8")
    start = source.index("            await using var grant = new SqlCommand(\"\"\"")
    end_token = "            await grant.ExecuteNonQueryAsync();"
    end = source.index(end_token, start) + len(end_token)
    replacement = r'''            await using (var grant = new SqlCommand("""
                CREATE USER [fieldvisit_disposable_runtime] FOR LOGIN [fieldvisit_disposable_runtime];
                GRANT CONNECT TO [fieldvisit_disposable_runtime];
                """, install) { CommandTimeout = 10 })
            {
                await grant.ExecuteNonQueryAsync();
            }
            var grantsPath = Environment.GetEnvironmentVariable("FIELDVISIT_API_RUNTIME_GRANTS_SQL_FILE");
            if (string.IsNullOrWhiteSpace(grantsPath) || !File.Exists(grantsPath))
                throw new InvalidOperationException("RUNTIME_GRANTS_FILE_MISSING");
            var grantsSql = await File.ReadAllTextAsync(grantsPath);
            await using (var applyGrants = new SqlCommand(grantsSql, install) { CommandTimeout = 30 })
                await applyGrants.ExecuteNonQueryAsync();'''
    source = source[:start] + replacement + source[end:]
    anchor = "        await using var runtime = new SqlConnection(config.ConnectionString);\n        await runtime.OpenAsync();"
    if source.count(anchor) != 1:
        raise SystemExit("RUNTIME_CONNECTION_PATCH_STOP")
    source = source.replace(anchor, anchor + "\n        await CleanInstallRuntimeAclProbe.VerifyAsync(runtime);", 1)
    path.write_text(source, encoding="utf-8", newline="\n")
    (runner / "RuntimeAclProbe.cs").write_text(PROBE, encoding="utf-8", newline="\n")

def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", type=Path, default=Path("."))
    ap.add_argument("--matrix", type=Path, default=Path("docs/release/g1-execution/promotion/API-RUNTIME-SQL-CRUD-MATRIX.csv"))
    ap.add_argument("--runner", type=Path)
    ap.add_argument("--appdb", type=Path, default=Path(APPDBCONTEXT_PATH))
    ap.add_argument("--grants", type=Path)
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        rows = rows_from(a.matrix)
        mapped_tables = ef_mapped_tables(a.appdb)
        sql = grant_sql(rows, mapped_tables)
        for name in PROTECTED:
            if f"DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.[{name}]" not in sql:
                raise SystemExit("HISTORY_DENY_MISSING")
        if any(x in sql.lower() for x in ("db_datawriter", "db_datareader", "db_owner")):
            raise SystemExit("BROAD_ROLE_PRESENT")
        if "OPTIONAL_CANDIDATE_OBJECT_ABSENT:ChangeRequests" not in sql:
            raise SystemExit("FUTURE_SCHEMA_NOT_GATED")
        if "GRANT INSERT, UPDATE, DELETE ON SCHEMA::dbo" in sql:
            raise SystemExit("SCHEMA_WIDE_GRANT_PRESENT")
        if "GRANT SELECT ON SCHEMA::dbo" in sql or len(mapped_tables) != 57:
            raise SystemExit("EF_SELECT_WHITELIST_CONTRACT_STOP")
        select_grants = [line for line in sql.splitlines() if line.strip().startswith("GRANT ") and "SELECT" in line and "ON OBJECT::dbo.[" in line]
        if len(select_grants) != 63:
            raise SystemExit("EF_SELECT_GRANT_COUNT_STOP")
        for row in rows:
            grants = [line.strip() for line in sql.splitlines()
                      if line.strip().startswith("GRANT ") and f"ON OBJECT::dbo.[{row['table']}]" in line]
            granted = set()
            for line in grants:
                granted.update(line.split(" ON OBJECT::", 1)[0][len("GRANT "):].split(", "))
            for operation in ("INSERT", "UPDATE", "DELETE"):
                expected = row[operation] == "YES" and row["table"] not in PROTECTED
                if (operation in granted) != expected:
                    raise SystemExit("CRUD_MATRIX_GRANT_PARITY_STOP")
        if "REQUIRED_EF_READ_OBJECT_MISSING:UserIdentityProfiles" not in sql:
            raise SystemExit("LOGIN_READ_TABLE_SELECT_MISSING")
        if "OPTIONAL_CANDIDATE_OBJECT_ABSENT:FieldVisitMigrationLedger" not in sql:
            raise SystemExit("OPTIONAL_PROTECTED_LEDGER_CONTRACT_STOP")
        if "DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.[FieldVisitMigrationLedger]" not in sql:
            raise SystemExit("OPTIONAL_PROTECTED_LEDGER_DENY_STOP")
        if "WHERE 1=0" not in PROBE or "DEFAULT VALUES" not in PROBE:
            raise SystemExit("HISTORY_DML_PROBE_MISSING")
        print("PathB ACL matrix parser, table-scoped grants, protected denies: PASS")
        return
    if a.runner is None or a.grants is None:
        raise SystemExit("RUNNER_AND_GRANTS_REQUIRED")
    prepare(a.repo.resolve(), a.runner.resolve(), a.grants.resolve())

if __name__ == "__main__":
    main()
