#!/usr/bin/env python3
"""Fail-closed local governance validation for UAT migration stages 1800_002-007."""

from __future__ import annotations

import hashlib
import re
import subprocess
import sys
from pathlib import Path

try:
    import yaml
except ImportError as exc:  # pragma: no cover - explicit tooling prerequisite
    raise SystemExit("BLOCKED: PyYAML is required for YAML syntax validation") from exc


ROOT = Path(__file__).resolve().parents[1]
CONFIG = {
    "002": ("1800_002_person_employment_role_membership", "1.8.0-001", "1.8.0-002", {"UserIdentityProfiles"}),
    "003": ("1800_003_deployment_sites", "1.8.0-002", "1.8.0-003", set()),
    "004": ("1800_004_location_governance", "1.8.0-003", "1.8.0-004", set()),
    "005": (
        "1800_005_project_visit_rate_lifecycle",
        "1.8.0-004",
        "1.8.0-005",
        {"Projects", "VisitTypes", "MileageRateRules"},
    ),
    "006": ("1800_006_notification_framework", "1.8.0-005", "1.8.0-006", {"Employments"}),
    "007": ("1800_007_mileage_google_governance", "1.8.0-006", "1.8.0-007", {"MileageCalculations"}),
}

GOVERNANCE = ROOT / "docs" / "migrations" / "UAT-MIGRATION-1800-002-007-GOVERNANCE.md"
POST_UAT_VERIFY = ROOT / ".github" / "workflows" / "post-uat-v180-verify.yml"
RECOVERY_MARKERS = (
    "RECOVERY_MODEL=RESTORE_OR_REVIEWED_FORWARD_FIX",
    "PRE_STAGE_RESTORE_POINT=REQUIRED",
    "RECOVERY_OWNER=REQUIRED",
    "WRITE_QUIESCENCE=REQUIRED",
    "AUTO_RERUN=FORBIDDEN",
    "AD_HOC_ROLLBACK_SQL=FORBIDDEN",
    "IN_PLACE_SQL_REWRITE_AFTER_EXECUTION=FORBIDDEN",
    "POST_STAGE_STOP_FOR_REVIEW=REQUIRED",
)
RECOVERY_POLICY_REQUIREMENTS = (
    "This policy is mandatory for every stage 1800_002 through 1800_007.",
    "verified pre-stage Azure SQL restore/PITR capability",
    "recovery point exist;",
    "a Recovery Owner is identified;",
    "application writes are quiesced for the approved migration window;",
    "the exact predecessor SchemaVersion is verified;",
    "the historical fingerprint baseline is captured and verified; and",
    "no partial target-stage state exists.",
    "No stage may execute if this recovery gate is incomplete.",
    "If any stage fails, STOP immediately.",
    "Do not automatically rerun.",
    "manually rerun without a new Control Tower authorization.",
    "Do not edit the failed",
    "`Up.sql` in place.",
    "Do not bypass predecessor or clean-state checks.",
    "There is no approved `Down.sql` for 1800_002 through 1800_007.",
    "No improvised",
    "rollback SQL, temporary hand-written reverse SQL, destructive schema reversal,",
    "or manual cleanup intended to simulate `Down.sql` is permitted.",
    "Every recovery",
    "action requires explicit Control Tower review.",
    "#### Case A — transaction failed or rolled back; clean exact predecessor remains",
    "STOP and perform read-only verification.",
    "Confirm the exact predecessor SchemaVersion.",
    "Confirm that no partial objects, columns, or data state exists.",
    "Confirm that historical fingerprints are unchanged.",
    "Diagnose the failure.",
    "Control Tower may explicitly authorize a retry of the same immutable stage.",
    "Never retry automatically.",
    "#### Case B — stage committed and additive state is understood; Verify fails",
    "historical fingerprints remain intact.",
    "STOP, freeze",
    "writes, and do not use rollback SQL.",
    "new separately",
    "reviewed forward-fix migration with a new migration artifact/version,",
    "independent review, immutable SQL hashes, a protected execution mechanism, QA",
    "and governance review, and explicit Control Tower authorization.",
    "Do not edit or",
    "reuse the already executed stage SQL in place.",
    "#### Case C — fingerprint mismatch, destructive or ambiguous change, or unproven partial state",
    "STOP, freeze writes, do not retry, and do not forward-fix immediately.",
    "restore/PITR assessment",
    "use the verified pre-stage recovery point only when",
    "After restore, rerun read-only preflight and prove the exact clean",
    "predecessor before any new migration authorization.",
    "`Up → Verify → SchemaVersion confirmation → historical fingerprint verification → STOP_FOR_REVIEW`",
    "The next stage is never automatically authorized.",
)

STAGE_004_PERMISSION_SCRIPTS = {
    "grant": ROOT / "database" / "migrations" / "security" / "uat" / "Grant-gh-fieldvisit-uat-migrate-1800_004.sql",
    "verify": ROOT / "database" / "migrations" / "security" / "uat" / "Verify-gh-fieldvisit-uat-migrate-1800_004.sql",
    "revoke": ROOT / "database" / "migrations" / "security" / "uat" / "Revoke-gh-fieldvisit-uat-migrate-1800_004.sql",
}

STAGE_004_PARTIAL_MARKERS = (
    "object_id(n'dbo.teamlocationnotes',n'u')",
    "object_id(n'dbo.teamlocationnotehistory',n'u')",
    "col_length(n'dbo.locations',n'taxid')",
    "col_length(n'dbo.locations',n'masternote')",
    "col_length(n'dbo.locations',n'inactivatedat')",
    "col_length(n'dbo.locations',n'inactivatedbyuserid')",
    "col_length(n'dbo.locations',n'duplicateoflocationid')",
    "col_length(n'dbo.locations',n'duplicatereason')",
    "col_length(n'dbo.locations',n'normalizedlocationname')",
    "col_length(n'dbo.locations',n'normalizedaddress')",
    "object_id(n'dbo.tr_locations_protectcurrentdeploymentsitelocations',n'tr')",
    "object_id(n'dbo.tr_deploymentsitelocationassignments_protectactivelocation',n'tr')",
)

STAGE_004_INDEX_MARKERS = (
    ("dbo.locations", "ix_locations_organization_taxid"),
    ("dbo.locations", "ix_locations_normalizednameaddress"),
    ("dbo.locations", "ix_locations_duplicateof"),
    ("dbo.teamlocationnotes", "ix_teamlocationnotes_location_team"),
    ("dbo.teamlocationnotehistory", "ix_teamlocationnotehistory_note_changed"),
)

STAGE_005_PERMISSION_SCRIPTS = {
    "grant": ROOT / "database" / "migrations" / "security" / "uat" / "Grant-gh-fieldvisit-uat-migrate-1800_005.sql",
    "verify": ROOT / "database" / "migrations" / "security" / "uat" / "Verify-gh-fieldvisit-uat-migrate-1800_005.sql",
    "revoke": ROOT / "database" / "migrations" / "security" / "uat" / "Revoke-gh-fieldvisit-uat-migrate-1800_005.sql",
}

STAGE_005_PARTIAL_MARKERS = (
    "col_length(n'dbo.projects',n'inactivatedat')",
    "col_length(n'dbo.projects',n'inactivatedbyuserid')",
    "col_length(n'dbo.projects',n'rowversion')",
    "col_length(n'dbo.visittypes',n'inactivatedat')",
    "col_length(n'dbo.visittypes',n'inactivatedbyuserid')",
    "col_length(n'dbo.visittypes',n'rowversion')",
    "col_length(n'dbo.mileageraterules',n'createdbyuserid')",
    "col_length(n'dbo.mileageraterules',n'updatedbyuserid')",
    "col_length(n'dbo.mileageraterules',n'inactivatedat')",
    "col_length(n'dbo.mileageraterules',n'inactivatedbyuserid')",
    "col_length(n'dbo.mileageraterules',n'rowversion')",
    "object_id(n'dbo.tr_mileageraterules_protectseries',n'tr')",
)

