"""Isolated SQL Server 2022 behavioral test for one EF OUTPUT identity-column grant.
No Azure calls, no live grants, no business data. Two differing collations.
"""
import os
import pathlib
import re
import subprocess

SRC=pathlib.Path("database/uat/PathB_UAT_P0_MileageGovernanceEvents_OutputId_SELECT_REVIEW_20261119.sql")
sql=SRC.read_text(encoding="utf-8")
assert sql.count("GRANT SELECT ON OBJECT::dbo.[MileageGovernanceEvents] ([MileageGovernanceEventId])")==1
assert "PREVIOUS_INSERT_GRANT_HOLD" in sql and "UNEXPECTED_TABLE_SELECT_HOLD" in sql
assert not re.search(r"(?im)^\s*(?:GRANT|DENY|REVOKE)\s+(?!SELECT ON OBJECT::dbo\.\[MileageGovernanceEvents\] \(\[MileageGovernanceEventId\]\))",sql)
password=os.environ["PATHB_SQL_PASSWORD"]
def run(query, db="master", ok=True):
    cmd=["docker","exec","-i","pathb-acl-sql","/opt/mssql-tools18/bin/sqlcmd",
        "-S","localhost","-U","sa","-P",password,"-C","-b","-V","11","-d",db,"-h","-1","-W"]
    p=subprocess.run(cmd,input=query,text=True,capture_output=True,timeout=90)
    out=p.stdout+p.stderr
    if (p.returncode==0)!=ok:
        raise AssertionError("UNEXPECTED_SQL_OUTCOME: "+out[-1600:])
    return out

run("SET PARSEONLY ON;\n"+sql)
failure=run(sql,ok=False)
assert "EXACT_PATHB_TARGET_HOLD" in failure, "TARGET_REFUSAL_HOLD: "+failure[-1300:]
# Leave catalog checks + DCL byte-identical aside from fixed synthetic grantee name.
head,marker,tail=sql.partition("SELECT @oid=t.object_id,@cid=c.column_id")
assert marker and "EXACT_IDENTITY_COLUMN_HOLD" in tail
fixture=("SET NOCOUNT ON; SET XACT_ABORT ON;\n"
         "DECLARE @pid int=DATABASE_PRINCIPAL_ID(N'pathb_ci_api'), @oid int, @cid int;\n"
         +marker+tail)
fixture=fixture.replace("TO [api-fieldvisit-pathb-uat_pathb];","TO [pathb_ci_api];")
assert fixture.count("TO [pathb_ci_api];")==1
for db,collation in [("PathB_OutputId_CI1","SQL_Latin1_General_CP1_CI_AS"),
                     ("PathB_OutputId_CI2","Latin1_General_100_BIN2")]:
    run(f"CREATE DATABASE [{db}] COLLATE {collation};")
    try:
        run("""CREATE TABLE dbo.MileageGovernanceEvents
          (MileageGovernanceEventId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, EventType nvarchar(40) NOT NULL);
          CREATE USER [pathb_ci_api] WITHOUT LOGIN;
          GRANT INSERT ON OBJECT::dbo.MileageGovernanceEvents TO [pathb_ci_api];""",db)
        insert_sql="""EXECUTE AS USER=N'pathb_ci_api';
          INSERT INTO dbo.MileageGovernanceEvents(EventType)
          OUTPUT INSERTED.MileageGovernanceEventId VALUES(N'Test');
          REVERT;"""
        initial=run(insert_sql,db,ok=False)
        assert "permission was denied" in initial.lower() and "SELECT" in initial,initial[-1000:]
        run("DENY SELECT ON OBJECT::dbo.MileageGovernanceEvents TO [pathb_ci_api];",db)
        denied=run(fixture,db,ok=False)
        assert "RELEVANT_DENY_HOLD" in denied,denied[-1000:]
        run("REVOKE SELECT ON OBJECT::dbo.MileageGovernanceEvents FROM [pathb_ci_api];",db)
        applied=run(fixture,db)
        assert "APPLIED_ONE_COLUMN_SELECT_CATALOG_ONLY" in applied,applied[-1000:]
        inserted=run(insert_sql,db)
        assert re.search(r"(?m)^\s*\d+\s*$",inserted),inserted[-1000:]
        read=run("""SELECT dp.permission_name,dp.minor_id FROM sys.database_permissions dp
          WHERE dp.class=1 AND dp.major_id=OBJECT_ID(N'dbo.MileageGovernanceEvents')
          AND dp.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'pathb_ci_api')
          ORDER BY dp.permission_name;""",db)
        assert "SELECT" in read and "INSERT" in read,read[-1000:]
        replay=run(fixture,db,ok=False)
        assert "EXISTING_SELECT_HOLD" in replay,replay[-1000:]
        print(f"COLLATION={collation} INSERT_OUTPUT_ID_BEFORE_DENIED=PASS AFTER_ALLOWED=PASS DENY_GATE=PASS REPLAY_HOLD=PASS")
    finally:
        run(f"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}];")
print("NONDEPLOY_SQL2022_MILEAGE_OUTPUT_COLUMN_PASS AZURE_SQL_CALLS=0")
