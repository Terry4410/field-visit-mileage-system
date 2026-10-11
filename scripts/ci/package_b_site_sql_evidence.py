#!/usr/bin/env python3
"""Read-only evidence for isolated real SQL Server dated TripContext (NOT B4 GO)."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

PREFIX = "FieldVisit.B3.SqlServer.Tests.B4DatedSiteSqlServerTests."
REQUIRED = frozenset({
    "SQL_SITE_valid_primary_and_cross_org_picker_isolation",
    "SQL_SITE_disabled_site_cannot_create_draft_after_stale_picker",
    "SQL_SITE_expired_membership_rejects_explicit_team",
    "SQL_SITE_duplicate_effective_location_mapping_fails_closed",
    "SQL_SITE_expired_location_link_removes_official_picker_entry",
    "SQL_SITE_rowversion_rejects_stale_concurrent_site_edit",
})
HOLD = {
    "sql_server": "DISPOSABLE_LOCALHOST_14335",
    "official_uat_sql": "NOT_TOUCHED",
    "real_authenticated_sql_http": "NOT_TESTED",
    "formal_migration_1800_011": "NOT_EXECUTED",
    "site_evidence": "PARTIAL_READER_SQL_ONLY",
    "b4_formally_accepted": 0,
    "b3_flag": "OFF",
    "approve_apply": "DENY_ALL",
    "manager_grant": "BLOCKED",
    "business_uat": "HOLD",
    "production": "HARD_HOLD"
}

def validate(path: Path, sha: str | None) -> dict:
    if not path.is_file():
        raise ValueError("B4 SQL site TRX missing")
    if not isinstance(sha,str) or re.fullmatch(r"[0-9a-f]{40}",sha) is None:
        raise ValueError("B4 SQL site source SHA missing/invalid")
    root = ET.parse(path).getroot()
    summary=root.find(".//{*}ResultSummary")
    if summary is None or summary.get("outcome")!="Completed":
        raise ValueError("B4 SQL site TRX not completed")
    if len(root.findall(".//{*}ResultSummary"))!=1:
        raise ValueError("B4 SQL site TRX duplicate summary")
    counters=root.find(".//{*}Counters")
    if counters is None:
        raise ValueError("B4 SQL site counters missing")
    fields=("total","passed","failed","error","notExecuted")
    if any(field not in counters.attrib for field in fields):
        raise ValueError("B4 SQL site counters incomplete")
    values={field:int(counters.get(field,"-1")) for field in fields}
    if (values["total"]!=len(REQUIRED) or values["passed"]!=len(REQUIRED)
        or any(values[x] for x in ("failed","error","notExecuted"))):
        raise ValueError("B4 SQL site tests missing/skipped/failed")
    for field in ("aborted","timeout","inconclusive","notRunnable",
                  "disconnected","pending","inProgress"):
        if int(counters.attrib.get(field,"0")):
            raise ValueError("B4 SQL site run contains incomplete status")
    cases=root.findall(".//{*}UnitTestResult")
    names=[c.get("testName") for c in cases]
    expected={PREFIX+x for x in REQUIRED}
    if(len(cases)!=len(REQUIRED) or set(names)!=expected
       or len(set(names))!=len(names)):
        raise ValueError("B4 SQL site mandatory named cases missing/duplicated")
    caseids=set()
    entries=[]
    for case in cases:
        if case.get("outcome")!="Passed":
            raise ValueError("B4 SQL site case not Passed")
        for attr in ("testId","executionId"):
            identity=case.get(attr)
            if not identity or identity in caseids:
                raise ValueError("B4 SQL site result identity missing/duplicated")
            caseids.add(identity)
        duration=case.get("duration")
        if not duration or re.fullmatch(r"\d{2,}:[0-5]\d:[0-5]\d(?:\.\d{1,7})?",duration) is None:
            raise ValueError("B4 SQL site duration missing/malformed")
        entries.append({"name":case.get("testName"),"outcome":"Passed",
                        "test_id":case.get("testId"),
                        "execution_id":case.get("executionId"),
                        "duration":duration})
    return {"kind":"B4_DATED_SITE_REAL_SQL_PARTIAL_NONRELEASE","status":"PASS",
            "source_sha":sha,"tests_passed":len(cases),
            "tests_total":len(REQUIRED),"verified_case_names":sorted(REQUIRED),
            "trx_sha256":hashlib.sha256(path.read_bytes()).hexdigest(),
            "case_results":sorted(entries,key=lambda x:x["name"]),**HOLD}

def main()->int:
    p=argparse.ArgumentParser()
    p.add_argument("--trx",required=True)
    p.add_argument("--report",required=True)
    p.add_argument("--summary",required=True)
    a=p.parse_args()
    try:
        result=validate(Path(a.trx),os.environ.get("GITHUB_SHA"))
        code=0
    except (OSError,ValueError,ET.ParseError,TypeError) as exc:
        result={"kind":"B4_DATED_SITE_REAL_SQL_PARTIAL_NONRELEASE",
                "status":"FAIL_CLOSED","source_sha":os.getenv("GITHUB_SHA"),
                "reason":str(exc),**HOLD}
        code=1
    path=Path(a.report)
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    lines=[
        "### B4 dated official site — REAL disposable SQL Server / no HTTP",
        "Status: "+result["status"],
        "Source SHA: "+str(result.get("source_sha")),
        "Cases: "+str(result.get("tests_passed",0))+"/"+str(result.get("tests_total",len(REQUIRED))),
        "SQL database: randomly named disposable localhost SQL Server 2022 only.",
        "Actual SQL+HTTP integration: NOT TESTED; B4 formally accepted: 0/22.",
        "011 Migration: NOT EXECUTED. B3 OFF. Approve/Apply DENY ALL.",
        "Manager Grant BLOCKED. Business UAT HOLD. Production HARD HOLD."
    ]
    if "reason" in result:
        lines.append("FAIL CLOSED reason: "+result["reason"])
    Path(a.summary).write_text("\n".join(lines)+"\n",encoding="utf-8")
    print("\n".join(lines))
    return code

if __name__=="__main__":
    sys.exit(main())
