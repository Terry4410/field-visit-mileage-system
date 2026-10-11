#!/usr/bin/env python3
"""Fail-closed exact REAL HTTP JWT + ephemeral SQL TRX; NEVER a B4 release grant."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

PREFIX="FieldVisit.B4.SqlHttp.Tests.B4RealSqlHttpPipelineTests."
REQUIRED=frozenset({
    "SQL_HTTP_valid_JWT_role_membership_eligible_site_from_real_SQL",
    "SQL_HTTP_site_disabled_after_JWT_auth_fails_closed_for_stale_site",
    "SQL_HTTP_role_revoked_after_JWT_issue_denies_old_token_403",
    "SQL_HTTP_team_revoked_after_JWT_issue_denies_old_token_403",
    "SQL_HTTP_HR_leave_after_JWT_issue_denies_old_token_403",
    "SQL_HTTP_disconnected_SQL_500_generic_no_access_or_business_write",
    "SQL_HTTP_DRAFT_create_writes_real_SQL_trip_history_and_audit",
    "SQL_HTTP_DRAFT_disabled_site_rejects_update_no_extra_SQL_writes",
    "SQL_HTTP_DRAFT_disabled_site_rejects_submit_and_preserves_history",
    "SQL_HTTP_DRAFT_stale_RowVersion_does_not_create_new_audit",
    "SQL_HTTP_DRAFT_competing_same_RowVersion_updates_only_one_winner",
    "SQL_HTTP_DRAFT_forged_official_site_rejected_without_trip_or_audit",
})
RELEASE_HOLD={
    "runtime_scope":"ACTUAL_ASPNETCORE_APPJWT_AND_LOCAL_SQL2022",
    "database":"DISPOSABLE_LOCALHOST_14335_MASTER_ONLY",
    "azure_uat_prod":"NOT_TOUCHED",
    "formal_b4_accepted":0,
    "011_migration":"NOT_AUTHORIZED",
    "b3_flag":"OFF",
    "approve_apply":"DENY_ALL",
    "manager_grant":"BLOCKED",
    "business_uat":"HOLD",
    "production":"HARD_HOLD",
}

def validate(path:Path,sha:str|None)->dict:
    if not path.is_file():
        raise ValueError("SQL HTTP actual TRX missing")
    if not isinstance(sha,str) or re.fullmatch(r"[0-9a-f]{40}",sha) is None:
        raise ValueError("SQL HTTP GitHub SHA invalid or missing")
    root=ET.parse(path).getroot()
    summaries=root.findall(".//{*}ResultSummary")
    if len(summaries)!=1 or summaries[0].get("outcome")!="Completed":
        raise ValueError("SQL HTTP actual TRX result summary incomplete/duplicate")
    counters=summaries[0].find(".//{*}Counters")
    if counters is None or any(x not in counters.attrib
        for x in ("total","passed","failed","error","notExecuted")):
        raise ValueError("SQL HTTP actual TRX counters missing")
    vals={x:int(counters.get(x,"-1")) for x in
          ("total","passed","failed","error","notExecuted")}
    if (vals["total"]!=len(REQUIRED) or vals["passed"]!=len(REQUIRED)
        or any(vals[x] for x in ("failed","error","notExecuted"))):
        raise ValueError("SQL HTTP tests missing/failed/skipped")
    if any(int(counters.attrib.get(x,"0"))!=0 for x in
           ("aborted","timeout","inconclusive","notRunnable","disconnected",
            "passedButRunAborted","pending","inProgress")):
        raise ValueError("SQL HTTP TRX aborted/inconclusive/timeout")
    cases=root.findall(".//{*}UnitTestResult")
    names=[x.get("testName") for x in cases]
    expected={PREFIX+x for x in REQUIRED}
    if (len(cases)!=len(REQUIRED) or set(names)!=expected
        or len(set(names))!=len(names)):
        raise ValueError("SQL HTTP mandatory named cases missing/duplicated/replaced")
    identities=set()
    result=[]
    for case in cases:
        if case.get("outcome")!="Passed":
            raise ValueError("SQL HTTP mandatory case not Passed")
        for key in ("testId","executionId"):
            ident=case.get(key)
            if not ident or ident in identities:
                raise ValueError("SQL HTTP individual test/execution IDs missing/duplicated")
            identities.add(ident)
        duration=case.get("duration")
        if duration is None or re.fullmatch(
            r"\d{2,}:[0-5]\d:[0-5]\d(?:\.\d{1,7})?",duration) is None:
            raise ValueError("SQL HTTP per-case duration invalid")
        result.append({"name":case.get("testName"),"outcome":"Passed",
                       "test_id":case.get("testId"),"execution_id":case.get("executionId"),
                       "duration":duration})
    return {"kind":"B4_REAL_SQL_HTTP_ISOLATED_NONRELEASE","status":"PASS",
            "source_sha":sha,"passed":len(cases),"required":len(REQUIRED),
            "trx_sha256":hashlib.sha256(path.read_bytes()).hexdigest(),
            "case_results":sorted(result,key=lambda x:x["name"]),**RELEASE_HOLD}

def main()->int:
    cli=argparse.ArgumentParser()
    cli.add_argument("--trx",required=True)
    cli.add_argument("--report",required=True)
    cli.add_argument("--summary",required=True)
    a=cli.parse_args()
    try:
        result=validate(Path(a.trx),os.getenv("GITHUB_SHA"))
        code=0
    except (OSError,ValueError,TypeError,ET.ParseError) as exc:
        result={"kind":"B4_REAL_SQL_HTTP_ISOLATED_NONRELEASE","status":"FAIL_CLOSED",
                "source_sha":os.getenv("GITHUB_SHA"),"reason":str(exc),**RELEASE_HOLD}
        code=1
    output=Path(a.report)
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    lines=[
        "### Actual App JWT + real disposable SQL Server 2022 HTTP integration",
        "Status: "+result["status"],"Source SHA: "+str(result.get("source_sha")),
        "Cases: "+str(result.get("passed",0))+"/"+str(len(REQUIRED)),
        "Includes old JWT role/team/HR revocation, dated-site HTTP, outage, draft lifecycle, and competing RowVersion.",
        "Only GitHub localhost disposable DB. This is NOT UAT/Production SQL or formal B4 acceptance.",
        "B4 accepted 0/22, B3 OFF, 011 NOT AUTHORIZED, Approve/Apply DENY ALL.",
        "Manager Grant BLOCKED, Business UAT HOLD, Production HARD HOLD."
    ]
    if "reason" in result:lines.append("FAIL CLOSED: "+result["reason"])
    Path(a.summary).write_text("\n".join(lines)+"\n",encoding="utf-8")
    print("\n".join(lines))
    return code

if __name__=="__main__":
    sys.exit(main())
