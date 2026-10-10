#!/usr/bin/env python3
"""Validate real SQL Server disposable-fixture TRX. Never authorize UAT GO."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

def validate(path: Path, sha: str | None = None) -> dict:
    if not path.is_file():
        raise ValueError("SQL Server test TRX missing")
    root = ET.parse(path).getroot()
    counters = root.find(".//{*}Counters")
    if counters is None:
        raise ValueError("SQL Server test TRX counters missing")
    names = ("total", "passed", "failed", "error", "notExecuted")
    if any(name not in counters.attrib for name in names):
        raise ValueError("SQL Server test TRX counters incomplete")
    results = {k: int(counters.attrib[k]) for k in names}
    if (results["total"] < 8 or results["passed"] != results["total"]
            or any(results[k] for k in ("failed","error","notExecuted"))):
        raise ValueError("SQL Server tests missing, skipped or failed: " + str(results))
    cases = root.findall(".//{*}UnitTestResult")
    if len(cases) != results["total"]:
        raise ValueError("SQL Server TRX per-test count mismatch")
    if any(c.get("outcome") != "Passed" for c in cases):
        raise ValueError("SQL Server TRX includes a non-passing test")
    if sha is not None and not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("invalid GitHub source SHA")
    return {
        "kind": "B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE",
        "status": "PASS",
        "source_sha": sha,
        "tests_passed": results["passed"],
        "tests_total": results["total"],
        "scope": "DISPOSABLE_LOCALHOST_SQL_SERVER_2022",
        "full_22_case_b4_sql_runtime": "NOT_COMPLETE",
        "formal_schema_migration": "NOT_EXECUTED",
        "existing_uat_or_production_database": "NOT_TOUCHED",
        "b3_approve_apply": "DENY_ALL",
        "business_uat": "HOLD",
        "production": "HARD_HOLD"
    }

def main() -> int:
    parser=argparse.ArgumentParser()
    parser.add_argument("--trx",required=True)
    parser.add_argument("--report",required=True)
    parser.add_argument("--summary",required=True)
    args=parser.parse_args()
    try:
        result=validate(Path(args.trx),os.getenv("GITHUB_SHA"))
        code=0
    except (OSError,ValueError,ET.ParseError) as exc:
        result={"kind":"B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE",
                "status":"FAIL_CLOSED","source_sha":os.getenv("GITHUB_SHA"),
                "error":str(exc),"business_uat":"HOLD","production":"HARD_HOLD"}
        code=1
    out=Path(args.report)
    md=Path(args.summary)
    out.parent.mkdir(parents=True,exist_ok=True)
    md.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    lines=["### B3/B4 actual disposable SQL Server tests",
           "Status: "+result["status"],
           "Source: "+str(result.get("source_sha")),
           "Cases: "+str(result.get("tests_passed",0))+
              "/"+str(result.get("tests_total",0)),
           "Scope: isolated localhost SQL Server 2022 only.",
           "Formal 011 migration: NOT EXECUTED. Full B4/API UAT: NOT COMPLETE.",
           "Business UAT: HOLD. Production: HARD HOLD."]
    if result.get("error"):
        lines.append("Error: "+result["error"])
    md.write_text("\n".join(lines)+"\n",encoding="utf-8")
    print("\n".join(lines))
    return code

if __name__=="__main__":
    sys.exit(main())
