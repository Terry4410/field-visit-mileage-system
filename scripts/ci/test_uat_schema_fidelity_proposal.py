from __future__ import annotations

import hashlib
import importlib.util
import json
import re
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SQL = ROOT / "docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql"
WORKFLOW = ROOT / ".github/workflows/azure-sql-uat-schema-fidelity-readonly.yml"
VALIDATOR = ROOT / "scripts/ci/uat_schema_fidelity_evidence.ps1"
POWERSHELL_TEST = ROOT / "scripts/ci/test_uat_schema_fidelity_evidence.ps1"
INVOKE_SQLCMD_TEST = ROOT / "scripts/ci/test_uat_schema_fidelity_invoke_sqlcmd.ps1"
COMPARATOR = ROOT / "scripts/ci/compare_uat010_schema_inventory.py"
REGISTRATION_STUB = ROOT / "docs/workflows/azure-sql-uat-schema-fidelity-readonly.main-registration-stub.yml"
R2_DIAGNOSTIC = ROOT / "docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.r2-unknown-failure-diagnostic.md"
SQL_SHA = "67551978bf16266ef8f38c018be87b7aadc321ee3db84d6d94b52a1538204734"
VALIDATOR_SHA = "5b71d40b58c6119fc1d694f3b934f89745ee4633c873da8dfd50fa1bd3309653"


def load_comparator():
    spec = importlib.util.spec_from_file_location("schema_compare", COMPARATOR)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sample_inventory() -> dict:
    return {
        "ContractVersion": "uat-schema-fidelity-v1",
        "SourceSha": "a" * 40,
        "SqlSha256": SQL_SHA,
        "Target": {"ServerName": "sql-fieldvisit-jpe-uat", "DatabaseName": "db-fieldvisit-uat"},
        "SchemaVersion": {"Count": 10, "Latest": "1.8.0-010"},
        "Columns": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts", "Ordinal": 1,
            "ColumnName": "RouteCalculationAttemptId", "SqlType": "bigint",
            "MaxLengthBytesOrCharacters": 8, "NumericPrecision": 19, "NumericScale": 0,
            "IsNullable": False, "IsIdentity": True, "IsComputed": False,
            "IsRowVersion": False, "DefaultDefinition": None,
        }],
        "Indexes": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts", "IndexName": "PK_RouteCalculationAttempts",
            "IndexType": "CLUSTERED", "IsPrimaryKey": True, "IsUniqueConstraint": False,
            "IsUnique": True, "IsDisabled": False, "HasFilter": False, "FilterDefinition": None,
            "KeyOrdinal": 1, "IsIncluded": False, "ColumnName": "RouteCalculationAttemptId", "IsDescending": False,
        }],
        "ForeignKeys": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts", "ForeignKeyName": "FK_RouteCalculationAttempts_VisitTrips",
            "ParentColumn": "VisitTripId", "ReferencedSchema": "dbo", "ReferencedTable": "VisitTrips",
            "ReferencedColumn": "VisitTripId", "ColumnOrdinal": 1, "DeleteAction": "NO_ACTION",
            "UpdateAction": "NO_ACTION", "IsDisabled": False, "IsNotTrusted": False,
        }],
        "Checks": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts",
            "CheckConstraintName": "CK_RouteCalculationAttempts_Reason",
            "Definition": "CalculationReason IN ('VisitorCalculate','LeaderRetry','CorrectionRecalculate','BackgroundMileageJob')",
            "IsDisabled": False, "IsNotTrusted": False,
        }],
        "HistoricalAggregates": [],
        "CapturedUtc": "2026-10-16T00:00:00Z",
    }