STAGE_005_INDEX_MARKERS = (
    ("dbo.projects", "ix_projects_search"),
    ("dbo.visittypes", "ux_visittypes_visittypecode"),
    ("dbo.visittypes", "ix_visittypes_active_sort"),
    ("dbo.mileageraterules", "ux_mileageraterules_scope_vehicle_start"),
    ("dbo.mileageraterules", "ix_mileageraterules_asof"),
)

STAGE_005_FORBIDDEN_UPDATE_OBJECTS = {
    "Organizations",
    "Teams",
    "UserIdentityProfiles",
    "Locations",
    "DeploymentSiteLocationAssignments",
}

STAGE_006_PERMISSION_SCRIPTS = {
    "grant": ROOT / "database" / "migrations" / "security" / "uat" / "Grant-gh-fieldvisit-uat-migrate-1800_006.sql",
    "verify": ROOT / "database" / "migrations" / "security" / "uat" / "Verify-gh-fieldvisit-uat-migrate-1800_006.sql",
    "revoke": ROOT / "database" / "migrations" / "security" / "uat" / "Revoke-gh-fieldvisit-uat-migrate-1800_006.sql",
}

STAGE_006_PARTIAL_MARKERS = (
    "object_id(n'dbo.notificationenvironmentpolicies',n'u')",
    "object_id(n'dbo.notificationemailallowlist',n'u')",
    "object_id(n'dbo.notificationsettings',n'u')",
    "object_id(n'dbo.notificationsettingrecipients',n'u')",
    "object_id(n'dbo.mailoutbox',n'u')",
    "object_id(n'dbo.maildeliverylogs',n'u')",
    "col_length(n'dbo.employments',n'optionalemailnotificationenabled')",
    "col_length(n'dbo.employments',n'emailnotificationenabled')",
)

STAGE_006_FORBIDDEN_UPDATE_OBJECTS = {
    "Organizations",
    "Teams",
    "UserIdentityProfiles",
    "Locations",
    "DeploymentSiteLocationAssignments",
    "Projects",
    "VisitTypes",
    "MileageRateRules",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)



def validate_recovery_governance() -> None:
    text = GOVERNANCE.read_text(encoding="utf-8")

    for marker in RECOVERY_MARKERS:
        require(text.count(marker) == 1, f"recovery governance marker missing or duplicated: {marker}")

    for policy in RECOVERY_POLICY_REQUIREMENTS:
        require(policy in text, f"recovery governance policy missing: {policy}")

    required_headings = (
        "### Pre-stage recovery gate",
        "### Failure, rerun, and immutable-source rules",
        "### Recovery decision matrix",
        "### Mandatory post-stage stop",
    )
    for heading in required_headings:
        require(text.count(heading) == 1, f"recovery governance section missing or duplicated: {heading}")

    print("PASS RECOVERY_GOVERNANCE markers+gate+rerun+rollback+decision-matrix+STOP_FOR_REVIEW")


def validate_post_uat_hook() -> None:
    text = POST_UAT_VERIFY.read_text(encoding="utf-8")
    parsed = yaml.load(text, Loader=yaml.BaseLoader)
    require(isinstance(parsed, dict), "post-UAT verification: YAML root must be a mapping")

    steps = parsed.get("jobs", {}).get("verify", {}).get("steps", [])
    hook_steps = [
        step for step in steps
        if step.get("name") == "Recovery governance fail-closed validation"
    ]
    require(len(hook_steps) == 1, "post-UAT verification: recovery validator step missing or duplicated")
    run = hook_steps[0].get("run", "")
    require(
        run.count("python3 scripts/validate_uat_migration_workflows.py") == 1,
        "post-UAT verification: recovery validator invocation missing or duplicated",
    )
    require(
        "python3 -m pip install --disable-pip-version-check --no-input PyYAML==6.0.3" in run,
        "post-UAT verification: pinned PyYAML prerequisite missing",
    )

    for forbidden in ("azure/login@", "Invoke-Sqlcmd", "sqlcmd ", "az sql ", "-InputFile"):
        require(forbidden not in text, f"post-UAT verification: forbidden mutation/execution token: {forbidden}")

    print("PASS POST_UAT_VERIFY YAML+direct-validator-hook+repository-only")


def compact_sql(text: str) -> str:
    return re.sub(r"\s+", "", text.lower())


