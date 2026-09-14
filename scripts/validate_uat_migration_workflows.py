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
    "004": ("1800_004_location_governance", "1.8.0-003", "1.8.0-004", {"Locations"}),
    "005": ("1800_005_project_visit_rate_lifecycle", "1.8.0-004", "1.8.0-005", {"Projects", "VisitTypes", "MileageRateRules"}),
    "006": ("1800_006_notification_framework", "1.8.0-005", "1.8.0-006", {"Employments"}),
    "007": ("1800_007_mileage_google_governance", "1.8.0-006", "1.8.0-007", {"MileageCalculations"}),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


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
        re.findall(r"HAS_PERMS_BY_NAME\(N'dbo\.([A-Za-z0-9_]+)',N'OBJECT',N'UPDATE'\)", text)
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
