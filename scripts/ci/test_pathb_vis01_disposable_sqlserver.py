"""NONDEPLOY ONLY: disposable SQL Server; no Azure writes or credential access."""
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SEED = (ROOT / 'database/uat/FieldVisit_PathB_UAT_ThreeRole_Seed_FINAL_CANDIDATE.sql').read_text()
VIS01 = (ROOT / 'database/uat/FieldVisit_PathB_VIS01_Context_Seed_LOCKED_REVIEW.sql').read_text()
READBACK = (ROOT / 'database/uat/FieldVisit_PathB_VIS01_Context_Seed_READBACK.sql').read_text()
SRC = (ROOT / 'scripts/ci/pathb_vis01_disposable_1800_subset.sql').read_text()
PREVIOUS = (ROOT / 'scripts/ci/pathb_uat_seed_schema.sql').read_text()
PASSWORD = os.environ['PATHB_SQL_PASSWORD']
CONTAINER = 'pathb-seed-sql'

def cmd(database: str, sql: str, *, success=True):
    assert database in ('master', 'PathBSeedCI_Positive', 'PathBSeedCI_VIS01Rollback')
    p = subprocess.run(['docker','exec','-i',CONTAINER,'/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-P',PASSWORD,'-C','-b','-V','11','-d',database],input=sql,text=True,capture_output=True)
    output=p.stdout+p.stderr
    if success and p.returncode:
        raise AssertionError('EXPECTED_SUCCESS_FAILED: '+output[-3000:])
    if not success and not p.returncode:
        raise AssertionError('EXPECTED_FAILURE_BUT_PASSED')
    return output

def localize(source: str, database: str, locked=False):
    assert database in ('PathBSeedCI_Positive','PathBSeedCI_VIS01Rollback')
    if locked:
        lock="THROW 59500, 'LOCKED_REVIEW_ONLY_NOT_AUTHORIZED_FOR_SQL_WRITE', 1;"
        assert source.count(lock)==1
        source=source.replace(lock,"-- CI ONLY: reviewer-lock removed inside isolated test fixture.")
    head=source.index('IF COALESCE(TRY_CONVERT(int,')
    tail=source.index('IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections',head)
    assert 'db-fieldvisit-pathb-uat' in source[head:tail]
    return source[:head]+f"IF DB_NAME()<>N'{database}' THROW 59502,'DISPOSABLE_TARGET_HOLD',1;\n"+source[tail:]

def scalar(db: str, query: str):
    output=cmd(db,'SET NOCOUNT ON;'+query)
    rows=[x.strip() for x in output.splitlines() if x.strip() and not x.strip().startswith('---')]
    assert rows,output
    return int(rows[0])

# Reuse disposable positive base (previous seed suite has already run).
cmd('PathBSeedCI_Positive',SRC)
out=cmd('PathBSeedCI_Positive',localize(VIS01,'PathBSeedCI_Positive',locked=True))
assert 'PATHB_VIS01_CONTEXT_SEED' in out,out
assert scalar('PathBSeedCI_Positive','SELECT COUNT(*) FROM dbo.Persons')==1
assert scalar('PathBSeedCI_Positive','SELECT COUNT(*) FROM dbo.Locations')==2
out=cmd('PathBSeedCI_Positive',localize(READBACK,'PathBSeedCI_Positive'))
assert 'PASS_CATALOG_ONLY_NOT_BUSINESS_UAT' in out,out
out=cmd('PathBSeedCI_Positive',localize(VIS01,'PathBSeedCI_Positive',locked=True),success=False)
assert 'EXACT_EXISTING_SEED_HOLD' in out,out
assert scalar('PathBSeedCI_Positive','SELECT COUNT(*) FROM dbo.Persons')==1

# A separate disposable database tests a late failure and full rollback.
cmd('master','CREATE DATABASE PathBSeedCI_VIS01Rollback;')
cmd('PathBSeedCI_VIS01Rollback',PREVIOUS)
marker="EXEC sys.sp_set_session_context @key=N'PATHB_DISPOSABLE_CI',@value=N'd6c220877b9976b6478d85b5171d4e06293595ae';\n"
cmd('PathBSeedCI_VIS01Rollback',marker+SEED)
cmd('PathBSeedCI_VIS01Rollback',SRC)
cmd('PathBSeedCI_VIS01Rollback',"ALTER TABLE dbo.Centers ADD CONSTRAINT CK_VIS01_Test_ForceRollback CHECK(CenterCode <> N'PBV01-CENTER');")
out=cmd('PathBSeedCI_VIS01Rollback',localize(VIS01,'PathBSeedCI_VIS01Rollback',locked=True),success=False)
assert 'CK_VIS01_Test_ForceRollback' in out,out
assert scalar('PathBSeedCI_VIS01Rollback','SELECT COUNT(*) FROM dbo.Persons')==0
assert scalar('PathBSeedCI_VIS01Rollback','SELECT COUNT(*) FROM dbo.Employments')==0
assert scalar('PathBSeedCI_VIS01Rollback','SELECT COUNT(*) FROM dbo.Locations')==0
assert scalar('PathBSeedCI_VIS01Rollback','SELECT COUNT(*) FROM dbo.UserIdentityProfiles WHERE EmploymentId IS NOT NULL')==0
print('PATHB_VIS01_CONTEXT_DISPOSABLE_BEHAVIOR=PASS')
print('AZURE_SQL_RUNTIME=NOT_TESTED')
