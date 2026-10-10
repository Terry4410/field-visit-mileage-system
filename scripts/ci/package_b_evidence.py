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
    "B3_bounded_payload_before_SQL_transaction",
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


# This is an authorization matrix, not a statement about live SQL/UAT state.
# All release gates remain blocked regardless of offline CI test success.
RELEASE_HOLD_CONTRACT = {
    "manager_grant_provenance": "BLOCKED_OWNER_IT_ATTESTATION",
    "schema_1800_011": "NOT_AUTHORIZED",
    "sql_server_runtime_and_concurrency": "NOT_TESTED",
    "b3_feature_flag": "OFF_REQUIRED",
    "b3_approve_apply": "DENY_ALL_REQUIRED",
    "protected_source_promotion": "NOT_AUTHORIZED",
    "business_uat": "HOLD",
    "production": "HARD_HOLD",
}


REQUIRED_SUCCESSFUL_STEPS = frozenset({
    "checkout", "security_gate", "python_syntax", "python_tests",
    "matrix_gate", "baseline_gate", "setup_dotnet", "setup_node",
    "backend_build", "backend_tests", "frontend_restore",
    "frontend_tests", "frontend_build",
})


def validate_step_outcomes(outcomes: dict) -> dict:
    """A passing test report is insufficient when Build or CI gate was skipped.

    Consumes the GitHub Actions 'steps' context captured prior to aggregation.
    Individual step 'outcome', not a caller-controlled PASS label, must
    explicitly be success. This is still source/CI evidence, not SQL UAT.
    """
    if not isinstance(outcomes, dict):
        raise ValueError("invalid GitHub Actions steps context")
    missing = sorted(REQUIRED_SUCCESSFUL_STEPS - set(outcomes))
    if missing:
        raise ValueError("required CI steps missing: " + ", ".join(missing))
    bad = sorted(step for step in REQUIRED_SUCCESSFUL_STEPS
                 if not isinstance(outcomes[step], dict)
                 or outcomes[step].get("outcome") != "success")
    if bad:
        raise ValueError("CI steps failed, skipped or unverifiable: " + ", ".join(bad))
    return {"required": len(REQUIRED_SUCCESSFUL_STEPS),
            "succeeded": len(REQUIRED_SUCCESSFUL_STEPS)}


# Immutable CI-required B3 OFF TestServer HTTP cases, not live business UAT.
HTTP_PREFIX="FieldVisit.Application.Tests.V180B3OffHttpPipelineTests."
HTTP_REQUIRED=frozenset(
    [f'HTTP_unauthenticated_B3_endpoints_return_401(endpoint: "{ep}")'
     for ep in ("Submit","Mine","Pending","Reject","Approve")]
    + [f'HTTP_authenticated_wrong_role_returns_403_before_B3_disabled(endpoint: "{ep}", role: "{role}")'
       for ep,role in (("Submit","admin"),("Mine","auditor"),
                       ("Pending","visitor"),("Reject","leader"),("Approve","visitor"))]
    + [f'HTTP_authorized_role_receives_503_B3_DISABLED_no_database(endpoint: "{ep}", role: "{role}")'
       for ep,role in (("Submit","visitor"),("Submit","leader"),
                       ("Mine","visitor"),("Mine","admin"),
                       ("Pending","admin"),("Reject","admin"),("Approve","admin"))]
    + ["HTTP_expired_or_wrong_issuer_JWT_cannot_read_B3_queue(expired: True, wrongIssuer: False)",
       "HTTP_expired_or_wrong_issuer_JWT_cannot_read_B3_queue(expired: False, wrongIssuer: True)"]
    + [f'HTTP_bad_JWT_variant_rejected_with_401_before_disabled_gate(variant: "{variant}")'
       for variant in ("wrong-audience","wrong-signature","not-yet-valid","malformed")]
    + [f'HTTP_wrong_method_cannot_bypass_endpoint_role_or_feature_gate(action: "{action}")'
       for action in ("locations","mine","admin/pending","admin/reject","admin/approve")])


