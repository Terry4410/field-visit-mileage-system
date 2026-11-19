"""Path B VIS01 optional reservation minimal column ACL, disposable SQL2022 only.
Independent of Azure and any live identity. Exercise the *actual* approved-location
subquery used by V180OptionCCreateReservationGuard and negative privileges.
"""
import os
import subprocess

PASSWORD = os.environ["PATHB_SQL_PASSWORD"]
DB = "PathBSeedCI_Positive"

def call(sql, should_pass=True):
    p = subprocess.run(
        ["docker", "exec", "-i", "pathb-seed-sql",
         "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa",
         "-P", PASSWORD, "-C", "-I", "-b", "-V", "11", "-d", DB],
        input=sql, text=True, capture_output=True)
    output=p.stdout+p.stderr
    if (p.returncode==0) != should_pass:
        raise AssertionError("UNEXPECTED_SQL_RESULT: "+output[-2000:])
    return output

# This follows the pre-existing VIS01 scoped ACL test, using its synthetic
# WITHOUT LOGIN principals. Do not create or alter a live principal.
call("""CREATE TABLE dbo.LocationCreateReservations(
 LocationId int NOT NULL PRIMARY KEY,
 ReservationStatus nvarchar(30) NOT NULL,
 PrivatePayload nvarchar(50) NULL
);""")
call("INSERT dbo.SchemaVersions(VersionNumber) VALUES(N'1.8.0-CREATE-LOWRISK');")
call("INSERT dbo.LocationCreateReservations(LocationId,ReservationStatus,PrivatePayload) VALUES (1,N'Pending',N'private'),(2,N'Published',N'private');")

# Before narrow grants, visibility and the exact app SELECT must be denied.
guard="""SELECT CASE WHEN EXISTS(
 SELECT 1 FROM dbo.LocationCreateReservations cr
 JOIN dbo.Locations l ON l.LocationId=cr.LocationId
 WHERE cr.LocationId=1
   AND (cr.ReservationStatus<>N'Published'
     OR l.ApprovalStatus<>N'Approved' OR l.IsActive<>1)
) THEN 1 ELSE 0 END;"""
before=call("EXECUTE AS USER=N'vis01_readwrite_test';"+guard+"REVERT;", False)
assert "permission was denied" in before.lower(), before

# Exact two-column GRANT contract, no table-wide SELECT or broader roles.
grants="""BEGIN TRANSACTION;
GRANT SELECT ON OBJECT::dbo.[LocationCreateReservations] ([LocationId]) TO [vis01_readwrite_test];
GRANT SELECT ON OBJECT::dbo.[LocationCreateReservations] ([ReservationStatus]) TO [vis01_readwrite_test];
COMMIT TRANSACTION;"""
call(grants)
result=call("EXECUTE AS USER=N'vis01_readwrite_test';"+guard+"REVERT;")
assert "1" in result, result
call("EXECUTE AS USER=N'vis01_readwrite_test';SELECT LocationId,ReservationStatus FROM dbo.LocationCreateReservations;REVERT;")
for sql in (
 "SELECT PrivatePayload FROM dbo.LocationCreateReservations",
 "SELECT * FROM dbo.LocationCreateReservations",
 "UPDATE dbo.LocationCreateReservations SET ReservationStatus=N'Published'",
 "DELETE FROM dbo.LocationCreateReservations",
 "INSERT dbo.LocationCreateReservations(LocationId,ReservationStatus) VALUES(333,N'Published')"
):
    result=call("EXECUTE AS USER=N'vis01_readwrite_test';"+sql+";REVERT;", False)
    assert "permission was denied" in result.lower(), result
for sql in (
 "SELECT LocationId FROM dbo.LocationCreateReservations",
 "SELECT ReservationStatus FROM dbo.LocationCreateReservations",
):
    result=call("EXECUTE AS USER=N'vis01_outsider_test';"+sql+";REVERT;", False)
    assert "permission was denied" in result.lower(), result
count=call("""SET NOCOUNT ON;
SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.columns c
 ON c.object_id=p.major_id AND c.column_id=p.minor_id
WHERE p.grantee_principal_id=USER_ID(N'vis01_readwrite_test')
 AND p.major_id=OBJECT_ID(N'dbo.LocationCreateReservations')
 AND p.class=1 AND p.permission_name=N'SELECT' AND p.state=N'G'
 AND c.name IN (N'LocationId',N'ReservationStatus');""")
assert "2" in count, count
print("PATHB_VIS01_OPTIONAL_RESERVATION_TWO_COLUMN_ACL_SQLSERVER=PASS")
print("AZURE_EFFECTIVE_MI=NOT_TESTED")