def validate_stage_004_permission_tooling() -> None:
    workflow_path = ROOT / ".github" / "workflows" / "azure-sql-uat-migration-1800-004.yml"
    workflow = workflow_path.read_text(encoding="utf-8")
    workflow_compact = compact_sql(workflow)
    parsed = yaml.load(workflow, Loader=yaml.BaseLoader)
    require(isinstance(parsed, dict), "004 workflow: YAML root must be a mapping")
    steps = parsed.get("jobs", {}).get("migrate-1800_004", {}).get("steps", [])
    require(isinstance(steps, list), "004 workflow: migration steps must be a list")

    def exact_step(name: str) -> dict[str, str]:
        matches = [step for step in steps if step.get("name") == name]
        require(len(matches) == 1, f"004 workflow: step missing or duplicated: {name}")
        return matches[0]

    def step_run(name: str) -> str:
        run = exact_step(name).get("run", "")
        require(isinstance(run, str) and run, f"004 workflow: step has no run block: {name}")
        return run

    def validate_token_masking(name: str, run: str) -> None:
        require(
            run.count("az account get-access-token") == 1,
            f"004 workflow: SQL step must obtain exactly one Azure SQL token: {name}",
        )
        require(
            run.count('Write-Output "::add-mask::$token"') == 1,
            f"004 workflow: token masking missing or duplicated: {name}",
        )

    def validate_timeout_profile(name: str, connection_timeout: int, query_timeout: int) -> str:
        run = step_run(name)
        invocations = [line.strip() for line in run.splitlines() if "Invoke-Sqlcmd" in line]
        require(
            len(invocations) == 1,
            f"004 workflow: expected exactly one Invoke-Sqlcmd in step: {name}",
        )
        invocation = invocations[0]
        require(
            re.findall(r"-ConnectionTimeout\s+(\d+)", invocation) == [str(connection_timeout)],
            f"004 workflow: incorrect ConnectionTimeout in step: {name}",
        )
        require(
            re.findall(r"-QueryTimeout\s+(\d+)", invocation) == [str(query_timeout)],
            f"004 workflow: incorrect QueryTimeout in step: {name}",
        )
        validate_token_masking(name, run)
        return run

    scripts: dict[str, str] = {}
    for kind, path in STAGE_004_PERMISSION_SCRIPTS.items():
        require(path.is_file(), f"004: {kind} permission script is missing")
        scripts[kind] = path.read_text(encoding="utf-8")

    grant = scripts["grant"]
    verify = scripts["verify"]
    revoke = scripts["revoke"]
    grant_compact = compact_sql(grant)

    for marker in STAGE_004_PARTIAL_MARKERS:
        require(marker in grant_compact, f"004 grant: partial-state marker missing: {marker}")
        require(marker in workflow_compact, f"004 workflow: partial-state marker missing: {marker}")

    for parent, index in STAGE_004_INDEX_MARKERS:
        exact_index_pattern = (
            rf"object_id=object_id\(n'{re.escape(parent)}',n'u'\)"
            rf"andname=n'{re.escape(index)}'"
        )
        require(re.search(exact_index_pattern, grant_compact) is not None,
                f"004 grant: exact parent+index marker missing: {parent}.{index}")
        require(re.search(exact_index_pattern, workflow_compact) is not None,
                f"004 workflow: exact parent+index marker missing: {parent}.{index}")

    require("grant update" not in grant.lower(), "004 grant: object UPDATE grant is forbidden")
    require("revoke update" not in revoke.lower(), "004 revoke: object UPDATE revoke is forbidden")
    require("grant update" not in verify.lower(), "004 verify: object UPDATE grant token is forbidden")

    required_grant_fragments = (
        "ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]",
        "GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]",
        "GRANT_PREPARED_FOR_1800_004",
    )
    for fragment in required_grant_fragments:
        require(fragment in grant, f"004 grant: required fragment missing: {fragment}")

    required_verify_fragments = (
        "1800_004_PERMISSION_GATE",
        "@CanAlterLocations <> 1",
        "@CanAlterDeploymentAssignments <> 1",
        "@CanReferenceUsers <> 1",
        "@CanReferenceLocations <> 1",
        "@CanReferenceTeams <> 1",
        "@CanUpdateLocations <> 0",
        "@CanUpdateOrganizations <> 0",
        "@CanUpdateTeams <> 0",
        "@CanUpdateStage002Profile <> 0",
    )
    for fragment in required_verify_fragments:
        require(fragment in verify, f"004 verify: required fail-closed gate missing: {fragment}")

    required_revoke_fragments = (
        "REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate]",
        "ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]",
        "REVOKED_1800_004_ELEVATION",
        "@CanUpdateLocations <> 0",
        "@CanUpdateOrganizations <> 0",
        "@CanUpdateTeams <> 0",
        "@CanUpdateStage002Profile <> 0",
    )
    for fragment in required_revoke_fragments:
        require(fragment in revoke, f"004 revoke: required fail-closed check missing: {fragment}")

    require("permission.permission_name=N'UPDATE'" not in workflow,
            "004 workflow: explicit permission allowlist must not permit UPDATE")
    for object_name in ("Locations", "Organizations", "Teams", "UserIdentityProfiles"):
        require(
            f"HAS_PERMS_BY_NAME(N'dbo.{object_name}',N'OBJECT',N'UPDATE')<>0" in workflow,
            f"004 workflow: effective UPDATE=0 gate missing for dbo.{object_name}",
        )

    for object_name in ("Locations", "DeploymentSiteLocationAssignments"):
        require(
            f"HAS_PERMS_BY_NAME(N'dbo.{object_name}',N'OBJECT',N'ALTER')<>1" in workflow,
            f"004 workflow: required ALTER gate missing for dbo.{object_name}",
        )
    for object_name in ("Users", "Locations", "Teams"):
        require(
            f"HAS_PERMS_BY_NAME(N'dbo.{object_name}',N'OBJECT',N'REFERENCES')<>1" in workflow,
            f"004 workflow: required REFERENCES gate missing for dbo.{object_name}",
        )

    warmup_name = "Warm up primary Azure SQL connectivity"
    preflight_name = "Validate predecessor, clean state, exact permissions, and capture fingerprints"
    up_name = "Apply only 1800_004 Up.sql"
    verify_name = "Run exact 1800_004 Verify.sql"
    history_name = "Revalidate frozen v1.7.2 historical SHA-256 fingerprints"
    final_name = "Confirm exact result, historical fingerprints, and STOP_FOR_REVIEW"
    expected_sql_steps = {
        warmup_name,
        preflight_name,
        up_name,
        verify_name,
        history_name,
        final_name,
    }

    warmup_step = exact_step(warmup_name)
    require(warmup_step.get("shell") == "pwsh", "004 workflow: warm-up shell must be exactly pwsh")
    warmup = validate_timeout_profile(warmup_name, 60, 60)

    query_matches = re.findall(r"(?m)^\s*\$query\s*=\s*'([^'\r\n]*)'\s*$", warmup)
    require(len(query_matches) == 1, "004 workflow: warm-up must have one literal $query assignment")
    normalized_query = re.sub(r"\s+", " ", query_matches[0]).strip()
    approved_query = (
        "SET NOCOUNT ON; SELECT DB_NAME() AS DatabaseName, "
        "USER_NAME() AS DatabasePrincipal;"
    )
    require(normalized_query == approved_query, "004 workflow: warm-up query is not the exact read-only query")
    require(
        not re.search(
            r"(?i)\b(?:INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|EXEC(?:UTE)?|FROM|JOIN)\b|dbo\.",
            normalized_query,
        ),
        "004 workflow: warm-up query contains business-table SQL, DDL, or DML",
    )
    for forbidden in ("-InputFile", "Up.sql", "Verify.sql"):
        require(forbidden not in warmup, f"004 workflow: warm-up contains forbidden token: {forbidden}")

    require(
        "if ($databaseName -ne 'db-fieldvisit-uat') { throw" in warmup,
        "004 workflow: warm-up exact database fail-closed comparison missing",
    )
    require(
        "if ($databasePrincipal -ne 'gh-fieldvisit-uat-migrate') { throw" in warmup,
        "004 workflow: warm-up exact principal fail-closed comparison missing",
    )
    require(warmup.count("$maxAttempts = 3") == 1,
            "004 workflow: warm-up maxAttempts must be exactly 3")
    require(warmup.count("$backoffSeconds = 5") == 1,
            "004 workflow: warm-up backoffSeconds must be exactly 5")
    require(
        len(re.findall(
            r"for\s*\(\s*\$attempt\s*=\s*1\s*;\s*\$attempt\s*-le\s*\$maxAttempts\s*;\s*\$attempt\+\+\s*\)",
            warmup,
        )) == 1,
        "004 workflow: warm-up retry loop must run through maxAttempts",
    )
    require(warmup.count("Start-Sleep -Seconds $backoffSeconds") == 1,
            "004 workflow: warm-up retry sleep is missing or duplicated")
    require(
        len(re.findall(
            r"if\s*\(\s*\$attempt\s*-ge\s*\$maxAttempts\s*\)\s*\{\s*throw\s*\}",
            warmup,
        )) == 1,
        "004 workflow: warm-up final failed attempt must throw",
    )

    validate_timeout_profile(preflight_name, 60, 60)
    validate_timeout_profile(up_name, 60, 600)
    validate_timeout_profile(verify_name, 60, 600)
    validate_timeout_profile(history_name, 60, 600)
    validate_timeout_profile(final_name, 60, 60)

    sql_step_names = {
        step.get("name")
        for step in steps
        if isinstance(step.get("run"), str) and "Invoke-Sqlcmd" in step["run"]
    }
    require(
        sql_step_names == expected_sql_steps,
        "004 workflow: SQL execution steps differ from the frozen connectivity contract",
    )
    require(re.search(r"(?i)\bApplicationIntent\b", workflow) is None,
            "004 workflow: ApplicationIntent must not be used")
    require(re.search(r"(?i)-ConnectionTimeout\s+15\b", workflow) is None,
            "004 workflow: all SQL connections must use ConnectionTimeout 60")

    print(
        "PASS 1800_004 permission-tooling+17-markers+negative-UPDATE+"
        "step-bound-connectivity-contract"
    )


