"""Exercise the exact ACL candidate parser and its catalog body on disposable SQL 2022.

Azure target and external-principal guards are tested fail-closed unchanged. The
catalog body uses a synthetic principal only inside disposable fixture databases.
"""
import hashlib
import os
import pathlib
import re
import subprocess

SOURCE = pathlib.Path("database/uat/PathB_UAT_Runtime_ACL_Readonly_SINGLE_RESULT_REVIEW.sql")
EXPECTED_SHA = "f93db87074932fd25b37d6cb227a266cb7730e7242f66c9ffb709801dd4e7143"
raw = SOURCE.read_bytes()
assert hashlib.sha256(raw).hexdigest() == EXPECTED_SHA, "SOURCE_SHA_HOLD"
sql = raw.decode("utf-8-sig")
assert "DECLARE @wanted TABLE" in sql and sql.count("SELECT record_type,table_name,operation,catalog_state,grantee,scope,detail") == 1
statements = re.sub(r"N?'(?:''|[^'])*'", "N''", re.sub(r"--[^\n]*", "", sql))
assert not re.search(r"\b(?:GRANT|REVOKE|DENY|CREATE|ALTER|DROP|EXEC(?:UTE)?)\b", statements, re.I), "NON_READONLY_SOURCE_HOLD"
assert not re.search(r"\b(?:INSERT|UPDATE|DELETE|MERGE)\s+(?:dbo\.|\[dbo\])", sql, re.I), "BUSINESS_WRITE_HOLD"
assert "FROM sys.database_permissions" in sql and "FROM sys.database_principals" in sql

password = os.environ["PATHB_SQL_PASSWORD"]

def run(query, db="master", ok=True, output=False):
    args = ["docker", "exec", "-i", "pathb-acl-sql", "/opt/mssql-tools18/bin/sqlcmd",
            "-S", "localhost", "-U", "sa", "-P", password, "-C", "-b", "-V", "11", "-d", db]
    if output:
        args += ["-h", "-1", "-s", "|", "-W", "-w", "2048"]
    proc = subprocess.run(args, input=query, text=True, capture_output=True, timeout=90)
    if (proc.returncode == 0) != ok:
        raise AssertionError("SQL_UNEXPECTED_OUTCOME: " + (proc.stdout + proc.stderr)[-1800:])
    return proc.stdout + proc.stderr

# Parse the original bytes using the actual SQL Server parser, without execution.
run("SET PARSEONLY ON;\n" + sql)
refusal = run(sql, ok=False)
assert "PATHB_TARGET_HOLD" in refusal, "TARGET_GUARD_NOT_FAIL_CLOSED: " + refusal[-1800:]

# Never edit the source. The only test adaptation replaces the Azure-only
# opening guard and external-principal lookup in memory; all 20 catalog
# operations, role expansion, unions, and final SELECT remain byte-identical.
opening, marker, body = sql.partition("DECLARE @wanted TABLE")
assert marker and "THROW 59803" in opening and opening.count("DECLARE @principalId int;") == 1
fixture_query = "SET NOCOUNT ON;\nDECLARE @principalId int = DATABASE_PRINCIPAL_ID(N'pathb_ci_api');\n" + marker + body
names = re.findall(r"\(N'([^']+)', N'(?:SELECT|INSERT|UPDATE|DELETE)'\)", sql)
assert len(names) == 20 and len(set(names)) >= 10

for db, collation in (("PathB_ACL_CI_1", "SQL_Latin1_General_CP1_CI_AS"),
                      ("PathB_ACL_CI_2", "Latin1_General_100_BIN2")):
    run(f"CREATE DATABASE [{db}] COLLATE {collation};")
    tables = sorted(set(names))
    fixture = "SET NOCOUNT ON;\n" + "\n".join(
        f"CREATE TABLE dbo.[{name}] (Id int NOT NULL, TestColumn nvarchar(20) NULL);" for name in tables)
    fixture += """
CREATE USER [pathb_ci_api] WITHOUT LOGIN;
CREATE ROLE [pathb_ci_inner];
CREATE ROLE [pathb_ci_outer];
ALTER ROLE [pathb_ci_inner] ADD MEMBER [pathb_ci_api];
ALTER ROLE [pathb_ci_outer] ADD MEMBER [pathb_ci_inner];
GRANT SELECT ON OBJECT::dbo.[VisitTripSnapshots] TO [pathb_ci_api];
GRANT INSERT ON OBJECT::dbo.[RouteCalculationAttempts] TO [pathb_ci_outer];
DENY SELECT ON OBJECT::dbo.[UserRoles] TO [pathb_ci_inner];
GRANT SELECT ON OBJECT::dbo.[UserFavoriteLocations] ([TestColumn]) TO [pathb_ci_inner];
GRANT UPDATE ON SCHEMA::dbo TO [pathb_ci_outer];
"""
    run(fixture, db)
    result = run(fixture_query, db, output=True)
    lines = [x for x in result.splitlines() if x.strip()]
    rows = [[part.strip() for part in line.split("|")] for line in lines]
    assert all(len(row) == 7 and row[0] in {"PERMISSION", "ROLE", "BROAD_PERMISSION", "SUMMARY"}
               for row in rows), "MULTIPLE_OR_MALFORMED_RESULTSETS: " + result[-1000:]
    permissions = [r for r in rows if r[0] == "PERMISSION"]
    assert len({(r[1], r[2]) for r in permissions}) == 20, "WANTED_OPERATION_COVERAGE_HOLD"
    assert len([r for r in rows if r[0] == "ROLE"]) == 2
    assert {r[4] for r in rows if r[0] == "ROLE"} == {"pathb_ci_inner", "pathb_ci_outer"}
    assert any(r[1:5] == ["RouteCalculationAttempts", "INSERT", "GRANT", "pathb_ci_outer"] for r in permissions)
    assert any(r[1:5] == ["VisitTripSnapshots", "SELECT", "GRANT", "pathb_ci_api"] for r in permissions)
    assert any(r[1:5] == ["UserRoles", "SELECT", "DENY", "pathb_ci_inner"] for r in permissions)
    assert any(r[1:5] == ["UserFavoriteLocations", "SELECT", "GRANT", "pathb_ci_inner"] and r[6] == "TestColumn" for r in permissions)
    broad = [r for r in rows if r[0] == "BROAD_PERMISSION"]
    assert any(r[2:6] == ["UPDATE", "GRANT", "pathb_ci_outer", "SCHEMA"] for r in broad)
    summary = {r[2]: r[3] for r in rows if r[0] == "SUMMARY"}
    assert summary == {"WANTED_OPERATIONS": "20", "CATALOG_ONLY_NOT_EFFECTIVE_PROOF": "TRUE",
                       "ROLE_COUNT": "2", "BROAD_PERMISSION_ROWS": str(len(broad))}, summary
    print(f"COLLATION={collation} SINGLE_RESULTSET=PASS OPERATIONS=20 ROLES=2 BROAD={len(broad)}")
    run(f"DROP DATABASE [{db}];")

print("EXACT_SOURCE_SHA=PASS SQLSERVER2022_PARSE=PASS AZURE_GUARD=PASS")
print("AZURE_SQL_EXECUTIONS=0 PERMISSION_CHANGES_IN_TARGET=0")