class SchemaFidelityProposalTests(unittest.TestCase):
    def test_sql_is_hash_pinned_exact_target_and_select_only(self):
        source = SQL.read_bytes()
        self.assertEqual(hashlib.sha256(source).hexdigest(), SQL_SHA)
        executable = re.sub(rb"/\*.*?\*/|--[^\n]*", b"", source, flags=re.S).upper()
        for token in (
            b" INSERT ", b" UPDATE ", b" DELETE ", b" MERGE ", b" CREATE ", b" ALTER ",
            b" DROP ", b" TRUNCATE ", b" EXEC ", b" EXECUTE ", b" RESTORE ", b" BACKUP ",
            b" OPENROWSET ", b" OPENDATASOURCE ", b" SELECT *",
        ):
            self.assertNotIn(token, b" " + executable + b" ")
        for required in (b"SYS.COLUMNS", b"SYS.INDEXES", b"SYS.FOREIGN_KEYS", b"SYS.CHECK_CONSTRAINTS", b"IS_NOT_TRUSTED"):
            self.assertIn(required, executable)
        for sensitive in (b"ORIGINAL_LOGIN", b"SUSER_SNAME", b"SYSTEM_USER", b"CURRENT_USER", b"APPLIEDBY"):
            self.assertNotIn(sensitive, executable)
        for table in (b"VISITTRIPS", b"VISITTRIPSTOPS", b"VISITTRIPSNAPSHOTS", b"VISITTRIPSNAPSHOTSTOPS"):
            self.assertIn(b"HAS_PERMS_BY_NAME(N'DBO." + table + b"',N'OBJECT',N'SELECT')", executable)
        self.assertIn(b"THROW 51993", executable)
        self.assertIn(b"HISTORICAL_SELECT_OR_OBJECT_UNAVAILABLE", executable)

    def test_inventory_references_match_protected_001_010_and_ef_model(self):
        migrations = [
            ROOT / f"database/migrations/1800_{number:03d}_{name}/Up.sql"
            for number, name in (
                (1, "organization_center_team_lifecycle"),
                (2, "person_employment_role_membership"),
                (3, "deployment_sites"),
                (4, "location_governance"),
                (5, "project_visit_rate_lifecycle"),
                (6, "notification_framework"),
                (7, "mileage_google_governance"),
                (8, "rate_manual_effective_dates"),
                (9, "one_stop_route_attempt_constraint"),
                (10, "background_mileage_reason_constraint"),
            )
        ]
        self.assertTrue(all(path.is_file() for path in migrations))
        for number, path in enumerate(migrations, start=1):
            self.assertIn(f"1.8.0-{number:03d}", path.read_text(encoding="utf-8-sig"))

        migration_001 = migrations[0].read_text(encoding="utf-8-sig")
        for token in ("VisitTripId", "VisitTripSnapshotId", "VisitTripSnapshotStopId"):
            self.assertIn(token, migration_001)
        ef_model = (ROOT / "backend/src/FieldVisit.Infrastructure/AppDbContext.cs").read_text(encoding="utf-8")
        for token in ("VisitTripId", "VisitTripStopId", "VisitTripSnapshotId", "VisitTripSnapshotStopId"):
            self.assertIn(token, ef_model)
        migration_010 = migrations[9].read_text(encoding="utf-8-sig")
        self.assertIn("CK_RouteCalculationAttempts_Reason", migration_010)
        self.assertIn("BackgroundMileageJob", migration_010)

    def test_workflow_is_fixed_target_hash_pinned_and_fail_closed(self):
        workflow = (WORKFLOW.read_text(encoding="utf-8") + (ROOT / ".github/workflows/r3-private-uat-execution.yml").read_text())
        self.assertIn("refs/heads/post-uat/v1.8.0", workflow)
        self.assertIn("ref: ${{ github.sha }}", workflow)
        self.assertIn("test \"$(git rev-parse HEAD)\" = \"${GITHUB_SHA}\"", workflow)
        self.assertIn("$invoke.ApplicationIntent = 'ReadOnly'", (ROOT / "scripts/ci/uat_schema_r3.ps1").read_text())
        self.assertIn("environment: uat", workflow)
        self.assertIn("id-token: write", workflow)
        self.assertIn(SQL_SHA, workflow)
        self.assertIn(VALIDATOR_SHA, workflow)
        # Inputs now bind exact Owner SHA and separately gate billable connection-only wake.
        for required in ('approved_source_sha:', 'authorize_coldstart:', 'accept_compute_charges:', 'default: false'):
            self.assertIn(required, workflow)
        self.assertNotIn("database/migrations/", workflow)
        self.assertNotIn("dotnet ef database update", workflow.lower())

    def test_dry_routing_default_main_refuses_and_protected_runs_guard_first(self):
        stub = REGISTRATION_STUB.read_text(encoding="utf-8")
        protected = (WORKFLOW.read_text(encoding="utf-8") + (ROOT / ".github/workflows/r3-private-uat-execution.yml").read_text())
        expected_name = "name: Azure SQL UAT schema fidelity inventory (SELECT-only)"
        self.assertIn(expected_name, stub)
        self.assertIn(expected_name, protected)
        self.assertIn("workflow_dispatch:", stub)
        self.assertIn("contents: read", stub)
        self.assertIn("exit 1", stub)
        for forbidden in ("id-token: write", "environment: uat", "azure/login", "Invoke-Sqlcmd", "AZURE_CLIENT_ID"):
            self.assertNotIn(forbidden, stub)
        self.assertIn("needs: branch-guard", protected)
        self.assertIn('test "${GITHUB_REF}" = "refs/heads/post-uat/v1.8.0"', protected)
        self.assertLess(protected.index("branch-guard:"), protected.index("metadata-only-inventory:"))

    def test_powershell_contract_suite_covers_seven_result_sets_and_negative_cases(self):
        source = POWERSHELL_TEST.read_text(encoding="utf-8")
        for required in (
            "valid seven-result-set DataSet normalizes",
            "wrong server identity fails",
            "unexpected extra column fails",
            "wrong schema version fails",
            "missing historical aggregate fails",
            "missing seventh result set fails",
            "SQL failure output suppresses raw secret",
            "real SqlException signals map only to allow-listed diagnostics",
            "approved wrapper chain reaches provider signal within three levels",
            "unknown and over-depth wrappers stay suppressed",
            "wrong evidence mode fails",
        ):
            self.assertIn(required, source)
        self.assertIn("New-ValidInventoryDataSet", source)
        self.assertIn("Get-SanitizedUatInventoryFailure", source)
        self.assertIn("System.Management.Automation.ErrorRecord", source)
        self.assertIn("wrapped unapproved SQL number was not suppressed", source)

    def test_wrapper_traversal_is_exact_allowlisted_bounded_and_fail_closed(self):
        source = VALIDATOR.read_text(encoding="utf-8")
        for wrapper in (
            "Microsoft.SqlServer.Management.PowerShell.SqlPowerShellSqlExecutionException",
            "System.Management.Automation.RuntimeException",
            "System.Reflection.TargetInvocationException",
        ):
            self.assertIn(wrapper, source)
        self.assertIn("$item.Depth -ge 3", source)
        self.assertIn("$approvedWrappers -notcontains $exception.GetType().FullName", source)
        self.assertIn("$Failure -is [System.Exception]", source)
        self.assertNotIn("PSObject.Properties.Name", source)

    def test_disposable_invoke_sqlcmd_tests_are_runner_local_and_nondeploy(self):
        source = INVOKE_SQLCMD_TEST.read_text(encoding="utf-8")
        workflow = ((ROOT / ".github/workflows/uat-schema-fidelity-proposal-nondeploy.yml").read_text(encoding="utf-8") + (ROOT / ".github/workflows/r3-recovery-nondeploy-worker.yml").read_text())
        for required in (
            "localhost,1433", "master", "SqlServer -RequiredVersion 22.4.5.1",
            "AbortOnError = $true", "OutputSqlErrors = $false", "-OutputAs DataSet",
            "SqlPowerShellSqlExecutionException", "unsupported-provider-code",
        ):
            self.assertIn(required, source)
        for forbidden in (
            "sql-fieldvisit-jpe-uat", "db-fieldvisit-uat", "AccessToken", "azure/login",
            "INSERT ", "UPDATE ", "DELETE ", "MERGE ", "CREATE ", "ALTER ", "DROP ",
            "RESTORE ", "BACKUP ", "upload-artifact",
        ):
            self.assertNotIn(forbidden, source)
        self.assertIn("mcr.microsoft.com/mssql/server:2022-latest", workflow)
        self.assertNotIn("id-token: write", workflow)
        self.assertNotIn("environment: uat", workflow)

    def test_r2_diagnostic_plan_is_staged_and_requires_new_authorization(self):
        source = R2_DIAGNOSTIC.read_text(encoding="utf-8")
        for stage in ("IdentityVersion", "Catalog", "HistoricalAccess", "HistoricalAggregates"):
            self.assertIn(stage, source)
        self.assertIn("distinct `Invoke-Sqlcmd` call", source)
        self.assertIn("R2 authorization is consumed", source)
        self.assertIn("separately reviewed, hash-pinned stage SQL", source)

    def test_validator_is_hash_pinned_and_exact_shape(self):
        source = VALIDATOR.read_bytes()
        self.assertEqual(hashlib.sha256(source).hexdigest(), VALIDATOR_SHA)
        text = source.decode("utf-8")
        self.assertIn("$tables.Count -ne 7", text)
        self.assertIn("result columns were missing, reordered or unexpected", text)
        self.assertIn("1.8.0-010", text)
        self.assertIn("METADATA_ONLY_NO_ROW_VALUES", text)
        self.assertIn("raw SQL error suppressed", text)
        for classification in (
            "TargetGuardRejected", "RequiredObjectMissing", "HistoricalSelectOrObjectUnavailable",
            "SqlPermissionDenied", "MissingObjectOrColumn", "SqlSyntaxOrCompatibility",
            "AuthenticationOrDatabaseAccess", "TimeoutOrTransient", "ConnectivityFailure",
            "UnclassifiedSqlFailure",
        ):
            self.assertIn(classification, text)
        self.assertIn("Invoke-R3Diagnostic", (WORKFLOW.read_text(encoding="utf-8") + (ROOT / ".github/workflows/r3-private-uat-execution.yml").read_text()))
        self.assertIn("Get-SanitizedUatInventoryFailure", (ROOT / "scripts/ci/uat_schema_r3.ps1").read_text())

    def test_comparator_detects_full_facet_and_010_drift(self):
        comparator = load_comparator()
        expected = sample_inventory()
        actual = json.loads(json.dumps(expected))
        self.assertEqual(comparator.assert_010_contract(actual), [])
        self.assertEqual(comparator.compare(expected, actual), [])
        actual["Columns"][0]["IsNullable"] = True
        drift = comparator.compare(expected, actual)
        self.assertEqual(len(drift), 1)
        self.assertIn("IsNullable", drift[0])
        actual = json.loads(json.dumps(expected))
        actual["Checks"][0]["IsNotTrusted"] = True
        self.assertIn("010: reason constraint is not trusted", comparator.assert_010_contract(actual))

    def test_comparator_requires_real_reference_and_actual_files(self):
        comparator = load_comparator()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "inventory.json"
            path.write_text(json.dumps(sample_inventory()), encoding="utf-8")
            loaded = comparator.load_inventory(path)
            self.assertEqual(loaded["SchemaVersion"]["Latest"], "1.8.0-010")


if __name__ == "__main__":
    unittest.main(verbosity=2)
