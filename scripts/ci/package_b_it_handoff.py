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

from package_b_approval_hold import require_approve_deny_all
from package_b_appsettings_hold import checked_in_config_is_off

ROOT = Path(__file__).resolve().parents[2]
EXPECTED_PROTECTED = "68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2"
IT_FILE = "docs/release/PACKAGE-B3-IT-SCHEMA-REVIEW-EXECUTION-HOLD-CHECKLIST-DRAFT.md"
DELTA_FILE = "docs/release/PACKAGE-B3-IT-EVIDENCE-DELTA-MATRIX.json"
SOURCE_FILES = (
    IT_FILE,
    DELTA_FILE,
    "backend/tests/FieldVisit.B3.SqlServer.Tests/B312ExpandedLocationsColumnContract.cs",
    "backend/tests/FieldVisit.B3.SqlServer.Tests/B312RealB3ActorSqlFixture.cs",
    "docs/release/PACKAGE-B3-APPROVAL-APPLY-SECURITY-DESIGN-NONEXECUTABLE.md",
    "docs/release/PACKAGE-B-FULL-UAT-22-CASE-TRACEABILITY-NONEXECUTABLE.md",
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


# Independent expected evidence mapping. Source paths are deliberately kept
# separately from the user-editable JSON, so the matrix cannot substitute a
# self-asserted GO, a fabricated path or a test-only claim for IT signoff.
EVIDENCE_SOURCE_BY_GATE = {
    "DDL-01": "docs/release/PACKAGE-B3-1800_011-SCHEMA-IA-NONEXECUTABLE.md",
    "DDL-02": "docs/release/PACKAGE-B3-1800_012-FORWARD-ONLY-ATOMIC-APPLY-IA-NONEXECUTABLE.md",
    "CAT-01": "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidatePhysicalManifest.cs",
    "CAT-02": "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidateCheckDefaultsManifest.cs",
    "CAT-03": "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidateNameManifest.cs",
    "LOC-01": "backend/tests/FieldVisit.B3.SqlServer.Tests/B312ExpandedLocationsColumnContract.cs",
    "HIST-01": "backend/tests/FieldVisit.Application.Tests/V180ApprovedSnapshotExportReadSourceTests.cs",
    "VER-01": "backend/tests/FieldVisit.B3.SqlServer.Tests/B312PinnedCandidatePhysicalManifest.cs",
    "GRANT-01": IT_FILE,
    "DEP-01": IT_FILE,
    "VAL-B1-002": "backend/tests/FieldVisit.B3.SqlServer.Tests/B312RealB3ActorSqlFixture.cs",
    "B3-APPROVE": "docs/release/PACKAGE-B3-APPROVAL-APPLY-SECURITY-DESIGN-NONEXECUTABLE.md",
    "B4-UAT": "docs/release/PACKAGE-B-FULL-UAT-22-CASE-TRACEABILITY-NONEXECUTABLE.md",
    "PRODUCTION": "docs/release/PACKAGE-B-UAT-CONSOLIDATED-FINAL-EXECUTION-APPROVAL-DRAFT.md",
}

# Source-authored requirements, independent of user-editable JSON entries.
# A nonempty but weakened requirement MUST NOT count as a reviewable gate.
EVIDENCE_REQUIREMENT_BY_GATE = {
    "DDL-01": "IT/DBA 獨立審查 011 Up.sql、Verify.sql、前置版本與來源 SHA256",
    "DDL-02": "IT/DBA 獨立審查 012 Up.sql、Verify.sql、前向修改及失敗補償",
    "CAT-01": "DBA 實際完整 SQL catalog 欄位與 EF 對照及簽認",
    "CAT-02": "DBA 實際 CHECK、DEFAULT、Index filter 與 SQL 定義簽認",
    "CAT-03": "DBA 真實 FK、trigger、permission、dependency 完整清單簽認",
    "LOC-01": "DBA 實際 Locations 全部索引、依賴、七欄更新相容性簽認",
    "HIST-01": "DBA 與業務對歷史關聯、完整 Snapshot 查詢及 API／Excel 的真實 UAT 驗證",
    "VER-01": "IT 從正式受控 DDL 獨立固定且簽署 Schema Fingerprint",
    "GRANT-01": "資安／DBA 011／012 專屬限時 Grant、Verify、Revoke 與稽核",
    "DEP-01": "IT DevOps 獨立正式 UAT workflow、SHA pin、backup、rollback 與 postflight",
    "VAL-B1-002": "HR／IT 真實 HR、Admin grant、Team membership、撤銷與來源證據簽章",
    "B3-APPROVE": "IT／Owner 正式交易內授權、独立審核與核准/Apply 功能放行",
    "B4-UAT": "Business Owner 已核准 UAT 環境逐案執行 22/22 並簽名",
    "PRODUCTION": "Owner／IT 獨立正式上線授權、變更視窗與回退計畫",
}
MATRIX_EXPECTED_KEYS = frozenset({
    "kind", "schema_version", "decision", "release_authorized",
    "signoff_confirmed", "scope", "explanation", "gates",
})
GATE_EXPECTED_KEYS = frozenset({
    "id", "owner", "disposition", "candidate_evidence_class",
    "reviewed_source_path", "independent_external_evidence_required",
    "independent_signoff_received", "can_unlock_migration",
})
EXPECTED_EXPLANATION = "Evidence paths identify working candidates or documents, never signed live UAT/IT evidence. All gates remain BLOCKED_UNSIGNED."

WORK_TEST_ONLY_GATES = frozenset({
    "CAT-01", "CAT-02", "CAT-03", "LOC-01", "HIST-01",
    "VER-01", "VAL-B1-002",
})


def validate_delta_matrix(root: Path) -> list[dict]:
    """Validate explicit source-vs-missing evidence, never real-world signoff."""
    try:
        matrix = json.loads((root / DELTA_FILE).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError("HANDOFF_DELTA_MISSING_OR_MALFORMED") from exc
    if (not isinstance(matrix, dict)
            or matrix.get("kind") != "PACKAGE_B3_IT_GATE_EVIDENCE_DELTA_NONEXECUTABLE"
            or type(matrix.get("schema_version")) is not int
            or matrix["schema_version"] != 1
            or matrix.get("decision") != "NO_GO"
            or matrix.get("release_authorized") is not False
            or matrix.get("signoff_confirmed") is not False
            or matrix.get("scope") != "SOURCE_ONLY_WORK_CANDIDATE_NOT_UAT_OR_PRODUCTION"
            or set(matrix) != MATRIX_EXPECTED_KEYS
            or matrix.get("explanation") != EXPECTED_EXPLANATION):
        raise ValueError("HANDOFF_DELTA_RELEASE_SAFETY_INVALID")
    gates = matrix.get("gates")
    if not isinstance(gates, list) or len(gates) != len(GATE_OWNERS):
        raise ValueError("HANDOFF_DELTA_GATE_COUNT")
    expected = {gid: owner for gid, owner, _ in GATE_OWNERS}
    if (set(EVIDENCE_REQUIREMENT_BY_GATE) != set(expected)
            or set(WORK_TEST_ONLY_GATES) - set(expected)):
        raise ValueError("HANDOFF_DELTA_REQUIREMENT_MAPPING_MISMATCH")
    if set(EVIDENCE_SOURCE_BY_GATE) != set(expected):
        raise ValueError("HANDOFF_DELTA_CODE_MAPPING_MISMATCH")
    seen = set()
    verified = []
    for gate in gates:
        if not isinstance(gate, dict):
            raise ValueError("HANDOFF_DELTA_GATE_INVALID")
        gid = gate.get("id")
        if not isinstance(gid, str) or gid not in expected or gid in seen:
            raise ValueError("HANDOFF_DELTA_GATE_ID_INVALID")
        seen.add(gid)
        source = EVIDENCE_SOURCE_BY_GATE[gid]
        evidence_class = (
            "WORK_TEST_ONLY" if gid in WORK_TEST_ONLY_GATES
            else "DOCUMENTED_ONLY"
        )
        if (set(gate) != GATE_EXPECTED_KEYS
                or gate.get("owner") != expected[gid]
                or gate.get("reviewed_source_path") != source
                or gate.get("candidate_evidence_class") != evidence_class
                or gate.get("disposition") != "BLOCKED_UNSIGNED"
                or gate.get("independent_signoff_received") is not False
                or gate.get("can_unlock_migration") is not False
                or gate.get("independent_external_evidence_required")
                    != EVIDENCE_REQUIREMENT_BY_GATE[gid]
                or source not in SOURCE_FILES
                or not (root / source).is_file()):
            raise ValueError("HANDOFF_DELTA_GATE_NOT_VERIFIABLE: " + gid)
        verified.append({
            "id": gid,
            "owner": expected[gid],
            "disposition": "BLOCKED_UNSIGNED",
            "candidate_evidence_class": evidence_class,
            "reviewed_source_path": source,
            "reviewed_source_sha256":
                hashlib.sha256((root / source).read_bytes()).hexdigest(),
            "independent_external_evidence_required":
                gate["independent_external_evidence_required"],
            "independent_signoff_received": False,
            "release_authorized": False,
        })
    if seen != set(expected):
        raise ValueError("HANDOFF_DELTA_GATE_MISSING")
    return sorted(verified, key=lambda r: r["id"])


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

    delta = validate_delta_matrix(root)
    checklist = (root / IT_FILE).read_text(encoding="utf-8")
    missing = [gid for gid, _, _ in GATE_OWNERS if gid not in checklist
               and gid not in ("B3-APPROVE", "B4-UAT", "PRODUCTION")]
    if missing:
        raise ValueError("HANDOFF_GATE_UNLISTED: " + ",".join(missing))

    cfg = root / "backend/src/FieldVisit.Api/appsettings.json"
    service = root / "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs"
    if not cfg.is_file() or not service.is_file():
        raise ValueError("HANDOFF_APP_SOURCE_MISSING")
    checked_off, explanation = checked_in_config_is_off(cfg.parent)
    if not checked_off:
        raise ValueError("HANDOFF_FEATURE_NOT_OFF: " + explanation)
    content = service.read_text(encoding="utf-8")
    try:
        require_approve_deny_all(content)
    except ValueError as exc:
        raise ValueError("HANDOFF_APPROVE_NOT_DENY_ALL") from exc

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
        "gate_evidence_delta": delta,
        "gate_evidence_delta_count": len(delta),
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
    lines.extend(("", f"Source-backed delta coverage: {report['gate_evidence_delta_count']}/14; all UNSIGNED.",
                  "Each gate's WORK candidate source hash and missing independent proof are in the JSON artifact.",
                  "Reviewed candidate files are SHA256-bound in the JSON artifact.",
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