def validate_http_off_trx(path: Path) -> dict:
    root=ET.parse(path).getroot()
    tests=[x for x in root.findall(".//{*}UnitTestResult")
           if x.get("testName","").startswith(HTTP_PREFIX)]
    expected={HTTP_PREFIX+name for name in HTTP_REQUIRED}
    observed=[x.get("testName") for x in tests]
    if len(tests)!=len(expected) or set(observed)!=expected or len(set(observed))!=len(observed):
        raise ValueError("B3 HTTP mandatory named cases missing, duplicated or replaced")
    for x in tests:
        if x.get("outcome")!="Passed" or not x.get("testId") or not x.get("executionId"):
            raise ValueError("B3 HTTP test outcome or evidence ID missing/failed")
    return {"status":"PASS", "scope":"ISOLATED_TESTSERVER_JWT_B3_OFF",
            "passed":len(tests),"required":len(expected),
            "source":"backend TRX individually named HTTP cases",
            "api_off_08_hr_runtime":"NOT_TESTED",
            "business_uat":"HOLD"}


def parse_trx(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    counters = root.find(".//{*}Counters")
    if counters is None:
        raise ValueError("TRX Counters missing")
    required = ("total", "passed", "failed", "error", "notExecuted")
    if any(key not in counters.attrib for key in required):
        raise ValueError("TRX required counters missing")
    data = {key: int(counters.attrib[key]) for key in required}
    if any(n < 0 or n > data["total"] for n in data.values()):
        raise ValueError("TRX counters invalid or negative")
    return data


def parse_junit(path: Path) -> dict[str, int]:
    root = ET.parse(path).getroot()
    def tag_name(element: ET.Element) -> str:
        return element.tag.rsplit("}", 1)[-1]
    suites = [node for node in root.iter() if tag_name(node) == "testsuite"]
    if not suites:
        raise ValueError("JUnit suites missing")
    # Leaf suites own the testcases; aggregate suites must not double count.
    leaves = [suite for suite in suites
              if not any(tag_name(node) == "testsuite"
                         for node in suite.iter() if node is not suite)]
    data = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
    for suite in leaves:
        n = int(suite.get("tests", "-1"))
        failures = int(suite.get("failures", "0")) + int(suite.get("errors", "0"))
        skipped = int(suite.get("skipped", "0"))
        cases = [node for node in suite.iter() if tag_name(node) == "testcase"]
        if n < 0 or failures < 0 or skipped < 0 or n != len(cases):
            raise ValueError("JUnit testcase count does not match reported suite")
        actual_failed = sum(any(tag_name(c) in ("failure", "error")
                                for c in case.iter() if c is not case)
                            for case in cases)
        actual_skipped = sum(any(tag_name(c) == "skipped"
                                 for c in case.iter() if c is not case)
                             for case in cases)
        if failures != actual_failed or skipped != actual_skipped:
            raise ValueError("JUnit testcase statuses do not match suite counters")
        data["total"] += n
        data["failed"] += failures
        data["skipped"] += skipped
        data["passed"] += n - failures - skipped
    return data


def collect(guard_path: Path, trx: Path, junit: Path, matrix: Path,
            step_outcomes: dict | None = None,
            require_http_off: bool = False) -> dict:
    evidence = {"gate": "PACKAGE_B_B3_B4_CI_EVIDENCE",
                "result": "FAIL_CLOSED", "guard": None,
                "backend": None, "frontend": None, "b4_matrix": None,
                "http_off": None,
                "build_and_ci_steps": None,
                "release_authorization": dict(RELEASE_HOLD_CONTRACT),
                "release_go": False,
                "errors": []}
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
    if require_http_off:
        try:
            evidence["http_off"]=validate_http_off_trx(trx)
            evidence["http_off"]["source_sha"]=evidence["guard"]["sha"]
        except (OSError,ValueError,KeyError,TypeError,ET.ParseError) as exc:
            evidence["errors"].append("B3 HTTP OFF: "+str(exc))
    try:
        data = parse_junit(junit)
        if data["total"] <= 0 or data["failed"] or data["skipped"] or \
                data["passed"] != data["total"]:
            raise ValueError("frontend tests missing, failing or skipped: " + str(data))
        evidence["frontend"] = data
    except (OSError, ValueError, ET.ParseError) as exc:
        evidence["errors"].append("frontend: " + str(exc))
    try:
        matrix_data = json.loads(matrix.read_text(encoding="utf-8"))
        expected_ids = [f"B4-SQL-{i:02d}" for i in range(1, 23)]
        observed = matrix_data.get("ids")
        if (matrix_data.get("passed") is not True
                or matrix_data.get("status") != "DOCUMENTED_ONLY_SQL_RUNTIME_NOT_TESTED"
                or matrix_data.get("present") != 22
                or matrix_data.get("required") != 22
                or not isinstance(observed, list)
                or observed != expected_ids
                or matrix_data.get("candidate_sha") != evidence["guard"]["sha"]):
            raise ValueError("B4 runtime HOLD matrix missing, forged or SHA mismatch")
        evidence["b4_matrix"] = {"documented": 22,
                                 "sql_server_runtime": "NOT_TESTED"}
    except (OSError, ValueError, KeyError, TypeError) as exc:
        evidence["errors"].append("B4 matrix: " + str(exc))
    if step_outcomes is not None:
        try:
            evidence["build_and_ci_steps"] = validate_step_outcomes(step_outcomes)
        except (ValueError, TypeError) as exc:
            evidence["errors"].append("CI step outcomes: " + str(exc))
    if not evidence["errors"]:
        evidence["result"] = "PASS"
    evidence["notice"] = "No SQL Server runtime or migration verification. Full UAT and Production HOLD."
    return evidence


def main() -> int:
    p = argparse.ArgumentParser()
    for key in ("guard", "trx", "junit", "matrix", "steps-json", "output", "summary"):
        p.add_argument("--" + key, required=True)
    args = p.parse_args()
    try:
        steps = json.loads(args.steps_json)
    except (TypeError, ValueError) as exc:
        steps = {"invalid": {"outcome": f"invalid JSON: {exc}"}}
    result = collect(Path(args.guard), Path(args.trx), Path(args.junit),
                     Path(args.matrix), step_outcomes=steps,
                     require_http_off=True)
    target = Path(args.output)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n",
                      encoding="utf-8")
    lines = ["### Package B consolidated automated test evidence",
             "Offline CI status: **" + result["result"] + "**",
             "Release authorization: **HARD HOLD — NOT GO**",
             "These are code/CI checks, not SQL Server runtime, Business UAT, or deployment approval.",
             "",
             "| Evidence | Result |", "| --- | --- |"]
    if result["guard"]:
        lines.append("| Protected + feature/approval/migration guard | " +
                     str(result["guard"]["passed_checks"]) + " checks PASS |")
    if result["build_and_ci_steps"]:
        s = result["build_and_ci_steps"]
        lines.append("| CI build / test / hard-hold steps | " +
                     str(s["succeeded"]) + "/" + str(s["required"]) +
                     " mandatory steps successful |")
    for name in ("backend", "frontend"):
        if result[name]:
            x = result[name]
            lines.append("| " + name + " | " + str(x["passed"]) + "/" +
                         str(x["total"]) + " PASS |")
    if result["http_off"]:
        h=result["http_off"]
        lines.append("| B3 OFF JWT HTTP (isolated, no DB) | "+
                     str(h["passed"])+"/"+str(h["required"])+
                     " PASS; API-OFF-08 outstanding |")
    if result["b4_matrix"]:
        lines.append("| B4 SQL runtime checklist (documentation only) | "
                     "22/22 documented; NOT executed on SQL Server |")
    for name, status in result["release_authorization"].items():
        lines.append("| Release gate: " + name + " | " + status + " |")
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
