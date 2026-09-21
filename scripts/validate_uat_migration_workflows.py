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
    "005": ("1800_005_project_visit_rate_lifecycle", "1.8.0-004", "1.8.0-005", {"Projects", "VisitTypes", "MileageRateRules"}),
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

    require(workflow.count("Warm up primary Azure SQL connectivity") == 1,
            "004 workflow: primary connectivity warm-up missing or duplicated")
    require("$maxAttempts = 3" in workflow and "$backoffSeconds = 5" in workflow,
            "004 workflow: approved connectivity retry policy missing")
    require("ApplicationIntent" not in workflow,
            "004 workflow: ApplicationIntent must not be used")
    require("-ConnectionTimeout 15" not in workflow,
            "004 workflow: all SQL connections must use ConnectionTimeout 60")

    print("PASS 1800_004 permission-tooling+17-markers+negative-UPDATE+connectivity")


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
