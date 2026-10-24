#!/usr/bin/env python3
"""Offline source-closure gate for the PR50 UAT010 compatibility candidate.

This gate verifies frozen source inputs and explicitly refuses to interpret the
synthetic Promotion fingerprint or the candidate reference as observed UAT data.
It performs no network, Azure, SQL, file export, or identity operation.
"""
from __future__ import annotations

import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Callable, Mapping

BASE = "b18ced16aec934dedc6dcb8cdc90b5679c0e6f68"
STAGING = "d5ae0b47b8a3c4887fb4e090877e5929749c45b4"
STAGING_PARENT = "b020357b1f1a8b9cdf19512abb653afca883a177"
RC = "f29affb3299828f45b1f131321779e8bea2f86a8"
MANIFEST = "docs/release/g1-execution/promotion/manifest.json"
NECESSARY = "docs/release/g1-minimal-preflight/necessary-reference.json"
PROMOTION_REFERENCE = "docs/release/g1-execution/promotion/reference.json"
BACKFILL = "docs/release/optionc-canonical-sql-draft/candidate/1800_014/Backfill.sql"
CATALOG = "docs/release/g1-execution/promotion/CatalogContract.sql"

EXPECTED_TRIGGERS = {
    "1800_001_organization_center_team_lifecycle": {"TR_TeamCenterAssignments_NoOverlap"},
    "1800_002_person_employment_role_membership": {
        "TR_EmploymentStatusPeriods_NoOverlap", "TR_EmploymentRoleAssignments_NoOverlap",
        "TR_TeamMemberships_OnePrimary", "TR_TeamLeaderAssignments_NoOverlap",
        "TR_TeamLeaderDelegations_NoOverlap",
    },
    "1800_003_deployment_sites": {
        "TR_DeploymentSites_CenterPeriod", "TR_DeploymentSiteLocations_NoOverlap",
        "TR_TeamDeploymentSites_NoOverlap", "TR_EmploymentDeploymentSites_SameSiteNoOverlap",
        "TR_EmploymentDeploymentSites_OnePrimary", "TR_TeamCenterAssignments_ProtectTeamSites",
        "TR_DeploymentSites_ProtectTeamSites",
    },
    "1800_004_location_governance": {
        "TR_Locations_ProtectCurrentDeploymentSiteLocations",
        "TR_DeploymentSiteLocationAssignments_ProtectActiveLocation",
    },
    "1800_005_project_visit_rate_lifecycle": {"TR_MileageRateRules_ProtectSeries"},
    "1800_006_notification_framework": set(),
    "1800_007_mileage_google_governance": {
        "TR_RouteCalculationAttempts_BasisTrip", "TR_Locations_SelectedGeocodingAttempt",
        "TR_MileageCalculations_SelectedRouteTrip", "TR_MileageCalculations_DecisionEvidence",
        "TR_VisitTripSnapshots_RouteAttemptTrip",
    },
    "1800_008_rate_manual_effective_dates": {"TR_MileageRateRules_ProtectSeries"},
    "1800_009_one_stop_route_attempt_constraint": set(),
    "1800_010_background_mileage_reason_constraint": set(),
}
TRIGGER_RE = re.compile(r"\bCREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+(?:\[?dbo\]?\s*\.\s*)?\[?([A-Za-z0-9_]+)\]?", re.I)
EXPECTED_IDS = ["1.8.0-011", "1.8.0-012", "1.8.0-014", "1.8.0-015", "1.8.0-016", "1.8.0-017", "1.8.0-018", "1.8.0-019", "1.8.0-020"]
REQUIRED_INPUTS = {
    f"database/migrations/1800_{i:03d}_" for i in range(1, 11)
}


class GateError(ValueError):
    """A safe, non-sensitive contract failure."""


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def validate_reference(manifest: Mapping, promotion_reference: Mapping, necessary: Mapping) -> None:
    if manifest.get("contract") != "g1-fixed-target-promotion-v1" or manifest.get("rcSource") != RC:
        raise GateError("manifest")
    if manifest.get("reviewedStagingCommit") != STAGING or manifest.get("reviewedStagingParent") != STAGING_PARENT:
        raise GateError("staging-source")
    if necessary.get("rc") != RC or necessary.get("scope") != "CANDIDATE_NECESSARY_CONTRACT_NOT_OBSERVED_UAT":
        raise GateError("necessary-reference")
    if manifest.get("approval") != "PROPOSED_NOT_AUTHORIZED":
        raise GateError("authorization-state")
    if promotion_reference.get("originRc") != RC or "SYNTHETIC" not in promotion_reference.get("fixture", ""):
        raise GateError("synthetic-fixture-label")
    baseline = manifest.get("schema010SourceBoundBaseline", {})
    if baseline.get("status") != "HOLD" or baseline.get("syntheticReferenceAcceptedAsUatBaseline") is not False:
        raise GateError("baseline-must-remain-unknown")
    ids = [step.get("proposedId") for step in manifest.get("steps", [])]
    if ids != EXPECTED_IDS or any("013" in item for item in ids):
        raise GateError("migration-order")
    inputs = necessary.get("inputs", {})
    migrations = {p for p in inputs if p.startswith("database/migrations/1800_") and p.endswith("/Up.sql")}
    if len(migrations) != 10:
        raise GateError("source-input-coverage")
    for prefix in REQUIRED_INPUTS:
        if not any(p.startswith(prefix) for p in migrations):
            raise GateError("source-input-coverage")
    if not all(isinstance(v, str) and re.fullmatch(r"[0-9a-f]{64}", v) for v in inputs.values()):
        raise GateError("source-hash-shape")


