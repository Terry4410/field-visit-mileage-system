#!/usr/bin/env python3
"""Offline, fail-closed IT handoff readiness ledger; NEVER a release GO token.

Runs on the isolated WORK CI runner. Documents source-backed preparation only.
Does not connect to databases, Azure, HR, Entra or any deployment endpoint.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
EXPECTED_PROTECTED = "68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2"
IT_FILE = "docs/release/PACKAGE-B3-IT-SCHEMA-REVIEW-EXECUTION-HOLD-CHECKLIST-DRAFT.md"
SOURCE_FILES = (
    IT_FILE,
    "docs/release/PACKAGE-B3-1800_011-SCHEMA-IA-NONEXECUTABLE.md",
    "docs/release/PACKAGE-B3-1800_012-FORWARD-ONLY-ATOMIC-APPLY-IA-NONEXECUTABLE.md",
    "docs/release/PACKAGE-B-UAT-CONSOLIDATED-FINAL-EXECUTION-APPROVAL-DRAFT.md",
    "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidateNameManifest.cs",
    "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidatePhysicalManifest.cs",
    "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidateCheckDefaultsManifest.cs",
    "backend/tests/FieldVisit.Application.Tests/V180ApprovedSnapshotExportReadSourceTests.cs",
)
GATE_OWNERS = (
    ("DDL-01", "IT/DBA", "011 independent Up/Verify and predecessor proof"),
    ("DDL-02", "IT/DBA", "012 independent Up/Verify and forward-only recovery"),
    ("CAT-01", "DBA", "Full signed 011/012 physical column/EF catalog"),
    ("CAT-02", "DBA", "Signed CHECK/default/index/filtered-key parity"),
    ("CAT-03", "DBA", "Signed FK/trigger/permissions dependency graph"),
    ("LOC-01", "DBA/Development", "Actual UAT Locations schema and seven fields"),
    ("HIST-01", "DBA/Business", "True Snapshot and historic reference invariants"),
    ("VER-01", "DBA/Security", "Independent signed schema/checksum provenance"),
    ("GRANT-01", "Security/DBA", "011/012 least-privilege grant/verify/revoke"),
    ("DEP-01", "IT DevOps", "Version-pinned isolated UAT migration workflows"),
    ("VAL-B1-002", "HR/IT", "Real HR/role/team grant and denial attestations"),
    ("B3-APPROVE", "IT/Owner", "Approved live atomic executor and runtime security"),
    ("B4-UAT", "Business/Owner", "22 formal test results on authorized UAT"),
    ("PRODUCTION", "Owner/IT", "Separate signed production authorization"),
)


def collect(root: Path, sha: str) -> dict:
    """Collect independent SOURCE facts, never reinterpret tests as signoff."""
    if not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise ValueError("HANDOFF_INVALID_WORK_SHA")
    fingerprints = []
    for path in SOURCE_FILES:
        file = root / path
        if not file.is_file():
            raise ValueError("HANDOFF_REQUIRED_SOURCE_MISSING: " + path)
        fingerprints.append({
            "file": path,
            "sha256": hashlib.sha256(file.read_bytes()).hexdigest(),
        })

    checklist = (root / IT_FILE).read_text(encoding="utf-8")
    missing = [gid for gid, _, _ in GATE_OWNERS if gid not in checklist
               and gid not in ("B3-APPROVE", "B4-UAT", "PRODUCTION")]
    if missing:
        raise ValueError("HANDOFF_GATE_UNLISTED: " + ",".join(missing))

    cfg = root / "backend/src/FieldVisit.Api/appsettings.json"
    service = root / "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs"
    if not cfg.is_file() or not service.is_file():
        raise ValueError("HANDOFF_APP_SOURCE_MISSING")
    if json.loads(cfg.read_text(encoding="utf-8"))["PackageB"]["B3"]["Enabled"] is not False:
        raise ValueError("HANDOFF_FEATURE_NOT_OFF")
    content = service.read_text(encoding="utf-8")
    pos = content.find("public async Task<V180B3RequestView> ApproveAsync(")
    if pos < 0 or 'throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED")' not in content[pos:]:
        raise ValueError("HANDOFF_APPROVE_NOT_DENY_ALL")

    migrations = root / "database/migrations"
    workflows = root / ".github/workflows"
    if not migrations.is_dir() or not workflows.is_dir():
        raise ValueError("HANDOFF_REPOSITORY_INVENTORY_MISSING")
    # 011 and 012 are PREPARATION ONLY; the existence of an executable
    # migration/workflow cannot be represented as an approved readiness PASS.
    forbidden = [
        p.relative_to(root).as_posix()
        for p in migrations.rglob("*")
        if p.is_file() and re.search(r"1800[_-]01[12]", p.as_posix(), re.I)
    ]
    forbidden += [
        p.relative_to(root).as_posix()
        for p in workflows.iterdir()
        if p.is_file() and re.search(r"1800[_-]01[12]", p.name, re.I)
    ]
    if forbidden:
        raise ValueError("HANDOFF_UNAUTHORIZED_MIGRATION_OR_WORKFLOW: " +
                         ",".join(sorted(forbidden)))
    gates = [{"id": gid, "owner": owner, "required": needed,
              "status": "BLOCKED_UNSIGNED"} for gid, owner, needed in GATE_OWNERS]

    return {
        "kind": "PACKAGE_B_IT_HANDOFF_NONEXECUTABLE",
        "status": "IT_HANDOFF_PREP_PASS_RELEASE_HOLD",
        "release_decision": "NO_GO",
        "source_sha": sha,
        "protected_source_expected": EXPECTED_PROTECTED,
        "scope": "SOURCE_ONLY_WORK_BRANCH_NO_EXTERNAL_ACCESS",
        "review_artifact_hashes": fingerprints,
        "release_gates": gates,
        "formal_b4_accepted": 0,
        "formal_b4_total": 22,
        "feature_B3_enabled": False,
        "approve_apply": "DENY_ALL",
        "uat_migration_011_authorized": False,
        "uat_migration_012_authorized": False,
        "uat_deployment_authorized": False,
        "business_uat": "HOLD",
        "production": "HARD_HOLD",
        "work_ci_status": "VERIFY_FINAL_GITHUB_ACTIONS_SEPARATELY",
        "note": ("Candidate schema and disposable CI are technical evidence only; "
                 "these entries are not signed IT/HR/Owner attestations."),
    }


def render(report: dict) -> str:
    lines = [
        "### Package B3 — IT handoff ledger (NO RELEASE)",
        "Status: " + report["status"],
        "Exact WORK SHA: " + report["source_sha"],
        "Release: " + report["release_decision"],
        "Formal B4: 0/22; B3 OFF; Approve/Apply DENY ALL;",
        "UAT migrations/Deploy NOT AUTHORIZED; Production HARD HOLD.",
        "",
        "| Gate | Owner | Disposition |",
        "| --- | --- | --- |",
    ]
    lines += [f"| {g['id']} | {g['owner']} | {g['status']} |"
              for g in report["release_gates"]]
    lines.extend(("", "Reviewed candidate files are SHA256-bound in the JSON artifact.",
                  "CI PASS here is only evidence that HOLD was preserved."))
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", required=True)
    ap.add_argument("--summary", required=True)
    args = ap.parse_args()
    result_path = ROOT / args.report
    summary_path = ROOT / args.summary
    result_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        sha = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        expected = os.environ.get("GITHUB_SHA")
        if expected and expected != sha:
            raise ValueError("HANDOFF_SHA_MISMATCH")
        result = collect(ROOT, sha)
        code = 0
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError,
            json.JSONDecodeError) as exc:
        result = {"kind": "PACKAGE_B_IT_HANDOFF_NONEXECUTABLE",
                  "status": "FAIL_CLOSED", "release_decision": "NO_GO",
                  "error": str(exc), "business_uat": "HOLD",
                  "production": "HARD_HOLD"}
        code = 1
    result_path.write_text(json.dumps(result, indent=2,
                                      ensure_ascii=False) + "\n",
                           encoding="utf-8")
    summary_path.write_text(
        render(result) if code == 0
        else "### IT handoff ledger FAIL CLOSED\n" + result["error"] + "\n"
             + "Release NO_GO, Business UAT HOLD, Production HARD_HOLD.\n",
        encoding="utf-8")
    print(summary_path.read_text(encoding="utf-8"))
    return code


if __name__ == "__main__":
    sys.exit(main())