def validate_stage_005_permission_tooling() -> None:
    workflow_path = ROOT / ".github" / "workflows" / "azure-sql-uat-migration-1800-005.yml"
    workflow = workflow_path.read_text(encoding="utf-8")
    workflow_compact = compact_sql(workflow)
    parsed = yaml.load(workflow, Loader=yaml.BaseLoader)
    require(isinstance(parsed, dict), "005 workflow: YAML root must be a mapping")
    steps = parsed.get("jobs", {}).get("migrate-1800_005", {}).get("steps", [])
    require(isinstance(steps, list), "005 workflow: migration steps must be a list")

    def exact_step(name: str) -> dict[str, str]:
        matches = [step for step in steps if step.get("name") == name]
        require(len(matches) == 1, f"005 workflow: step missing or duplicated: {name}")
        return matches[0]

    def step_run(name: str) -> str:
        run = exact_step(name).get("run", "")
        require(isinstance(run, str) and run, f"005 workflow: step has no run block: {name}")
        return run

    def validate_token_masking(name: str, run: str) -> None:
        require(
            run.count("az account get-access-token") == 1,
            f"005 workflow: SQL step must obtain exactly one Azure SQL token: {name}",
        )
        require(
            run.count('Write-Output "::add-mask::$token"') == 1,
            f"005 workflow: token masking missing or duplicated: {name}",
        )

    def validate_timeout_profile(name: str, connection_timeout: int, query_timeout: int) -> str:
        run = step_run(name)
        invocations = [line.strip() for line in run.splitlines() if "Invoke-Sqlcmd" in line]
        require(
            len(invocations) == 1,
            f"005 workflow: expected exactly one Invoke-Sqlcmd in step: {name}",
        )
        invocation = invocations[0]
        require(
            re.findall(r"-ConnectionTimeout\s+(\d+)", invocation) == [str(connection_timeout)],
            f"005 workflow: incorrect ConnectionTimeout in step: {name}",
        )
        require(
            re.findall(r"-QueryTimeout\s+(\d+)", invocation) == [str(query_timeout)],
            f"005 workflow: incorrect QueryTimeout in step: {name}",
        )
        validate_token_masking(name, run)
        return run

    scripts: dict[str, str] = {}
    for kind, path in STAGE_005_PERMISSION_SCRIPTS.items():
        require(path.is_file(), f"005: {kind} permission script is missing")
        scripts[kind] = path.read_text(encoding="utf-8")

    grant = scripts["grant"]
    verify = scripts["verify"]
    revoke = scripts["revoke"]
    grant_compact = compact_sql(grant)
    verify_compact = compact_sql(verify)
    revoke_compact = compact_sql(revoke)

    require(
        len(STAGE_005_PARTIAL_MARKERS) + len(STAGE_005_INDEX_MARKERS) == 17,
        "005 validator: frozen partial-state marker count must be exactly 17",
    )
    for marker in STAGE_005_PARTIAL_MARKERS:
        require(grant_compact.count(marker) == 1, f"005 grant: partial-state marker missing or duplicated: {marker}")
        require(workflow_compact.count(marker) == 1, f"005 workflow: partial-state marker missing or duplicated: {marker}")

    expected_column_markers = {
        (table, column)
        for table, column in re.findall(
            r"col_length\(n'dbo\.([^']+)',n'([^']+)'\)",
            "".join(STAGE_005_PARTIAL_MARKERS),
        )
    }
    for source_name, source in (("grant", grant_compact), ("workflow", workflow_compact)):
        actual_column_markers = set(
            re.findall(
                r"col_length\(n'dbo\.(projects|visittypes|mileageraterules)',n'([^']+)'\)",
                source,
            )
        )
        require(
            actual_column_markers == expected_column_markers,
            f"005 {source_name}: Stage 005 column-marker set is not exact",
        )

    for parent, index in STAGE_005_INDEX_MARKERS:
        exact_index_pattern = (
            rf"object_id=object_id\(n'{re.escape(parent)}',n'u'\)"
            rf"andname=n'{re.escape(index)}'"
        )
        require(len(re.findall(exact_index_pattern, grant_compact)) == 1,
                f"005 grant: exact parent+index marker missing or duplicated: {parent}.{index}")
        require(len(re.findall(exact_index_pattern, workflow_compact)) == 1,
                f"005 workflow: exact parent+index marker missing or duplicated: {parent}.{index}")

    for source_name, source in (("grant", grant_compact), ("workflow", workflow_compact)):
        actual_indexes = set(
            re.findall(
                r"object_id=object_id\(n'(dbo\.(?:projects|visittypes|mileageraterules))',n'u'\)"
                r"andname=n'([^']+)'",
                source,
            )
        )
        require(actual_indexes == set(STAGE_005_INDEX_MARKERS),
                f"005 {source_name}: Stage 005 parent+index marker set is not exact")
        actual_triggers = set(
            re.findall(r"object_id\(n'dbo\.(tr_[^']+)',n'tr'\)", source)
        )
        require(
            actual_triggers == {"tr_mileageraterules_protectseries"},
            f"005 {source_name}: Stage 005 trigger-marker set is not exact",
        )

    granted_update_matches = re.findall(
        r"GRANT\s+UPDATE\s+ON\s+OBJECT::dbo\.([A-Za-z0-9_]+)", grant, re.IGNORECASE
    )
    revoked_update_matches = re.findall(
        r"REVOKE\s+UPDATE\s+ON\s+OBJECT::dbo\.([A-Za-z0-9_]+)", revoke, re.IGNORECASE
    )
    required_update_objects = {"Projects", "VisitTypes", "MileageRateRules"}
    require(len(granted_update_matches) == 3 and set(granted_update_matches) == required_update_objects,
            "005 grant: exact UPDATE grant set must be {Projects, VisitTypes, MileageRateRules}")
    require(len(revoked_update_matches) == 3 and set(revoked_update_matches) == required_update_objects,
            "005 revoke: exact UPDATE revoke set must be {Projects, VisitTypes, MileageRateRules}")

    required_grant_fragments = (
        "SET XACT_ABORT ON",
        "BEGIN TRANSACTION",
        "ROLLBACK TRANSACTION",
        "ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]",
        "GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]",
        "GRANT UPDATE ON OBJECT::dbo.Projects TO [gh-fieldvisit-uat-migrate]",
        "GRANT UPDATE ON OBJECT::dbo.VisitTypes TO [gh-fieldvisit-uat-migrate]",
        "GRANT UPDATE ON OBJECT::dbo.MileageRateRules TO [gh-fieldvisit-uat-migrate]",
        "GRANT_PREPARED_FOR_1800_005",
        "latest SchemaVersion is not exact predecessor 1.8.0-004",
        "one or more of 17 Stage 005 partial-state markers exist",
        "pre-stage UPDATE capability residue exists",
    )
    for fragment in required_grant_fragments:
        require(fragment in grant, f"005 grant: required fail-closed fragment missing: {fragment}")

    required_verify_fragments = (
        "1800_005_PERMISSION_GATE",
        "@CanAlterProjects <> 1",
        "@CanAlterVisitTypes <> 1",
        "@CanAlterMileageRateRules <> 1",
        "@CanReferenceUsers <> 1",
        "@CanUpdateProjects <> 1",
        "@CanUpdateVisitTypes <> 1",
        "@CanUpdateMileageRateRules <> 1",
        "@CanUpdateOrganizations <> 0",
        "@CanUpdateTeams <> 0",
        "@CanUpdateUserIdentityProfiles <> 0",
        "@CanUpdateLocations <> 0",
        "@CanUpdateDeploymentAssignments <> 0",
    )
    for fragment in required_verify_fragments:
        require(fragment in verify, f"005 verify: required fail-closed gate missing: {fragment}")

    required_revoke_fragments = (
        "SET XACT_ABORT ON",
        "BEGIN TRANSACTION",
        "ROLLBACK TRANSACTION",
        "REVOKE UPDATE ON OBJECT::dbo.Projects FROM [gh-fieldvisit-uat-migrate]",
        "REVOKE UPDATE ON OBJECT::dbo.VisitTypes FROM [gh-fieldvisit-uat-migrate]",
        "REVOKE UPDATE ON OBJECT::dbo.MileageRateRules FROM [gh-fieldvisit-uat-migrate]",
        "REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate]",
        "ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]",
        "@CanInsertDboSchema <> 0",
        "@CanUpdateMileageRateRules <> 0",
        "@CanUpdateProjects <> 0",
        "@CanUpdateVisitTypes <> 0",
        "@CanUpdateOrganizations <> 0",
        "@CanUpdateTeams <> 0",
        "@CanUpdateUserIdentityProfiles <> 0",
        "@CanUpdateLocations <> 0",
        "@CanUpdateDeploymentAssignments <> 0",
        "REVOKED_1800_005_ELEVATION",
    )
    for fragment in required_revoke_fragments:
        require(fragment in revoke, f"005 revoke: required fail-closed check missing: {fragment}")
    require("SchemaVersions" not in revoke,
            "005 revoke: revoke must remain independently usable without a SchemaVersion gate")

    explicit_update_allowlist_matches = re.findall(
        r"object_name\(p\.major_id\)in\(([^)]+)\)andp\.permission_name=n'update'",
        verify_compact,
    )
    require(len(explicit_update_allowlist_matches) == 1,
            "005 verify: exact explicit UPDATE allowlist is missing or duplicated")
    explicit_update_allowlist = set(
        re.findall(r"n'([^']+)'", explicit_update_allowlist_matches[0])
    )
    require(explicit_update_allowlist == {"projects", "visittypes", "mileageraterules"},
            "005 verify: explicit UPDATE allowlist is not exact")
    for object_name in ("projects", "visittypes", "mileageraterules"):
        require(f"object_name(p.major_id)=n'{object_name}'" not in revoke_compact,
                f"005 revoke: CONNECT-only baseline check must not allow {object_name} UPDATE")

    required_update_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'update'\)<>1",
        workflow_compact,
    )
    forbidden_update_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'update'\)<>0",
        workflow_compact,
    )
    alter_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'alter'\)<>1",
        workflow_compact,
    )
    reference_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'references'\)<>1",
        workflow_compact,
    )
    require(
        len(required_update_matches) == 3
        and set(required_update_matches) == {"projects", "visittypes", "mileageraterules"},
        "005 workflow: required UPDATE set must be exactly {Projects, VisitTypes, MileageRateRules}",
    )
    require(
        len(forbidden_update_matches) == len(STAGE_005_FORBIDDEN_UPDATE_OBJECTS)
        and set(forbidden_update_matches) == {name.lower() for name in STAGE_005_FORBIDDEN_UPDATE_OBJECTS},
        "005 workflow: forbidden current/prior-stage UPDATE set is not exact",
    )
    require(len(alter_matches) == 3 and set(alter_matches) == {"projects", "visittypes", "mileageraterules"},
            "005 workflow: required ALTER set is not exact")
    require(len(reference_matches) == 1 and set(reference_matches) == {"users"},
            "005 workflow: required REFERENCES set is not exact")

    warmup_name = "Warm up primary Azure SQL connectivity"
    preflight_name = "Validate predecessor, clean state, exact permissions, and capture fingerprints"
    up_name = "Apply only 1800_005 Up.sql"
    verify_name = "Run exact 1800_005 Verify.sql"
    history_name = "Revalidate frozen v1.7.2 historical SHA-256 fingerprints"
    final_name = "Confirm exact result, historical fingerprints, and STOP_FOR_REVIEW"
    expected_sql_steps = {
        warmup_name,
        preflight_name,
        up_name,
        verify_name,
        history_name,
        final_name,
    }

    warmup_step = exact_step(warmup_name)
    require(warmup_step.get("shell") == "pwsh", "005 workflow: warm-up shell must be exactly pwsh")
    warmup = validate_timeout_profile(warmup_name, 60, 60)

    query_matches = re.findall(r"(?m)^\s*\$query\s*=\s*'([^'\r\n]*)'\s*$", warmup)
    require(len(query_matches) == 1, "005 workflow: warm-up must have one literal $query assignment")
    normalized_query = re.sub(r"\s+", " ", query_matches[0]).strip()
    approved_query = (
        "SET NOCOUNT ON; SELECT DB_NAME() AS DatabaseName, "
        "USER_NAME() AS DatabasePrincipal;"
    )
    require(normalized_query == approved_query, "005 workflow: warm-up query is not the exact read-only query")
    require(
        not re.search(
            r"(?i)\b(?:INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|EXEC(?:UTE)?|FROM|JOIN)\b|dbo\.",
            normalized_query,
        ),
        "005 workflow: warm-up query contains business-table SQL, DDL, or DML",
    )
    for forbidden in ("-InputFile", "Up.sql", "Verify.sql"):
        require(forbidden not in warmup, f"005 workflow: warm-up contains forbidden token: {forbidden}")

    require(
        "if ($databaseName -ne 'db-fieldvisit-uat') { throw" in warmup,
        "005 workflow: warm-up exact database fail-closed comparison missing",
    )
    require(
        "if ($databasePrincipal -ne 'gh-fieldvisit-uat-migrate') { throw" in warmup,
        "005 workflow: warm-up exact principal fail-closed comparison missing",
    )
    require(warmup.count("$maxAttempts = 3") == 1,
            "005 workflow: warm-up maxAttempts must be exactly 3")
    require(warmup.count("$backoffSeconds = 5") == 1,
            "005 workflow: warm-up backoffSeconds must be exactly 5")
    require(
        len(re.findall(
            r"for\s*\(\s*\$attempt\s*=\s*1\s*;\s*\$attempt\s*-le\s*\$maxAttempts\s*;\s*\$attempt\+\+\s*\)",
            warmup,
        )) == 1,
        "005 workflow: warm-up retry loop must run through maxAttempts",
    )
    require(warmup.count("Start-Sleep -Seconds $backoffSeconds") == 1,
            "005 workflow: warm-up retry sleep is missing or duplicated")
    require(
        len(re.findall(
            r"if\s*\(\s*\$attempt\s*-ge\s*\$maxAttempts\s*\)\s*\{\s*throw\s*\}",
            warmup,
        )) == 1,
        "005 workflow: warm-up final failed attempt must throw",
    )

    validate_timeout_profile(preflight_name, 60, 60)
    validate_timeout_profile(up_name, 60, 600)
    validate_timeout_profile(verify_name, 60, 600)
    validate_timeout_profile(history_name, 60, 600)
    validate_timeout_profile(final_name, 60, 60)

    sql_step_names = {
        step.get("name")
        for step in steps
        if isinstance(step.get("run"), str) and "Invoke-Sqlcmd" in step["run"]
    }
    require(sql_step_names == expected_sql_steps,
            "005 workflow: SQL execution steps differ from the frozen connectivity contract")
    require(re.search(r"(?i)\bApplicationIntent\b", workflow) is None,
            "005 workflow: ApplicationIntent must not be used")
    require(re.search(r"(?i)-ConnectionTimeout\s+15\b", workflow) is None,
            "005 workflow: all SQL connections must use ConnectionTimeout 60")

    up = ROOT / "database" / "migrations" / "1800_005_project_visit_rate_lifecycle" / "Up.sql"
    stage_verify = ROOT / "database" / "migrations" / "1800_005_project_visit_rate_lifecycle" / "Verify.sql"
    history = ROOT / "database" / "migrations" / "security" / "uat" / "Verify-v180-historical-fingerprints.sql"
    require(sha256(up) == "4a78f8d53f77111b1c445bf9669274cbc70824ca589b0d8322367e5257551d10",
            "005: frozen Up.sql SHA-256 changed")
    require(sha256(stage_verify) == "fe4a264fa98100dd07d228d5984f112095f1f5dd8b806fe43d5a89fc4231f8ae",
            "005: frozen Verify.sql SHA-256 changed")
    require(sha256(history) == "1d7f77dc200590c2ba3b989ed340442d835f8e02be9edbb57af68ae5e4e9cba0",
            "005: frozen historical verifier SHA-256 changed")

    governance = GOVERNANCE.read_text(encoding="utf-8")
    governance_flat = re.sub(r"\s+", " ", governance)
    require(
        governance.count("| 1800_005 | `dbo.Projects`, `dbo.VisitTypes`, `dbo.MileageRateRules` |") == 1,
        "005 governance: exact temporary UPDATE row missing or duplicated",
    )
    require(
        "No `UPDATE` permission is permitted on `dbo.Organizations`, `dbo.Teams`, `dbo.UserIdentityProfiles`, `dbo.Locations`, or `dbo.DeploymentSiteLocationAssignments`"
        in governance_flat,
        "005 governance: exact forbidden UPDATE statement missing",
    )
    require(
        "RUN_ID=35745343788 exposed the previous permission-model defect and MUST NOT be rerun."
        in governance_flat,
        "005 governance: failed-run prohibition missing",
    )
    require(
        "The Grant and Revoke scripts are independent approved actions outside the migration workflow" in governance_flat,
        "005 governance: independent permission-action rule missing",
    )
    require(
        "The immutable Stage 005 SQL locks, recovery governance, single-stage execution boundary, no-automatic-rerun rule, and terminal `STOP_FOR_REVIEW` remain unchanged."
        in governance_flat,
        "005 governance: immutable/recovery/single-stage/STOP_FOR_REVIEW preservation missing",
    )

    print(
        "PASS 1800_005 permission-tooling+17-markers+exact-UPDATE+"
        "step-bound-connectivity+immutable-hashes"
    )


