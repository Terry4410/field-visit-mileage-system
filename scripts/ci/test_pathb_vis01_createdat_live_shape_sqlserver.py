"""NONDEPLOY disposable SQL Server 2022 VIS01: Owner-observed six required CreatedAt columns.
Never connects to Azure. The exact separate review SQL is NOT a live execution approval.
"""
import os
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CANDIDATE = (ROOT / "database/uat/FieldVisit_PathB_VIS01_Context_Seed_CREATEDAT_REVIEW_CANDIDATE_20261119.sql").read_text(encoding="utf-8")
BASE_SEED = (ROOT / "database/uat/FieldVisit_PathB_UAT_ThreeRole_Seed_FINAL_CANDIDATE.sql").read_text(encoding="utf-8")
SCHEMA = (ROOT / "scripts/ci/pathb_uat_seed_schema.sql").read_text(encoding="utf-8")
SUBSET = (ROOT / "scripts/ci/pathb_vis01_disposable_1800_subset.sql").read_text(encoding="utf-8")
READBACK = (ROOT / "database/uat/FieldVisit_PathB_VIS01_Context_Seed_READBACK.sql").read_text(encoding="utf-8")
PASSWORD = os.environ["PATHB_SQL_PASSWORD"]
TABLES = (
    "DeploymentSiteLocationAssignments", "DeploymentSites",
    "EmploymentDeploymentSiteAssignments", "Persons",
    "TeamCenterAssignments", "TeamDeploymentSiteAssignments"
)
DATABASES = ("PathBSeedCI_LiveShapePositive", "PathBSeedCI_LiveShapeRollback")

def run(db, script, *, success=True):
    assert db in ("master", *DATABASES)
    p = subprocess.run(
        ["docker", "exec", "-i", "pathb-seed-sql",
         "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa",
         "-P", PASSWORD, "-C", "-I", "-b", "-V", "11", "-d", db],
        input=script, text=True, capture_output=True)
    output = p.stdout + p.stderr
    if success and p.returncode:
        raise AssertionError("EXPECTED_SUCCESS_FAILED: " + output[-2500:])
    if not success and not p.returncode:
        raise AssertionError("EXPECTED_FAILURE_BUT_PASSED")
    return output

def scalar(db, sql):
    text = run(db, "SET NOCOUNT ON; " + sql)
    rows = [s.strip() for s in text.splitlines() if s.strip() and not s.startswith("---")]
    assert rows, text
    return int(rows[0])

def localize(sql, db):
    # Replace only the exact-target guard, solely in the disposable test process.
    start = sql.index("IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY('EngineEdition')),0)<>5")
    end = sql.index("IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections", start)
    assert "db-fieldvisit-pathb-uat" in sql[start:end]
    assert "sql-fieldvisit-jpe-pathb-uat" in sql[start:end]
    return sql[:start] + (
        "IF DB_NAME()<>N'" + db + "' THROW 59502,'DISPOSABLE_TARGET_HOLD',1;\n"
    ) + sql[end:]

def setup(db):
    run("master", "CREATE DATABASE [" + db + "];")
    run(db, SCHEMA)
    marker = (
        "EXEC sys.sp_set_session_context @key=N'PATHB_DISPOSABLE_CI',"
        "@value=N'd6c220877b9976b6478d85b5171d4e06293595ae';\n"
    )
    run(db, marker + BASE_SEED)
    run(db, SUBSET)
    # The real Azure metadata report established no defaults on these six columns.
    # The canonical migration extracts normally HAVE defaults; remove only those
    # in the disposable fixture so the test reproduces the observed live shape.
    for table in TABLES:
        run(db, "ALTER TABLE dbo.[" + table + "] DROP CONSTRAINT [DF_" + table + "_CreatedAt];")
    listed = ",".join("N'" + t + "'" for t in TABLES)
    actual = scalar(db, (
        "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id "
        "WHERE t.name IN (" + listed + ") AND c.name=N'CreatedAt' "
        "AND c.is_nullable=0 AND c.default_object_id=0"
    ))
    assert actual == 6, "REPRODUCED_REQUIRED_COLUMNS_HOLD"

def main():
    assert CANDIDATE.count("BEGIN TRANSACTION;") == 1
    assert CANDIDATE.count("ROLLBACK TRANSACTION;") == 1
    assert len(re.findall(r"\bINSERT dbo\.", CANDIDATE)) == 14
    assert len(re.findall(r"\bUPDATE dbo\.", CANDIDATE)) == 1
    for table in TABLES:
        assert "(N'" + table + "',N'CreatedAt')" in CANDIDATE
        insert = re.search(r"INSERT dbo\." + table + r"\(([^;]+?)\)\s*VALUES\(([^;]+?)\);", CANDIDATE)
        assert insert and "CreatedAt" in insert.group(1) and "@now" in insert.group(2), table
    pos, rollback = DATABASES
    setup(pos)
    out = run(pos, localize(CANDIDATE, pos))
    assert "PATHB_VIS01_CONTEXT_SEED" in out, out
    assert scalar(pos, "SELECT COUNT(*) FROM dbo.Persons") == 1
    assert scalar(pos, "SELECT COUNT(*) FROM dbo.Locations") == 2
    readback = run(pos, localize(READBACK, pos))
    assert "PASS_CATALOG_ONLY_NOT_BUSINESS_UAT" in readback, readback
    denied = run(pos, localize(CANDIDATE, pos), success=False)
    assert "EXACT_EXISTING_SEED_HOLD" in denied, denied
    assert scalar(pos, "SELECT COUNT(*) FROM dbo.Persons") == 1

    setup(rollback)
    run(rollback, "ALTER TABLE dbo.Centers ADD CONSTRAINT CK_VIS01_LiveShapeRollback CHECK(CenterCode <> N'PBV01-CENTER');")
    denied = run(rollback, localize(CANDIDATE, rollback), success=False)
    assert "CK_VIS01_LiveShapeRollback" in denied, denied
    for table in ("Persons", "Employments", "Locations"):
        assert scalar(rollback, "SELECT COUNT(*) FROM dbo." + table) == 0, table
    assert scalar(rollback, "SELECT COUNT(*) FROM dbo.UserIdentityProfiles WHERE EmploymentId IS NOT NULL") == 0
    print("VIS01_SIX_REQUIRED_CREATEDAT_DISPOSABLE=PASS")
    print("AZURE_SQL_RUNTIME=NOT_TESTED")

if __name__ == "__main__":
    main()
