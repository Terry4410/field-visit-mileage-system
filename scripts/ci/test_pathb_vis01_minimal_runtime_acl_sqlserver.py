"""Disposable SQLServer effective privilege tests for the exact source-reviewed VIS01 minimum grant list.
No Azure principals, external tokens, or unmasked app identifiers are used here.
"""
import os, re, subprocess
from pathlib import Path

root=Path(__file__).resolve().parents[2]
source=(root/"database/uat/FieldVisit_PathB_VIS01_Complete_Minimal_Runtime_ACL_LOCKED_TEST.sql").read_text()
password=os.environ["PATHB_SQL_PASSWORD"]
db="PathBSeedCI_Positive"

def call(sql,expect_success=True):
    p=subprocess.run(["docker","exec","-i","pathb-seed-sql","/opt/mssql-tools18/bin/sqlcmd",
       "-S","localhost","-U","sa","-P",password,"-C","-I","-b","-V","11","-d",db],
       input=sql,text=True,capture_output=True)
    output=p.stdout+p.stderr
    if expect_success and p.returncode:
        raise AssertionError("SQL_NOT_SUCCESS: "+output[-1600:])
    if not expect_success and not p.returncode:
        raise AssertionError("SQL_UNEXPECTED_SUCCESS")
    return output

# Positive VIS01 context fixtures have already populated source-derived tables.
# These DDL are *only* disposable missing base tables for ACL behavior, not a canonical schema.
table_sql="""CREATE TABLE dbo.VisitTrips(Id int IDENTITY PRIMARY KEY,Label nvarchar(50));
CREATE TABLE dbo.VisitTripStops(Id int IDENTITY PRIMARY KEY,Label nvarchar(50));
CREATE TABLE dbo.MileageCalculations(Id int IDENTITY PRIMARY KEY,Label nvarchar(50));
CREATE TABLE dbo.VisitTripStatusHistory(Id int IDENTITY PRIMARY KEY,Label nvarchar(50));
CREATE TABLE dbo.AuditLogs(Id int IDENTITY PRIMARY KEY,Label nvarchar(50));
CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(50) PRIMARY KEY);"""
call(table_sql)
call("CREATE USER [vis01_readwrite_test] WITHOUT LOGIN;CREATE USER [vis01_outsider_test] WITHOUT LOGIN;")
selects=re.findall(r"GRANT SELECT ON OBJECT::dbo\.\[(\w+)\] TO \[api-fieldvisit-pathb-uat_pathb\];",source)
inserts=re.findall(r"GRANT INSERT ON OBJECT::dbo\.\[(\w+)\] TO \[api-fieldvisit-pathb-uat_pathb\];",source)
assert len(selects)==16 and len(inserts)==5
for table in selects:
    out=call(f"EXECUTE AS USER=N'vis01_readwrite_test';SELECT COUNT(*) FROM dbo.[{table}];REVERT;",False)
    assert "permission was denied" in out.lower(),out
grants="BEGIN TRANSACTION;\n"+"\n".join(
    f"GRANT SELECT ON OBJECT::dbo.[{table}] TO [vis01_readwrite_test];" for table in selects
)+"\n"+"\n".join(
    f"GRANT INSERT ON OBJECT::dbo.[{table}] TO [vis01_readwrite_test];" for table in inserts
)+"\nCOMMIT TRANSACTION;"
call(grants)
for table in selects:
    call(f"EXECUTE AS USER=N'vis01_readwrite_test';SELECT COUNT(*) FROM dbo.[{table}];REVERT;")
    out=call(f"EXECUTE AS USER=N'vis01_outsider_test';SELECT COUNT(*) FROM dbo.[{table}];REVERT;",False)
    assert "permission was denied" in out.lower(),out
for table in inserts:
    call(f"EXECUTE AS USER=N'vis01_readwrite_test';INSERT dbo.[{table}](Label) OUTPUT INSERTED.Id VALUES(N'fixture');REVERT;")
    out=call(f"EXECUTE AS USER=N'vis01_outsider_test';INSERT dbo.[{table}](Label) VALUES(N'no');REVERT;",False)
    assert "permission was denied" in out.lower(),out
for statement in (
   "UPDATE dbo.VisitTrips SET Label=N'unapproved'",
   "DELETE FROM dbo.VisitTrips",
   "INSERT dbo.SchemaVersions(VersionNumber) VALUES(N'unauthorized')",
   "INSERT dbo.Employments(PersonId,OrganizationId,SourceType) VALUES(1,1,N'unauthorized')",
):
    out=call(f"EXECUTE AS USER=N'vis01_readwrite_test';{statement};REVERT;",False)
    assert "permission was denied" in out.lower(),out
# Ensure APP role itself does not acquire broad fixed db roles via this limited grant.
p=call("SET NOCOUNT ON;SELECT IS_ROLEMEMBER(N'db_owner',N'vis01_readwrite_test')+IS_ROLEMEMBER(N'db_datareader',N'vis01_readwrite_test')+IS_ROLEMEMBER(N'db_datawriter',N'vis01_readwrite_test');")
assert re.search(r"(?m)^\s*0\s*$",p),p
print("PATHB_VIS01_MINIMAL_RUNTIME_ACL_SQLSERVER=PASS")
print("AZURE_EFFECTIVE_RUNTIME=NOT_TESTED")
