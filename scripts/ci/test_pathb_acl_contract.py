"""Safe test-harness contract, not live Azure permissions."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/ci/test_pathb_acl_sqlserver.sh"
SCHEMA = ROOT / "scripts/ci/pathb_acl_behavior.sql"
TABLES = ("Organizations","Roles","Teams","UserEmploymentPeriods","UserIdentityProfiles","UserRoleAssignments","Users","UserTeamAssignments")

class Contract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sh = SCRIPT.read_text()
        cls.sql = SCHEMA.read_text()

    def test_disposable_database_only(self):
        self.assertIn("PathBACLDisposable", self.sh)
        for forbidden in ("db-fieldvisit-pathb-uat","sql-fieldvisit-jpe-pathb-uat","api-fieldvisit-pathb-uat_pathb"):
            self.assertNotIn(forbidden, self.sh)

    def test_eight_named_objects(self):
        for name in TABLES:
            self.assertIn("CREATE TABLE dbo." + name, self.sql)
            self.assertGreaterEqual(self.sh.count("GRANT SELECT ON OBJECT::dbo." + name + " TO [pathb_login_test]"), 1)

    def test_negative_and_transaction_rollback(self):
        for term in ("expect_sql_error","NotAnExistingTable","ROLLBACK TRANSACTION","pathb_outsider_test","DENY SELECT","SecretTest"):
            self.assertIn(term,self.sh)

    def test_no_prod_identity_or_network(self):
        for term in ("az sql","azure login","Invoke-WebRequest","curl ","CREATE USER FROM EXTERNAL PROVIDER"):
            self.assertNotIn(term,self.sh)
        self.assertIn("PATHB_ACL_DISPOSABLE_SQLSERVER_BEHAVIOR=PASS",self.sh)

if __name__ == "__main__":
    unittest.main()