def verify_trigger_sources(read: Callable[[str], bytes], necessary: Mapping) -> None:
    inputs = necessary.get("inputs", {})
    for dirname, expected in EXPECTED_TRIGGERS.items():
        candidates = [p for p in inputs if p.startswith(f"database/migrations/{dirname}/") and p.endswith("/Up.sql")]
        if len(candidates) != 1:
            raise GateError("trigger-source-map")
        text = read(candidates[0]).decode("utf-8")
        found = set(TRIGGER_RE.findall(text))
        if found != expected:
            raise GateError("trigger-source-set")


def validate_trigger_texts(by_migration: Mapping[str, str]) -> None:
    if set(by_migration) != set(EXPECTED_TRIGGERS):
        raise GateError("trigger-source-map")
    for migration, text in by_migration.items():
        if set(TRIGGER_RE.findall(text)) != EXPECTED_TRIGGERS[migration]:
            raise GateError("trigger-source-set")


def validate_teamless_policy(backfill: str) -> None:
    required = ("l.TeamId IS NOT NULL", "u.OrganizationId<>l.OrganizationId", "t.OrganizationId<>l.OrganizationId")
    if any(token not in backfill for token in required):
        raise GateError("nullable-team-boundary")
    if not re.search(r"l\.TeamId\s+IS\s+NOT\s+NULL\s+AND", backfill, re.I):
        raise GateError("nullable-team-boundary")


def verify_source_files(root: Path, manifest: Mapping, necessary: Mapping) -> None:
    def read(rel: str) -> bytes:
        path = root / rel
        if not path.is_file():
            raise GateError("source-missing")
        return path.read_bytes()

    for source_map in (manifest.get("sourceHashes", {}), manifest.get("files", {}), necessary.get("inputs", {})):
        if not isinstance(source_map, dict) or not source_map:
            raise GateError("source-map")
        for path, digest in source_map.items():
            if sha256(read(path)) != digest.lower():
                raise GateError("source-hash")
    original = manifest.get("originalSqlSha256", {})
    if not isinstance(original, dict) or not original:
        raise GateError("source-map")
    overrides = {item.get("path"): item for item in manifest.get("candidateSourceOverrides", [])}
    for path, digest in original.items():
        override = overrides.get(path)
        expected = override.get("reviewedSha256") if override else digest
        if sha256(read(path)) != expected.lower():
            raise GateError("source-hash")
        if override and override.get("previousRegistrySha256") != digest:
            raise GateError("override-provenance")
    verify_trigger_sources(read, necessary)

    backfill = read(BACKFILL).decode("utf-8")
    # The only permitted NULL-Team case is guarded by the explicit non-NULL
    # branch; the same-org Creator remains independently required.
    validate_teamless_policy(backfill)

    catalog = read(CATALOG).decode("utf-8")
    promotion = read("docs/release/g1-execution/promotion/Promotion.sql").decode("utf-8")
    if any(token not in catalog for token in ("OBJECT_DEFINITION", "sys.triggers", "is_disabled")):
        raise GateError("catalog-contract")
    if any(token not in promotion for token in ("is_not_trusted", "CK_ImportBatches_Status", "CK_ImportBatchItems_Status")):
        raise GateError("catalog-contract")


def sanitized_result(source_sha: str, contract: str = "PASS") -> dict[str, str]:
    if not re.fullmatch(r"[0-9a-f]{40}", source_sha):
        raise GateError("source-sha")
    if contract not in ("PASS", "FAIL"):
        raise GateError("status")
    return {
        "SourceSHA": source_sha,
        "SourceClosure": contract,
        "Necessary010Acceptance": "HOLD",
        "TriggerEventParity": "UNKNOWN",
        "ObservedUAT010": "UNKNOWN",
        "ApiPrincipal": "UNKNOWN",
        "RuntimeProvenance": "UNKNOWN",
        "Recovery": "UNKNOWN",
        "LiveOperation": "NOT_EXECUTED",
    }


def run(root: Path, enforce_parent: bool = False) -> dict[str, str]:
    manifest = json.loads((root / MANIFEST).read_text(encoding="utf-8"))
    promotion_reference = json.loads((root / PROMOTION_REFERENCE).read_text(encoding="utf-8"))
    necessary = json.loads((root / NECESSARY).read_text(encoding="utf-8"))
    validate_reference(manifest, promotion_reference, necessary)
    verify_source_files(root, manifest, necessary)
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise GateError("git-sha")
    if enforce_parent:
        try:
            subprocess.check_call(["git", "merge-base", "--is-ancestor", BASE, sha], cwd=root,
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except subprocess.CalledProcessError as exc:
            raise GateError("base-ancestry") from exc
    return sanitized_result(sha)


def main() -> int:
    try:
        result = run(Path(__file__).resolve().parents[2], enforce_parent=True)
        print("; ".join(f"{key}={value}" for key, value in result.items()))
        return 0
    except Exception:
        print("SourceClosure=FAIL; Details=SUPPRESSED; ObservedUAT010=UNKNOWN; ApiPrincipal=UNKNOWN; RuntimeProvenance=UNKNOWN; Recovery=UNKNOWN; LiveOperation=NOT_EXECUTED")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
