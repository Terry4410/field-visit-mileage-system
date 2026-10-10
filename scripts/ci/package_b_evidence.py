#!/usr/bin/env python3
"""Consolidate offline B3/B4 guard + xUnit TRX + Vitest JUnit evidence.

No network/DDL/deployment. Missing or failing proof => fail closed.
"""
from __future__ import annotations

import argparse
import json
import os
import re
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

FROZEN_PROTECTED_SHA = "68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2"
REQUIRED_SECURITY_GATES = frozenset({
    "baseline_ancestry",
    "protected_source_frozen",
    "work_branch_only",
    "feature_flag_off",
    "B3_approve_executor_DENY_ALL",
    "migration_dir_present",
    "all_migrations_unchanged_since_baseline",
    "no_unapproved_workflow_edits_since_baseline",
    "no_executable_011_migration",
    "workflow_dir_present",
    "no_011_deploy_workflow",
    "no_hidden_011_workflow_dispatch",
    "CI_readonly_no_deploy",
})


def validate_guard(guard: dict, github_sha: str | None = None) -> dict:
    """Validate *all* mandatory controls, not a self-asserted aggregate PASS."""
    if not isinstance(guard, dict) or guard.get("kind") != "PACKAGE_B_B3_B4_NONDEPLOY":
        raise ValueError("unexpected guard report format")
    if guard.get("passed") is not True:
        raise ValueError("B3/B4 hard-hold security preflight failed")
    if guard.get("frozen_sha") != FROZEN_PROTECTED_SHA:
        raise ValueError("frozen protected SHA is incorrect")
    head = guard.get("head_sha")
    if not isinstance(head, str) or not re.fullmatch(r"[0-9a-f]{40}", head):
        raise ValueError("invalid candidate commit SHA")
    if github_sha and github_sha != head:
        raise ValueError("guard SHA does not match GitHub run")
    checks = guard.get("checks")
    if not isinstance(checks, list):
        raise ValueError("missing guard checks")
    names = [row.get("name") for row in checks if isinstance(row, dict)]
    if len(names) != len(checks) or len(names) != len(set(names)):
        raise ValueError("duplicate or malformed security check names")
    missing = REQUIRED_SECURITY_GATES - set(names)
    extra = set(names) - REQUIRED_SECURITY_GATES
    if missing or extra:
        raise ValueError(f"guard coverage mismatch: missing={sorted(missing)}, extra={sorted(extra)}")
    if not all(row.get("passed") is True for row in checks):
        raise ValueError("one or more mandatory security gates failed")
    return {
        "sha": head,
        "passed_checks": len(checks),
        "protected_sha": guard["frozen_sha"],
    }


def parse_trx(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    counters = root.find(".//{*}Counters")
    if counters is None:
        raise ValueError("TRX Counters missing")
    return {key: int(counters.attrib.get(key, "0"))
            for key in ("total", "passed", "failed", "error", "notExecuted")}


def parse_junit(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    suites = [root] if root.tag == "testsuite" else root.findall(".//testsuite")
    if not suites:
        raise ValueError("JUnit suites missing")
    # Count leaf suites to avoid double counting aggregate parent suites.
    leaves = [s for s in suites if not s.findall("testsuite")]
    if not leaves:
        raise ValueError("JUnit leaf suites missing")
    data = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
    for suite in leaves:
        n = int(suite.get("tests", "0"))
        failures = int(suite.get("failures", "0")) + int(suite.get("errors", "0"))
        skipped = int(suite.get("skipped", "0"))
        data["total"] += n
        data["failed"] += failures
        data["skipped"] += skipped
        data["passed"] += n - failures - skipped
    return data


def collect(guard_path: Path, trx: Path, junit: Path) -> dict:
    evidence = {"gate": "PACKAGE_B_B3_B4_CI_EVIDENCE",
                "result": "FAIL_CLOSED", "guard": None,
                "backend": None, "frontend": None, "errors": []}
    try:
        guard = json.loads(guard_path.read_text(encoding="utf-8"))
        evidence["guard"] = validate_guard(guard, os.environ.get("GITHUB_SHA"))
    except (OSError, ValueError, KeyError, TypeError) as exc:
        evidence["errors"].append("guard: " + str(exc))
    try:
        data = parse_trx(trx)
        if data["total"] <= 0 or data["failed"] or data["error"] or \
                data["passed"] != data["total"]:
            raise ValueError("backend tests missing, failing or skipped: " + str(data))
        evidence["backend"] = data
    except (OSError, ValueError, ET.ParseError) as exc:
        evidence["errors"].append("backend: " + str(exc))
    try:
        data = parse_junit(junit)
        if data["total"] <= 0 or data["failed"] or data["skipped"] or \
                data["passed"] != data["total"]:
            raise ValueError("frontend tests missing, failing or skipped: " + str(data))
        evidence["frontend"] = data
    except (OSError, ValueError, ET.ParseError) as exc:
        evidence["errors"].append("frontend: " + str(exc))
    if not evidence["errors"]:
        evidence["result"] = "PASS"
    evidence["notice"] = "No SQL Server runtime or migration verification. Full UAT and Production HOLD."
    return evidence


def main() -> int:
    p = argparse.ArgumentParser()
    for key in ("guard", "trx", "junit", "output", "summary"):
        p.add_argument("--" + key, required=True)
    args = p.parse_args()
    result = collect(Path(args.guard), Path(args.trx), Path(args.junit))
    target = Path(args.output)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n",
                      encoding="utf-8")
    lines = ["### Package B consolidated automated test evidence",
             "Status: **" + result["result"] + "**", "",
             "| Evidence | Result |", "| --- | --- |"]
    if result["guard"]:
        lines.append("| Protected + feature/approval/migration guard | " +
                     str(result["guard"]["passed_checks"]) + " checks PASS |")
    for name in ("backend", "frontend"):
        if result[name]:
            x = result[name]
            lines.append("| " + name + " | " + str(x["passed"]) + "/" +
                         str(x["total"]) + " PASS |")
    for error in result["errors"]:
        lines.append("| Gate failure | " + error.replace("|", "/") + " |")
    lines.extend(["", "Offline CI only. 011 migration, live SQL, B3 apply, promotion, "
                          "deployment and Production remain HARD HOLD."])
    report = Path(args.summary)
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if result["result"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