def validate_stage_006_permission_tooling() -> None:
    workflow_path = ROOT / ".github" / "workflows" / "azure-sql-uat-migration-1800-006.yml"
    workflow = workflow_path.read_text(encoding="utf-8")
    workflow_compact = compact_sql(workflow)
    parsed = yaml.load(workflow, Loader=yaml.BaseLoader)
    require(isinstance(parsed, dict), "006 workflow: YAML root must be a mapping")
    steps = parsed.get("jobs", {}).get("migrate-1800_006", {}).get("steps", [])
    require(isinstance(steps, list), "006 workflow: migration steps must be a list")

    def exact_step(name: str) -> dict[str, str]:
        matches = [step for step in steps if step.get("name") == name]
        require(len(matches) == 1, f"006 workflow: step missing or duplicated: {name}")
        return matches[0]

    def step_run(name: str) -> str:
        run = exact_step(name).get("run", "")
        require(isinstance(run, str) and run, f"006 workflow: step has no run block: {name}")
        return run

    def validate_token_masking(name: str, run: str) -> None:
        require(
            run.count("az account get-access-token") == 1,
            f"006 workflow: SQL step must obtain exactly one Azure SQL token: {name}",
        )
        require(
            run.count('Write-Output "::add-mask::$token"') == 1,
            f"006 workflow: token masking missing or duplicated: {name}",
        )

    def validate_timeout_profile(name: str, connection_timeout: int, query_timeout: int) -> str:
        run = step_run(name)
        invocations = [line.strip() for line in run.splitlines() if "Invoke-Sqlcmd" in line]
        require(
            len(invocations) == 1,
            f"006 workflow: expected exactly one Invoke-Sqlcmd in step: {name}",
        )
        invocation = invocations[0]
        require(
            re.findall(r"-ConnectionTimeout\s+(\d+)", invocation) == [str(connection_timeout)],
            f"006 workflow: incorrect ConnectionTimeout in step: {name}",
        )
        require(
            re.findall(r"-QueryTimeout\s+(\d+)", invocation) == [str(query_timeout)],
            f"006 workflow: incorrect QueryTimeout in step: {name}",
        )
        validate_token_masking(name, run)
        return run

    scripts: dict[str, str] = {}
    for kind, path in STAGE_006_PERMISSION_SCRIPTS.items():
        require(path.is_file(), f"006: {kind} permission script is missing")
        scripts[kind] = path.read_text(encoding="utf-8")

    grant = scripts["grant"]
    verify = scripts["verify"]
    revoke = scripts["revoke"]
    grant_compact = compact_sql(grant)
    verify_compact = compact_sql(verify)
    revoke_compact = compact_sql(revoke)

    expected_explicit_permission_allowlists = {
        "grant": {"connect"},
        "verify": {"connect", "insert", "update"},
        "revoke": {"connect"},
    }
    for kind, source in scripts.items():
        explicit_permission_allowlist = re.findall(
            r"p\.permission_name\s*=\s*N'([^']+)'", source, re.IGNORECASE
        )
        require(
            len(explicit_permission_allowlist)
            == len(expected_explicit_permission_allowlists[kind])
            and {permission.lower() for permission in explicit_permission_allowlist}
            == expected_explicit_permission_allowlists[kind],
            f"006 {kind}: explicit permission allowlist is not exact or permits DELETE",
        )
        require(
            re.search(r"\bGRANT\s+DELETE\b", source, re.IGNORECASE) is None,
            f"006 {kind}: DELETE permission grant is forbidden",
        )

    broad_roles = ("db_datawriter", "db_owner", "db_securityadmin")
    broad_role_absence_operators = {
        "grant": r"=\s*1",
        "verify": r"<>\s*0",
        "revoke": r"<>\s*0",
    }
    for kind, source in scripts.items():
        for role in broad_roles:
            pattern = (
                rf"ISNULL\(IS_ROLEMEMBER\(N'{role}',\s*"
                rf"N'gh-fieldvisit-uat-migrate'\),\s*0\)\s*"
                rf"{broad_role_absence_operators[kind]}"
            )
            require(
                len(re.findall(pattern, source, re.IGNORECASE)) == 1,
                f"006 {kind}: fail-closed {role}=NO gate missing or duplicated",
            )

    role_additions = {
        kind: re.findall(
            r"ALTER\s+ROLE\s+([A-Za-z0-9_]+)\s+ADD\s+MEMBER",
            source,
            re.IGNORECASE,
        )
        for kind, source in scripts.items()
    }
    require(role_additions["grant"] == ["db_ddladmin"],
            "006 grant: db_ddladmin must be the only role addition")
    require(not role_additions["verify"] and not role_additions["revoke"],
            "006 verify/revoke: role additions are forbidden")

    require(len(STAGE_006_PARTIAL_MARKERS) == 8,
            "006 validator: frozen partial-state marker count must be exactly 8")
    for marker in STAGE_006_PARTIAL_MARKERS:
        require(grant_compact.count(marker) == 1,
                f"006 grant: partial-state marker missing or duplicated: {marker}")
        require(workflow_compact.count(marker) == 1,
                f"006 workflow: partial-state marker missing or duplicated: {marker}")

    expected_table_markers = {
        match
        for match in re.findall(
            r"object_id\(n'dbo\.([^']+)',n'u'\)",
            "".join(STAGE_006_PARTIAL_MARKERS),
        )
    }
    expected_column_markers = {
        (table, column)
        for table, column in re.findall(
            r"col_length\(n'dbo\.([^']+)',n'([^']+)'\)",
            "".join(STAGE_006_PARTIAL_MARKERS),
        )
    }
    for source_name, source in (("grant", grant_compact), ("workflow", workflow_compact)):
        actual_table_markers = set(
            re.findall(
                r"object_id\(n'dbo\.(notificationenvironmentpolicies|notificationemailallowlist|"
                r"notificationsettings|notificationsettingrecipients|mailoutbox|maildeliverylogs)',n'u'\)",
                source,
            )
        )
        actual_column_markers = set(
            re.findall(
                r"col_length\(n'dbo\.(employments)',n'(optionalemailnotificationenabled|"
                r"emailnotificationenabled)'\)",
                source,
            )
        )
        require(actual_table_markers == expected_table_markers,
                f"006 {source_name}: Stage 006 table-marker set is not exact")
        require(actual_column_markers == expected_column_markers,
                f"006 {source_name}: Stage 006 column-marker set is not exact")

    granted_update_matches = re.findall(
        r"GRANT\s+UPDATE\s+ON\s+OBJECT::dbo\.([A-Za-z0-9_]+)", grant, re.IGNORECASE
    )
    revoked_update_matches = re.findall(
        r"REVOKE\s+UPDATE\s+ON\s+OBJECT::dbo\.([A-Za-z0-9_]+)", revoke, re.IGNORECASE
    )
    require(granted_update_matches == ["Employments"],
            "006 grant: exact UPDATE grant set must be {Employments}")
    require(revoked_update_matches == ["Employments"],
            "006 revoke: exact UPDATE revoke set must be {Employments}")

    required_grant_fragments = (
        "SET XACT_ABORT ON",
        "BEGIN TRANSACTION",
        "ROLLBACK TRANSACTION",
        "ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]",
        "GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]",
        "GRANT UPDATE ON OBJECT::dbo.Employments TO [gh-fieldvisit-uat-migrate]",
        "GRANT_PREPARED_FOR_1800_006",
        "latest SchemaVersion is not exact predecessor 1.8.0-005",
        "one or more of 8 Stage 006 partial-state markers exist",
        "pre-stage UPDATE capability residue exists",
    )
    for fragment in required_grant_fragments:
        require(fragment in grant, f"006 grant: required fail-closed fragment missing: {fragment}")

    required_verify_fragments = (
        "1800_006_PERMISSION_GATE",
        "@CanAlterEmployments <> 1",
        "@CanReferenceUsers <> 1",
        "@CanReferenceEmployments <> 1",
        "@CanUpdateEmployments <> 1",
        "@CanUpdateProjects <> 0",
        "@CanUpdateVisitTypes <> 0",
        "@CanUpdateMileageRateRules <> 0",
        "@CanUpdateOrganizations <> 0",
        "@CanUpdateTeams <> 0",
        "@CanUpdateUserIdentityProfiles <> 0",
        "@CanUpdateLocations <> 0",
        "@CanUpdateDeploymentAssignments <> 0",
    )
    for fragment in required_verify_fragments:
        require(fragment in verify, f"006 verify: required fail-closed gate missing: {fragment}")

    required_revoke_fragments = (
        "SET XACT_ABORT ON",
        "BEGIN TRANSACTION",
        "ROLLBACK TRANSACTION",
        "REVOKE UPDATE ON OBJECT::dbo.Employments FROM [gh-fieldvisit-uat-migrate]",
        "REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate]",
        "ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]",
        "@CanInsertDboSchema <> 0",
        "@CanUpdateEmployments <> 0",
        "@CanUpdateProjects <> 0",
        "@CanUpdateVisitTypes <> 0",
        "@CanUpdateMileageRateRules <> 0",
        "@CanUpdateOrganizations <> 0",
        "@CanUpdateTeams <> 0",
        "@CanUpdateUserIdentityProfiles <> 0",
        "@CanUpdateLocations <> 0",
        "@CanUpdateDeploymentAssignments <> 0",
        "REVOKED_1800_006_ELEVATION",
    )
    for fragment in required_revoke_fragments:
        require(fragment in revoke, f"006 revoke: required fail-closed check missing: {fragment}")
    require("SchemaVersions" not in revoke,
            "006 revoke: revoke must remain independently usable without a SchemaVersion gate")

    require(
        verify_compact.count(
            "object_name(p.major_id)=n'employments'andp.permission_name=n'update'andp.state=n'g'"
        ) == 1,
        "006 verify: exact Employments UPDATE allowlist is missing or duplicated",
    )
    require(
        "object_name(p.major_id)=n'employments'" not in revoke_compact,
        "006 revoke: CONNECT-only baseline check must not allow Employments UPDATE",
    )

    required_update_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'update'\)<>1",
        workflow_compact,
    )
    forbidden_update_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'update'\)<>0",
        workflow_compact,
    )
    alter_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'alter'\)<>1",
        workflow_compact,
    )
    reference_matches = re.findall(
        r"has_perms_by_name\(n'dbo\.([^']+)',n'object',n'references'\)<>1",
        workflow_compact,
    )
    require(required_update_matches == ["employments"],
            "006 workflow: required UPDATE set must be exactly {Employments}")
    require(
        len(forbidden_update_matches) == len(STAGE_006_FORBIDDEN_UPDATE_OBJECTS)
        and set(forbidden_update_matches) == {
            name.lower() for name in STAGE_006_FORBIDDEN_UPDATE_OBJECTS
        },
        "006 workflow: forbidden prior-stage UPDATE set is not exact",
    )
    require(alter_matches == ["employments"],
            "006 workflow: required ALTER set must be exactly {Employments}")
    require(len(reference_matches) == 2 and set(reference_matches) == {"users", "employments"},
            "006 workflow: required REFERENCES set must be exactly {Users, Employments}")

    warmup_name = "Warm up primary Azure SQL connectivity"
    preflight_name = "Validate predecessor, clean state, exact permissions, and capture fingerprints"
    up_name = "Apply only 1800_006 Up.sql"
    verify_name = "Run exact 1800_006 Verify.sql"
    history_name = "Revalidate frozen v1.7.2 historical SHA-256 fingerprints"
    final_name = "Confirm exact result, historical fingerprints, and STOP_FOR_REVIEW"
    expected_sql_steps = {
        warmup_name,
        preflight_name,
        up_name,
        verify_name,
        history_name,
        final_name,
    }

    warmup_step = exact_step(warmup_name)
    require(warmup_step.get("shell") == "pwsh", "006 workflow: warm-up shell must be exactly pwsh")
    warmup = validate_timeout_profile(warmup_name, 60, 60)

    query_matches = re.findall(r"(?m)^\s*\$query\s*=\s*'([^'\r\n]*)'\s*$", warmup)
    require(len(query_matches) == 1, "006 workflow: warm-up must have one literal $query assignment")
    normalized_query = re.sub(r"\s+", " ", query_matches[0]).strip()
    approved_query = (
        "SET NOCOUNT ON; SELECT DB_NAME() AS DatabaseName, "
        "USER_NAME() AS DatabasePrincipal;"
    )
    require(normalized_query == approved_query, "006 workflow: warm-up query is not the exact read-only query")
    require(
        not re.search(
            r"(?i)\b(?:INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|EXEC(?:UTE)?|FROM|JOIN)\b|dbo\.",
            normalized_query,
        ),
        "006 workflow: warm-up query contains business-table SQL, DDL, or DML",
    )
    for forbidden in ("-InputFile", "Up.sql", "Verify.sql"):
        require(forbidden not in warmup, f"006 workflow: warm-up contains forbidden token: {forbidden}")

    require(
        "if ($databaseName -ne 'db-fieldvisit-uat') { throw" in warmup,
        "006 workflow: warm-up exact database fail-closed comparison missing",
    )
    require(
        "if ($databasePrincipal -ne 'gh-fieldvisit-uat-migrate') { throw" in warmup,
        "006 workflow: warm-up exact principal fail-closed comparison missing",
    )
    require(warmup.count("$maxAttempts = 3") == 1,
            "006 workflow: warm-up maxAttempts must be exactly 3")
    require(warmup.count("$backoffSeconds = 5") == 1,
            "006 workflow: warm-up backoffSeconds must be exactly 5")
    require(
        len(re.findall(
            r"for\s*\(\s*\$attempt\s*=\s*1\s*;\s*\$attempt\s*-le\s*\$maxAttempts\s*;\s*\$attempt\+\+\s*\)",
            warmup,
        )) == 1,
        "006 workflow: warm-up retry loop must run through maxAttempts",
    )
    require(warmup.count("Start-Sleep -Seconds $backoffSeconds") == 1,
            "006 workflow: warm-up retry sleep is missing or duplicated")
    require(
        len(re.findall(
            r"if\s*\(\s*\$attempt\s*-ge\s*\$maxAttempts\s*\)\s*\{\s*throw\s*\}",
            warmup,
        )) == 1,
        "006 workflow: warm-up final failed attempt must throw",
    )

    validate_timeout_profile(preflight_name, 60, 60)
    validate_timeout_profile(up_name, 60, 600)
    validate_timeout_profile(verify_name, 60, 600)
    validate_timeout_profile(history_name, 60, 600)
    validate_timeout_profile(final_name, 60, 60)

    sql_step_names = {
        step.get("name")
        for step in steps
        if isinstance(step.get("run"), str) and "Invoke-Sqlcmd" in step["run"]
    }
    require(sql_step_names == expected_sql_steps,
            "006 workflow: SQL execution steps differ from the frozen connectivity contract")
    require(re.search(r"(?i)\bApplicationIntent\b", workflow) is None,
            "006 workflow: ApplicationIntent must not be used")
    require(re.search(r"(?i)-ConnectionTimeout\s+15\b", workflow) is None,
            "006 workflow: all SQL connections must use ConnectionTimeout 60")

    up = ROOT / "database" / "migrations" / "1800_006_notification_framework" / "Up.sql"
    stage_verify = ROOT / "database" / "migrations" / "1800_006_notification_framework" / "Verify.sql"
    history = ROOT / "database" / "migrations" / "security" / "uat" / "Verify-v180-historical-fingerprints.sql"
    require(sha256(up) == "69f3b864e91131ef0ffbc95e515aeac29ad1b1c2b48d9eb8e66cdb737eabcd56",
            "006: frozen Up.sql SHA-256 changed")
    require(sha256(stage_verify) == "df93b3580eb70a03260859e76c5df2478bc34bae3c8b967f69c3ce2623bb14d8",
            "006: frozen Verify.sql SHA-256 changed")
    require(sha256(history) == "1d7f77dc200590c2ba3b989ed340442d835f8e02be9edbb57af68ae5e4e9cba0",
            "006: frozen historical verifier SHA-256 changed")

    governance = GOVERNANCE.read_text(encoding="utf-8")
    governance_flat = re.sub(r"\s+", " ", governance)
    require(
        governance.count("| 1800_006 | `dbo.Employments` |") == 1,
        "006 governance: exact temporary UPDATE row missing or duplicated",
    )
    require(
        "No `UPDATE` permission is permitted on `dbo.Organizations`, `dbo.Teams`, `dbo.UserIdentityProfiles`, `dbo.Locations`, `dbo.DeploymentSiteLocationAssignments`, `dbo.Projects`, `dbo.VisitTypes`, or `dbo.MileageRateRules`"
        in governance_flat,
        "006 governance: exact forbidden UPDATE statement missing",
    )
    require(
        "The Stage 006 Grant and Revoke scripts are independent approved actions outside the migration workflow"
        in governance_flat,
        "006 governance: independent permission-action rule missing",
    )
    require(
        "The immutable Stage 006 SQL locks, eight-marker clean-state gate, recovery governance, single-stage execution boundary, no-automatic-rerun rule, and terminal `STOP_FOR_REVIEW` remain unchanged."
        in governance_flat,
        "006 governance: immutable/recovery/single-stage/STOP_FOR_REVIEW preservation missing",
    )

    print(
        "PASS 1800_006 permission-tooling+no-DELETE+broad-role-gates+8-markers+"
        "Employments-only-UPDATE+step-bound-connectivity+immutable-hashes"
    )


