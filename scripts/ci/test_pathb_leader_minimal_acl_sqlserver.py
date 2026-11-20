"""Leader P0 least-privilege 3 grant regression on disposable SQL Server only."""
import pathlib
import subprocess

candidate=pathlib.Path("database/uat/PathB_UAT_P0_LeaderApproval_Minimal_3Grant_REVIEW_20261120.sql").read_text(encoding="utf-8")
assert candidate.count("GRANT SELECT ON OBJECT::dbo.[MileageRateRules] TO [api-fieldvisit-pathb-uat_pathb]")==1
assert candidate.count("GRANT INSERT ON OBJECT::dbo.[ApprovalRecords] TO [api-fieldvisit-pathb-uat_pathb]")==1
assert candidate.count("GRANT SELECT ON OBJECT::dbo.[ApprovalRecords] ([ApprovalRecordId])")==1
assert candidate.count("GRANT ")==3
assert "EXACT_PATHB_TARGET_HOLD" in candidate
assert "RELEVANT_DENY_HOLD" in candidate
assert "UNEXPECTED_EXISTING_GRANT_HOLD" in candidate
assert "CONCURRENT_PERMISSION_CHANGE_HOLD" in candidate
assert "UNEXPECTED_WHOLE_APPROVAL_SELECT_HOLD" in candidate
assert "ROLLBACK TRANSACTION" in candidate
assert "BUSINESS_APPROVAL_RUNTIME_REVALIDATION_REQUIRED" in candidate

import os
password=os.environ["PATHB_SQL_PASSWORD"]
def run(q,dbname="master",ok=True):
    cmd=["docker","exec","-i","pathb-acl-sql","/opt/mssql-tools18/bin/sqlcmd","-S","localhost",
         "-U","sa","-P",password,"-C","-b","-V","11","-d",dbname,"-h","-1","-W"]
    p=subprocess.run(cmd,input=q,text=True,capture_output=True,timeout=80)
    if (p.returncode==0)!=ok:
        raise AssertionError("UNEXPECTED_OUTCOME "+(p.stdout+p.stderr)[-900:])
    return p.stdout+p.stderr

run("SET PARSEONLY ON;\n"+candidate)
target=run(candidate,ok=False)
assert "EXACT_PATHB_TARGET_HOLD" in target,target[-800:]

# Read original grant statements verbatim, changing grantee solely for isolated fixture.
start=candidate.index(" GRANT SELECT ON OBJECT::dbo.[MileageRateRules]")
end=candidate.index(" IF (SELECT COUNT(*) FROM sys.database_permissions",start)
grants=candidate[start:end].replace("[api-fieldvisit-pathb-uat_pathb]","[pathb_leader_acl_test]")
assert grants.count("GRANT ")==3
for db,collation in [("PathBLeaderAclA","SQL_Latin1_General_CP1_CI_AS"),
                      ("PathBLeaderAclB","Latin1_General_100_BIN2")]:
    run(f"CREATE DATABASE [{db}] COLLATE {collation};")
    try:
        run("""CREATE TABLE dbo.MileageRateRules
         (MileageRateRuleId int IDENTITY PRIMARY KEY,RatePerKm decimal(10,2) NOT NULL);
         CREATE TABLE dbo.ApprovalRecords
         (ApprovalRecordId bigint IDENTITY PRIMARY KEY,Action nvarchar(20) NOT NULL);
         CREATE USER [pathb_leader_acl_test] WITHOUT LOGIN;
         INSERT dbo.MileageRateRules(RatePerKm) VALUES (3.00);""",db)
        read="EXECUTE AS USER=N'pathb_leader_acl_test'; SELECT RatePerKm FROM dbo.MileageRateRules; REVERT;"
        add="""EXECUTE AS USER=N'pathb_leader_acl_test';
          INSERT dbo.ApprovalRecords(Action) OUTPUT INSERTED.ApprovalRecordId VALUES (N'Approved');
          REVERT;"""
        assert "permission was denied" in run(read,db,False).lower()
        assert "permission was denied" in run(add,db,False).lower()
        run(grants,db)
        assert "3.00" in run(read,db)
        assert "1" in run(add,db)
        assert "permission was denied" in run("""EXECUTE AS USER=N'pathb_leader_acl_test';
          SELECT Action FROM dbo.ApprovalRecords; REVERT;""",db,False).lower()
        row=run("""SELECT permission_name,minor_id FROM sys.database_permissions
         WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'pathb_leader_acl_test')
         AND major_id=OBJECT_ID(N'dbo.ApprovalRecords')
         ORDER BY permission_name;""",db)
        assert "INSERT" in row and "SELECT" in row
        print(f"COLLATION={collation} RATE_READ=PASS APPROVAL_INSERT_OUTPUT=PASS APPROVAL_WHOLE_TABLE_READ_DENIED=PASS")
    finally:
        run(f"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}];")
print("PATHB_LEADER_MINIMAL_3_GRANTS_DISPOSABLE_PASS AZURE_SQL_CALLS=0")
