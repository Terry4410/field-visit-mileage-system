#!/usr/bin/env python3
"""Fail-closed OFFLINE completeness check for B4 SQL runtime UAT matrix.

Document inspection only: an indexed scenario is NOT an executed test.
"""
import argparse
import json
import os
from pathlib import Path
import re
import sys

EXPECTED_IDS = frozenset(f"B4-SQL-{i:02d}" for i in range(1, 23))
ROW_RE = re.compile(
    r"^\|\s*(B4-SQL-\d{2})\s*\|\s*([^|]+)\|\s*([^|]+)\|", re.M
)


def validate_matrix(markdown: str) -> dict:
    if "NOT YET PASSED in SQL Server runtime" not in markdown:
        raise ValueError("document falsely suggests SQL Server runtime completion")
    if "UAT and Production remain HOLD" not in markdown:
        raise ValueError("missing runtime HOLD declaration")
    if "NONEXECUTABLE REVIEW MATRIX" not in markdown:
        raise ValueError("missing non-execution disclaimer")
    cases = ROW_RE.findall(markdown)
    ids = [case[0] for case in cases]
    if len(ids) != len(set(ids)):
        raise ValueError("duplicate B4 SQL runtime scenario IDs")
    missing = EXPECTED_IDS - set(ids)
    extra = set(ids) - EXPECTED_IDS
    if missing or extra:
        raise ValueError(f"B4 matrix IDs mismatch: missing={sorted(missing)}, extra={sorted(extra)}")
    if any(len(scenario.strip()) < 10 or len(expected.strip()) < 10
           for _, scenario, expected in cases):
        raise ValueError("incomplete runtime test scenario or expected invariant")
    return {
        "passed": True,
        "required": len(EXPECTED_IDS),
        "present": len(ids),
        "ids": sorted(ids),
        "status": "DOCUMENTED_ONLY_SQL_RUNTIME_NOT_TESTED",
        "notice": "Not a SQL Server result. No approval, migration, deploy or GO.",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--summary", required=True)
    args = parser.parse_args()
    candidate_sha = os.environ.get("GITHUB_SHA", "")
    try:
        result = validate_matrix(Path(args.input).read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        result = {
            "passed": False,
            "status": "FAIL_CLOSED",
            "reason": str(error),
            "notice": "IT/Owner SQL Server runtime still HOLD.",
        }
    result["candidate_sha"] = candidate_sha
    output = Path(args.report)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                      encoding="utf-8")
    summary = Path(args.summary)
    summary.parent.mkdir(parents=True, exist_ok=True)
    summary.write_text(
        "### B4 SQL runtime acceptance matrix (DOCUMENT-ONLY)\n\n"
        + ("**22/22 scenario definitions checked**" if result["passed"]
           else "**FAIL CLOSED: " + result.get("reason", "unknown") + "**")
        + "\n\n**NOT TESTED on SQL Server. Full UAT and Production HOLD.**\n",
        encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
