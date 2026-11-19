"""Nondeploy SQL Server 2022 behavior tests for the P0 core ACL candidate.

Disposable SQL2022 only. Never execute fixed Azure target GRANT in Azure.
The fixture strips ONLY Azure environment/principal guard, retaining the exact
permission preflight, 17 GRANTs, and transaction/readback in source.
"""
import os
import pathlib
import re
import subprocess

SOURCE = pathlib.Path("database/uat/PathB_UAT_P0_Core_Runtime_ACL_GRANT_REVIEW_20261119.sql")
raw = SOURCE.read_text(encoding="utf-8")
assert raw.count("GRANT ") >= 17 and raw.count(" GRANT ") == 17, "GRANT_COUNT_HOLD"
assert raw.count("ON OBJECT::dbo.") == 17, "DCL_SCOPE_HOLD"
assert "BEGIN TRANSACTION;" in raw and "ROLLBACK TRANSACTION;" in raw
assert "APPLIED_17_PLUS_EXISTING_3_CATALOG_ONLY" in raw
assert "db_owner" in raw and "RELEVANT_DENY_HOLD" in raw
assert not re.search(r"(?im)^\s*(?:CREATE|ALTER|DROP)\s+(?:DATABASE|TABLE|ROLE|USER)", raw)

password = os.environ["PATHB_SQL_PASSWORD"]
def run(query, db="master", ok=True):
    args=["docker","exec","-i","pathb-acl-sql","/opt/mssql-tools18/bin/sqlcmd",
          "-S","localhost","-U","sa","-P",password,"-C","-b","-V","11","-d",db,"-h","-1","-W"]
    p=subprocess.run(args,input=query,text=True,capture_output=True,timeout=100)
    out=(p.stdout+p.stderr)
    if (p.returncode==0)!=ok:
        raise AssertionError("SQL_OUTCOME_HOLD: "+out[-1800:])
    return out

# Prove the unmodified source parses and refuses the wrong engine/target.
run("SET PARSEONLY ON;\n"+raw)
assert "EXACT_PATHB_TARGET_HOLD" in run(raw,ok=False)

head, sep, rest=raw.partition("DECLARE @wanted TABLE")
assert sep and "BEGIN TRY" in rest
# Synthetic principal and same-name schema in a disposable DB;
# all 20 wanted operations and every preflight and GRANT after the declaration remain intact.
fixture_sql=("SET NOCOUNT ON; SET XACT_ABORT ON;\n"
             "DECLARE @pid int=DATABASE_PRINCIPAL_ID(N'pathb_p0_ci_api');\n"
             +sep+rest)
fixture_sql=fixture_sql.replace("TO [api-fieldvisit-pathb-uat_pathb];","TO [pathb_p0_ci_api];")
assert fixture_sql.count("TO [pathb_p0_ci_api];")==17

table_names=sorted(set(re.findall(r"\(N'([A-Za-z0-9_]+)',N'(?:SELECT|INSERT|UPDATE|DELETE)',[01]\)",rest)))
assert len(table_names)==13, table_names

for db, collation in [("PathB_P0_Core_1","SQL_Latin1_General_CP1_CI_AS"),
                      ("PathB_P0_Core_2","Latin1_General_100_BIN2")]:
    run(f"CREATE DATABASE [{db}] COLLATE {collation};")
    try:
        create="SET NOCOUNT ON;\n"+"\n".join(
            f"CREATE TABLE dbo.[{t}](Id int NOT NULL PRIMARY KEY, X nvarchar(20) NULL);" for t in table_names
        )+"\nCREATE USER [pathb_p0_ci_api] WITHOUT LOGIN;\n"
        for t in ("Roles","UserIdentityProfiles","UserRoleAssignments"):
            create+=f"GRANT SELECT ON OBJECT::dbo.[{t}] TO [pathb_p0_ci_api];\n"
        run(create,db)
        # A deliberately existing DENY must fail before any new DCL.
        run("DENY SELECT ON OBJECT::dbo.[UserRoles] TO [pathb_p0_ci_api];",db)
        denied=run(fixture_sql,db,ok=False)
        assert "RELEVANT_DENY_HOLD" in denied, denied[-1600:]
        run("REVOKE SELECT ON OBJECT::dbo.[UserRoles] FROM [pathb_p0_ci_api];",db)
        # Live grant candidate must issue a single transaction and 17 exact object grants.
        passed=run(fixture_sql,db)
        assert "APPLIED_17_PLUS_EXISTING_3_CATALOG_ONLY" in passed,passed[-1600:]
        rows=run("""SET NOCOUNT ON;
        SELECT COUNT(*) FROM sys.database_permissions dp
        WHERE dp.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'pathb_p0_ci_api')
          AND dp.class=1 AND dp.minor_id=0 AND dp.state IN('G','W')
          AND dp.permission_name IN('SELECT','INSERT','UPDATE','DELETE');""",db)
        assert re.search(r"(?m)^\s*20\s*$",rows),rows
        replay=run(fixture_sql,db,ok=False)
        assert "UNEXPECTED_EXISTING_GRANT_HOLD" in replay,replay[-1600:]
        print(f"COLLATION={collation} CORE_GRANT=17 PRESERVED=3 DENY_GATE=PASS REPLAY_REFUSAL=PASS")
    finally:
        run(f"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}];")
print("NONDEPLOY_SQL2022_P0_CORE_ACL_PASS AZURE_SQL_CALLS=0")