def validate(stage: str, directory: str, predecessor: str, target: str, update_objects: set[str]) -> None:
    token = f"1800_{stage}"
    workflow = ROOT / ".github" / "workflows" / f"azure-sql-uat-migration-1800-{stage}.yml"
    up = ROOT / "database" / "migrations" / directory / "Up.sql"
    verify = ROOT / "database" / "migrations" / directory / "Verify.sql"
    history = ROOT / "database" / "migrations" / "security" / "uat" / "Verify-v180-historical-fingerprints.sql"
    text = workflow.read_text(encoding="utf-8")

    # BaseLoader retains GitHub's `on` key as text rather than YAML 1.1 boolean True.
    parsed = yaml.load(text, Loader=yaml.BaseLoader)
    require(isinstance(parsed, dict), f"{stage}: YAML root must be a mapping")
    require(set(parsed.get("on", {})) == {"workflow_dispatch"}, f"{stage}: workflow_dispatch must be the only trigger")
    require(parsed["on"]["workflow_dispatch"]["inputs"]["confirm_migration"]["required"] == "true", f"{stage}: confirmation input must be required")

    for job in parsed.get("jobs", {}).values():
        for step in job.get("steps", []):
            run = step.get("run")
            shell = step.get("shell")
            if run and shell == "bash":
                result = subprocess.run(
                    ["bash", "-n"], input=run, text=True, capture_output=True, check=False
                )
                require(result.returncode == 0, f"{stage}: bash syntax: {result.stderr.strip()}")

    require(text.count("${{") == text.count("}}"), f"{stage}: unbalanced GitHub expression delimiters")
    require(text.count("@'") == text.count("'@"), f"{stage}: unbalanced literal PowerShell here-string")
    require(text.count('@"') == text.count('"@'), f"{stage}: unbalanced interpolated PowerShell here-string")

    require('refs/heads/post-uat/v1.8.0' in text, f"{stage}: branch guard missing")
    require(f'Confirmation must be exactly {token}.' in text, f"{stage}: exact token guard missing")
    require('${GITHUB_SHA}' in text and '${APPROVED_MIGRATION_COMMIT_SHA}' in text, f"{stage}: approved SHA equality guard missing")
    require('ref: ${{ vars.APPROVED_MIGRATION_COMMIT_SHA }}' in text, f"{stage}: exact approved checkout missing")
    require('persist-credentials: false' in text, f"{stage}: checkout credentials must not persist")

    up_match = re.search(r"EXPECTED_UP_SHA256: ([0-9a-f]{64})", text)
    verify_match = re.search(r"EXPECTED_VERIFY_SHA256: ([0-9a-f]{64})", text)
    history_match = re.search(r"EXPECTED_HISTORY_SHA256: ([0-9a-f]{64})", text)
    require(up_match is not None and up_match.group(1) == sha256(up), f"{stage}: Up.sql hash lock mismatch")
    require(verify_match is not None and verify_match.group(1) == sha256(verify), f"{stage}: Verify.sql hash lock mismatch")
    require(history_match is not None and history_match.group(1) == sha256(history), f"{stage}: historical verifier hash lock mismatch")

    require('rg-fieldvisit-uat' in text and 'sql-fieldvisit-jpe-uat' in text and 'db-fieldvisit-uat' in text, f"{stage}: exact UAT target guard missing")
    require(f"VersionNumber=N'{predecessor}'" in text, f"{stage}: predecessor guard missing")
    require(f"VersionNumber=N'{target}'" in text, f"{stage}: target version guard missing")
    require(f"partial {target} state exists" in text, f"{stage}: clean target-stage guard missing")

    require("db_datareader" in text and "db_ddladmin" in text, f"{stage}: required role gate missing")
    require("db_owner" in text and "db_securityadmin" in text and "db_datawriter" in text, f"{stage}: forbidden broad-role gate missing")
    require("Unexpected explicit database permission" in text, f"{stage}: exact explicit-permission allowlist missing")
    update_permission_checks = set(
        re.findall(
            r"HAS_PERMS_BY_NAME\(N'dbo\.([A-Za-z0-9_]+)',N'OBJECT',N'UPDATE'\)<>1",
            text,
        )
    )
    require(update_permission_checks == update_objects, f"{stage}: UPDATE permission gate is not exact")

    expected_path = f"database/migrations/{directory}"
    referenced_migration_paths = set(re.findall(r"database/migrations/(1800_00[1-7]_[A-Za-z0-9_]+)", text))
    require(referenced_migration_paths == {directory}, f"{stage}: workflow references more than its single stage")
    require(text.count(f"Apply only {token} Up.sql") == 1, f"{stage}: single apply step missing or duplicated")
    require(text.count("-InputFile $up") == 1 and text.count("-InputFile $verify") == 1, f"{stage}: exact one Up/Verify invocation required")
    require(text.count("-InputFile $history") == 1, f"{stage}: exact one historical fingerprint invocation required")
    require(expected_path + "/Up.sql" in text and expected_path + "/Verify.sql" in text, f"{stage}: fixed script pair missing")

    require("SchemaVersion count did not advance by exactly one" in text, f"{stage}: one-stage version-count assertion missing")
    require("VisitTrips historical key fingerprint changed" in text, f"{stage}: Trip fingerprint assertion missing")
    require("VisitTripSnapshots historical key fingerprint changed" in text, f"{stage}: Snapshot fingerprint assertion missing")
    require("STOP_FOR_REVIEW" in text, f"{stage}: stop gate missing")

    for other in CONFIG:
        other_token = f"1800_{other}"
        if other_token != token:
            require(other_token not in text, f"{stage}: references another executable stage {other_token}")

    print(f"PASS {token} YAML+guards+hashes+single-stage+STOP_FOR_REVIEW")


def main() -> int:
    try:
        validate_recovery_governance()
        validate_post_uat_hook()
        validate_stage_004_permission_tooling()
        validate_stage_005_permission_tooling()
        validate_stage_006_permission_tooling()
        for stage, values in CONFIG.items():
            validate(stage, *values)
    except (AssertionError, OSError, yaml.YAMLError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    print("VALIDATION_RESULT=PASS")
    print("MIGRATION_EXECUTED=NO")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
