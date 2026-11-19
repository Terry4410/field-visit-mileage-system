"""NONDEPLOY VIS01 synthetic context seed static contract; does NOT authorize live SQL."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SQL = (ROOT / "database/uat/FieldVisit_PathB_VIS01_Context_Seed_LOCKED_REVIEW.sql").read_text()

class Vis01Contract(unittest.TestCase):
    def test_locked_before_any_write(self):
        content=re.sub(r"(?m)^--.*\n","",SQL).strip()
        self.assertTrue(content.startswith("THROW 59500, 'LOCKED_REVIEW_ONLY_NOT_AUTHORIZED_FOR_SQL_WRITE', 1;"))

    def test_target_and_existing_seed(self):
        for term in ("db-fieldvisit-pathb-uat","sql-fieldvisit-jpe-pathb-uat",
                     "EmployeeNo=N'pilotv01'","OrganizationCode=N'UAT'","TeamCode=N'N01'",
                     "EmploymentId IS NULL","ENGINE", "EXACT_TARGET_HOLD"):
            if term=="ENGINE": continue
            self.assertIn(term,SQL)
        for term in ("CREATE USER","GRANT SELECT","DELETE FROM","Auth__DemoPassword"):
            self.assertNotIn(term,SQL)

    def test_delta_scope(self):
        allowed={"Persons","Employments","EmploymentStatusPeriods","TeamMemberships",
                 "Centers","TeamCenterAssignments","DeploymentSites","Locations",
                 "DeploymentSiteLocationAssignments","TeamDeploymentSiteAssignments",
                 "EmploymentDeploymentSiteAssignments","VisitTypes","Projects"}
        found=re.findall(r"INSERT dbo\.(\w+)\s*\(",SQL)
        self.assertEqual(set(found),allowed)
        self.assertEqual(len(found),14)
        self.assertEqual(len(re.findall(r"\bUPDATE dbo\.",SQL)),1)
        self.assertIn("UPDATE dbo.UserIdentityProfiles SET EmploymentId=@employment",SQL)

    def test_atomic_guards(self):
        for term in ("sp_getapplock","UPDLOCK,HOLDLOCK","BEGIN TRANSACTION",
                     "COMMIT TRANSACTION","ROLLBACK TRANSACTION",
                     "VIS01_NAMESPACE_COLLISION_HOLD","VIS01_CONTEXT_CHAIN_POSTCHECK_HOLD"):
            self.assertIn(term,SQL)


ACL = (ROOT / "database/uat/FieldVisit_PathB_VIS01_Context_SELECT_ACL_LOCKED_TEST.sql").read_text()
class Vis01AclContract(unittest.TestCase):
    def test_locked_ten_table_select_only(self):
        content = re.sub(r"(?m)^--.*\n", "", ACL).strip()
        self.assertTrue(content.startswith("THROW 59590, 'LOCKED_REVIEW_NO_AZURE_DCL_AUTHORIZATION', 1;"))
        expected = {"Employments","EmploymentStatusPeriods","TeamMemberships","TeamCenterAssignments",
            "EmploymentDeploymentSiteAssignments","TeamDeploymentSiteAssignments","DeploymentSites","Centers",
            "DeploymentSiteLocationAssignments","Locations"}
        found = re.findall(r"GRANT SELECT ON OBJECT::dbo\.(\w+) TO \[api-fieldvisit-pathb-uat_pathb\];", ACL)
        self.assertEqual(len(found), 10)
        self.assertEqual(set(found), expected)
        for banned in ("GRANT INSERT","GRANT UPDATE","GRANT DELETE","CREATE USER", "ALTER ROLE"):
            self.assertNotIn(banned, ACL)
    def test_fail_closed_binding_and_transaction(self):
        for token in ("API_MI_CLIENT_OR_OBJECT_SID_HOLD","TEN_EXACT_CONTEXT_TABLES_HOLD",
                      "PARTIAL_CONTEXT_GRANTS_HOLD_NO_WRITE","BROAD_RUNTIME_ROLE_HOLD",
                      "BEGIN TRANSACTION","COMMIT TRANSACTION","ROLLBACK TRANSACTION"):
            self.assertIn(token, ACL)


COMPLETE_ACL = (ROOT / "database/uat/FieldVisit_PathB_VIS01_Complete_Minimal_Runtime_ACL_LOCKED_TEST.sql").read_text()
class CompleteRuntimeAclContract(unittest.TestCase):
    def test_exact_minimum_grants(self):
        select_names = re.findall(r"GRANT SELECT ON OBJECT::dbo\.\[(\w+)\] TO \[api-fieldvisit-pathb-uat_pathb\];", COMPLETE_ACL)
        insert_names = re.findall(r"GRANT INSERT ON OBJECT::dbo\.\[(\w+)\] TO \[api-fieldvisit-pathb-uat_pathb\];", COMPLETE_ACL)
        reads = {"Employments","EmploymentStatusPeriods","TeamMemberships","TeamCenterAssignments",
           "EmploymentDeploymentSiteAssignments","TeamDeploymentSiteAssignments","DeploymentSites","Centers",
           "DeploymentSiteLocationAssignments","Locations","VisitTrips","VisitTripStops",
           "MileageCalculations","Projects","VisitTypes","SchemaVersions"}
        writes = {"VisitTrips","VisitTripStops","MileageCalculations","VisitTripStatusHistory","AuditLogs"}
        self.assertEqual(len(select_names),16)
        self.assertEqual(set(select_names),reads)
        self.assertEqual(len(insert_names),5)
        self.assertEqual(set(insert_names),writes)
    def test_locked_and_fail_closed(self):
        stripped = re.sub(r"(?m)^--.*\n", "", COMPLETE_ACL).strip()
        self.assertTrue(stripped.startswith("THROW 59590, 'LOCKED_REVIEW_NO_AZURE_DCL_AUTHORIZATION', 1;"))
        for token in ("EIGHTEEN_EXACT_VIS01_TABLES_HOLD","FIVE_EXACT_DRAFT_INSERT_TABLES_HOLD",
                      "PARTIAL_VIS01_GRANTS_HOLD_NO_WRITE","BEGIN TRANSACTION",
                      "COMMIT TRANSACTION","ROLLBACK TRANSACTION",
                      "API_MI_CLIENT_OR_OBJECT_SID_HOLD"):
            self.assertIn(token,COMPLETE_ACL)
        for forbidden in ("GRANT UPDATE","GRANT DELETE","GRANT CONTROL","ALTER ROLE",
                          "GRANT SELECT ON DATABASE","GRANT INSERT ON DATABASE"):
            self.assertNotIn(forbidden,COMPLETE_ACL)

if __name__=="__main__":
    unittest.main()
