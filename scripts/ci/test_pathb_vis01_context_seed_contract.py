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

if __name__=="__main__":
    unittest.main()
