"""Offline safety/shape regression for one-use UAT010 SQL read. No Azure/SQL calls."""
import pathlib, re, unittest
ROOT = pathlib.Path(__file__).resolve().parents[2]
SQL = (ROOT / 'scripts/ci/g1b_uat010_metadata_once.sql').read_text()
PS = (ROOT / 'scripts/ci/g1b_uat010_metadata_once.ps1').read_text()
class Safety(unittest.TestCase):
    def test_query_has_no_write_verbs(self):
        s = re.sub(r'--[^\n]*', '', SQL)
        s = re.sub(r"N?'(?:''|[^'])*'", "''", s)
        self.assertNotRegex(s, r'\b(?:INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC(?:UTE)?|GRANT|REVOKE|DENY|OPENROWSET|WAITFOR|xp_cmdshell)\b')
    def test_query_has_fixed_target(self):
        self.assertIn("N'db-fieldvisit-uat'", SQL)
        self.assertIn("N'sql-fieldvisit-jpe-uat'", SQL)
    def test_query_has_no_personal_rows(self):
        self.assertNotIn('SELECT *', SQL)
        self.assertNotRegex(SQL, r'(?i)\bFROM\s+dbo\.(?!SchemaVersions)')
    def test_query_has_004_and_007_triggers(self):
        for p in ('TR_Locations_ProtectCurrentDeploymentSiteLocations','TR_DeploymentSiteLocationAssignments_ProtectActiveLocation','TR_Locations_SelectedGeocodingAttempt','TR_MileageCalculations_DecisionEvidence'):
            self.assertIn(p, SQL)
    def test_query_checks_catalog_states(self):
        for p in ('is_disabled=0','is_not_trusted=0','OBJECT_DEFINITION','sys.trigger_events'):
            self.assertIn(p, SQL)
    def test_query_never_claims_api_identity(self):
        self.assertIn("(N'ACTUAL_API_PRINCIPAL',N'UNKNOWN')", SQL)
    def test_query_never_claims_full_parity(self):
        self.assertIn("(N'FULL_010_PARITY',N'UNKNOWN')", SQL)
    def test_one_connection_only(self):
        self.assertEqual(PS.count('$conn.Open()'), 1)
        self.assertEqual(PS.count('$cmd.ExecuteReader()'), 1)
        self.assertIn("'Pooling'] = $false", PS)
        self.assertIn("'ConnectRetryCount'] = 0", PS)
    def test_time_bounds(self):
        self.assertIn("'Connect Timeout'] = 120", PS)
        self.assertIn('$cmd.CommandTimeout = 30',PS)
    def test_no_raw_exception(self):
        self.assertNotIn('Write-Error $_', PS)
        self.assertNotIn('Write-Output $_', PS)
        self.assertIn('Details=SUPPRESSED', PS)
    def test_output_enum(self):
        self.assertIn('status -cnotin',PS)
        self.assertIn("'PASS','FAIL','UNKNOWN'",PS)
    def test_no_privileged_auth(self):
        self.assertNotIn('password',PS.lower())
        self.assertIn('az account get-access-token', PS)
    def test_source_sha_guard(self):
        self.assertIn('$env:G1B_EXPECTED_SOURCE -cne $env:GITHUB_SHA', PS)
if __name__=='__main__': unittest.main()
