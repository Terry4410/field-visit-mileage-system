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

    def test_sql_readiness_loop_has_bounded_success_and_failure_exits(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        start = text.index("      - name: Wait for isolated SQL Server 2022")
        end = text.index("      - name: Compile pinned SQL-only seven-table layer", start)
        block = text[start:end]
        self.assertIn("for i in $(seq 1 60); do", block)
        self.assertIn("grep -qx 'true'; then", block)
        self.assertIn("echo 'SQL2022_READY=PASS'", block)
        self.assertIn("sleep 2", block)
        self.assertIn("done", block)
        self.assertIn("echo 'SQL2022_READY=FAIL' >&2", block)
        self.assertEqual(text.count("- name: Compile pinned SQL-only seven-table layer (source contract, NONDEPLOY)"), 1)
        self.assertEqual(text.count("- uses: actions/upload-artifact@v4"), 1)
        self.assertNotIn("\n; then\n", text)

if __name__ == "__main__":
    unittest.main()
