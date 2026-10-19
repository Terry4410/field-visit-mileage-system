"""R3 fail-closed source contracts; SQL execution proof comes from the separate real-provider suite."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'scripts/ci/uat_schema_r3.hashes.json'
STAGES = ('IdentityVersion','Catalog','HistoricalAccess','HistoricalAggregates')
SQL_ROOT = 'docs/release/optionc-canonical-sql-draft/full-scheme-a/r3/'

def verify_hashes():
    manifest = json.loads(MANIFEST.read_text())
    expected = {SQL_ROOT+s+'.sql' for s in STAGES} | {'scripts/ci/uat_schema_r3.ps1','scripts/ci/uat_schema_fidelity_evidence.ps1'}
    assert set(manifest) == expected, 'reviewed file set mismatch'
    for relative, digest in manifest.items():
        assert re.fullmatch('[0-9a-f]{64}', digest), 'digest malformed'
        assert hashlib.sha256((ROOT / relative).read_bytes()).hexdigest() == digest, 'reviewed hash mismatch'
    workflow = (ROOT / '.github/workflows/azure-sql-uat-schema-fidelity-readonly.yml').read_text()
    assert hashlib.sha256(MANIFEST.read_bytes()).hexdigest() in workflow, 'manifest workflow pin mismatch'

class R3Contracts(unittest.TestCase):
    def test_exact_reviewed_hashes(self):
        verify_hashes()
        first = hashlib.sha256((ROOT / (SQL_ROOT+'IdentityVersion.sql')).read_bytes()).hexdigest()
        self.assertIn("$hash = '"+first+"'", (ROOT/'scripts/ci/uat_schema_r3.ps1').read_text())
    def test_select_only_no_sensitive_output(self):
        for stage in STAGES:
            sql = (ROOT / (SQL_ROOT+stage+'.sql')).read_text()
            sql = re.sub(r'/\*.*?\*/|--[^\n]*','',sql, flags=re.S)
            for forbidden in ('INSERT','UPDATE','DELETE','MERGE','CREATE','ALTER','DROP','TRUNCATE','EXEC','EXECUTE','RESTORE','BACKUP','OPENROWSET','OPENDATASOURCE','INTO','PRINT','RAISERROR','ORIGINAL_LOGIN','SUSER_SNAME','SYSTEM_USER','CURRENT_USER','APPLIEDBY'):
                self.assertNotRegex(sql.upper(), r'\b'+forbidden+r'\b')
            for guard in ('SERVERPROPERTY', 'DB_NAME()', 'Latin1_General_100_BIN2', '1.8.0-010','AppliedAt','THROW 51992','StagePass'):
                self.assertIn(guard,sql)
            self.assertNotIn('SELECT *',sql.upper())
    def test_runner_is_sequential_pinned_bounded_and_no_inventory(self):
        text=(ROOT/'scripts/ci/uat_schema_r3.ps1').read_text()
        self.assertIn("@('IdentityVersion','Catalog','HistoricalAccess','HistoricalAggregates')",text)
        self.assertEqual(text.count('Invoke-Sqlcmd @invoke'),1)
        self.assertNotRegex(text,r'(?i)retry|Start-Sleep|readonly\.sql|Write-UatSchemaInventory')
        for required in ('ConnectionTimeout = 10','QueryTimeout = 30','OutputSqlErrors = $false',"throw 'sql-stop'",'DataType -ne [int]','Get-SanitizedUatInventoryFailure','Get-FileHash','ManifestHash','ReadOnly'):
            self.assertIn(required,text)
        for line in text.splitlines():
            if 'Write-Output ' in line:
                self.assertTrue('Stage=$stage; Status=' in line)
                self.assertNotIn('$_',line)
                self.assertNotIn('$data',line)
    def test_future_workflow_guard_and_no_full_inventory(self):
        text=(ROOT/'.github/workflows/azure-sql-uat-schema-fidelity-readonly.yml').read_text()
        self.assertIn('needs: branch-guard',text)
        self.assertIn('refs/heads/post-uat/v1.8.0',text)
        self.assertIn('test "$(git rev-parse HEAD)" = "${GITHUB_SHA}"',text)
        self.assertIn('environment: uat',text)
        self.assertEqual(text.count('id-token: write'),1)
        self.assertLess(text.index('Verify R3 manifest'),text.index('uses: azure/login'))
        self.assertNotIn('InputFile',text)
        self.assertNotIn('Write-UatSchemaInventory',text)
        self.assertNotIn('upload-artifact',text)
        self.assertIn('Invoke-R3Diagnostic',text)
    def test_proposal_ci_no_protected_credentials(self):
        text=(ROOT/'.github/workflows/uat-schema-fidelity-proposal-nondeploy.yml').read_text()
        for forbidden in ('azure/login','id-token: write','environment: uat','secrets.','vars.AZURE','pull_request_target','workflow_run:'):
            self.assertNotIn(forbidden,text)
        self.assertIn('mcr.microsoft.com/mssql/server:2022-latest',text)
        self.assertIn('test_uat_schema_r3.ps1',text)
        self.assertIn('ref: ${{ github.sha }}',text)
    def test_accepted_inventory_and_classifier_bytes_unchanged(self):
        for relative in ('scripts/ci/uat_schema_fidelity_evidence.ps1','docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql','scripts/ci/test_uat_schema_fidelity_evidence.ps1','scripts/ci/test_uat_schema_fidelity_invoke_sqlcmd.ps1'):
            original=subprocess.check_output(['git','show','b1a507c90c577fb2c0cdd6a98ef33481d3a5f790:'+relative],cwd=ROOT)
            self.assertEqual(original,(ROOT/relative).read_bytes())

if __name__ == '__main__':
    if '--verify-hashes' in sys.argv:
        try: verify_hashes()
        except Exception: raise SystemExit('STOP: reviewed R3 hashes rejected.') from None
    else: unittest.main()
