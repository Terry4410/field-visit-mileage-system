from pathlib import Path
import unittest

WORKFLOW = Path(".github/workflows/clean-build-handover-preparation-nondeploy.yml")

class CleanInstallWorkflowPrivacyTests(unittest.TestCase):
    def test_sql_password_is_run_scoped_masked_and_cleaned(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertNotIn("    services:", text)
        self.assertNotIn("MSSQL_SA_PASSWORD: ", text)
        self.assertNotIn('CLEAN_INSTALL_SQL_PASSWORD: "', text)
        self.assertIn("secrets.token_hex(24)", text)
        self.assertIn("::add-mask::", text)
        self.assertIn("docker run --detach", text)
        self.assertIn("--publish 127.0.0.1:1433:1433", text)
        self.assertIn("MSSQL_SA_PASSWORD=$sql_password", text)
        self.assertIn("docker rm --force fieldvisit-clean-install-sql", text)
        self.assertIn("CLEAN_INSTALL=DISPOSABLE_SQL2022_CONTAINER_CLEANUP_PASS", text)

if __name__ == "__main__":
    unittest.main()
