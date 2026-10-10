#!/usr/bin/env python3
"""Fail-closed, READ-ONLY B3/B4 CI guard for the isolated WORK branch."""
import argparse
import datetime
import json
import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
FROZEN = "68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2"
BASELINE = "c1ee3c0c3df000491fd54df727482815d3c38917"
BRANCH = "refs/heads/work/v180-package-b-b2-b3-controlled"

def git(*arguments):
    return subprocess.check_output(
        ["git", *arguments], cwd=ROOT, text=True, stderr=subprocess.STDOUT
    ).strip()

def scan():
    results = []
    def gate(name, good, details):
        results.append({"name": name, "passed": bool(good), "details": str(details)})
    try:
        head = git("rev-parse", "HEAD")
        ancestor = subprocess.run(
            ["git", "merge-base", "--is-ancestor", BASELINE, head],
            cwd=ROOT, capture_output=True, check=False)
        gate("baseline_ancestry", ancestor.returncode == 0,
             "HEAD=" + head + "; ancestor=" + BASELINE)
    except (subprocess.CalledProcessError, OSError) as exc:
        gate("baseline_ancestry", False, exc)
    try:
        protected = git("rev-parse", "refs/remotes/origin/post-uat/v1.8.0")
        gate("protected_source_frozen", protected == FROZEN,
             "observed=" + protected + "; expected=" + FROZEN)
    except (subprocess.CalledProcessError, OSError) as exc:
        gate("protected_source_frozen", False, exc)
    gh_ref = os.environ.get("GITHUB_REF")
    gate("work_branch_only", not gh_ref or gh_ref == BRANCH,
         "GITHUB_REF=" + str(gh_ref))
    try:
        config = json.loads((ROOT / "backend/src/FieldVisit.Api/appsettings.json")
                            .read_text(encoding="utf-8"))
        enabled = config["PackageB"]["B3"]["Enabled"]
        gate("feature_flag_off", enabled is False, "B3 Enabled=" + str(enabled))
    except (KeyError, ValueError, OSError, TypeError) as exc:
        gate("feature_flag_off", False, exc)
    try:
        service = (ROOT / "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs").read_text(encoding="utf-8")
        approval = service[service.index("public async Task<V180B3RequestView> ApproveAsync("):]
        allowed = ('throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED")' in approval
                   and "db.SaveChanges" not in approval
                   and "db.ChangeRequestEvents.Add" not in approval
                   and "db.Locations.Update" not in approval)
        gate("B3_approve_executor_DENY_ALL", allowed, "Approve contains no DB writes")
    except (ValueError, OSError) as exc:
        gate("B3_approve_executor_DENY_ALL", False, exc)
    # Prevent future refactors from moving large untrusted DTO parsing into
    # SQL transactions or allowing malformed payloads to perform DB queries.
    try:
        source = service
        submit_start = source.index("public async Task<V180B3RequestView> SubmitAsync(")
        mine_start = source.index("public async Task<IReadOnlyList<V180B3RequestView>> MineAsync(")
        reject_start = source.index("public async Task<V180B3RequestView> RejectAsync(")
        approve_start = source.index("public async Task<V180B3RequestView> ApproveAsync(")
        submit = source[submit_start:mine_start]
        reject = source[reject_start:approve_start]
        def safe_order(body: str, preflight: str) -> bool:
            ready = body.index("await ReadyAsync(ct)")
            parsed = body.index(preflight)
            tx = body.index("BeginTransactionAsync(")
            live = body.index("LiveActorAsync(ct)")
            return ready < parsed < tx < live
        entry = ROOT / "backend/src/FieldVisit.Infrastructure/V180B3RequestInputRules.cs"
        rules = entry.read_text(encoding="utf-8")
        safe = (safe_order(submit, "V180B3RequestInputRules.RequireSubmission(input)")
                and safe_order(reject, "V180B3RequestInputRules.RequireReviewTarget(id,input)")
                and "Oversize(input.Proposed.LocationName,200)" in rules
                and "Oversize(input.Reason,1000)" in rules
                and "V180B3RowVersionRules.Parse(input.ExpectedRowVersion)" in rules
                and "V180B3RowVersionRules.Parse(input.RequestRowVersion)" in rules)
        gate("B3_bounded_payload_before_SQL_transaction", safe,
             "flag/schema -> bounded DTO preflight -> transaction -> live actor")
    except (OSError, ValueError) as exc:
        gate("B3_bounded_payload_before_SQL_transaction", False, exc)
    migration_dir = ROOT / "database/migrations"
    gate("migration_dir_present", migration_dir.is_dir(), migration_dir)
    # No previously approved migration may be edited to hide unapproved DDL.
    try:
        changed_sql = git("diff", "--name-only", BASELINE, "HEAD", "--",
                          "database/migrations")
        gate("all_migrations_unchanged_since_baseline", not bool(changed_sql),
             "changed migration files: " + str(changed_sql.splitlines()))
        changed_workflows = git("diff", "--name-only", BASELINE, "HEAD", "--",
                                ".github/workflows")
        unapproved = [p for p in changed_workflows.splitlines()
                      if p != ".github/workflows/package-b-b2-b3-controlled-verify.yml"]
        gate("no_unapproved_workflow_edits_since_baseline", not unapproved, unapproved)
    except (subprocess.CalledProcessError, OSError) as exc:
        gate("no_migration_or_workflow_release_changes", False, exc)
    forbidden = sorted(str(p.relative_to(ROOT)) for p in migration_dir.rglob("*")
                       if p.is_file() and re.search(r"1800[_-]011", p.name, re.I))
    gate("no_executable_011_migration", not forbidden, forbidden)
    workflow_dir = ROOT / ".github/workflows"
    gate("workflow_dir_present", workflow_dir.is_dir(), workflow_dir)
    dispatch = sorted(str(p.relative_to(ROOT)) for p in workflow_dir.glob("*")
                      if p.is_file() and re.search(r"1800[_-]011", p.name, re.I))
    gate("no_011_deploy_workflow", not dispatch, dispatch)
    # Reject a disguised workflow that dispatches 011 using a neutral filename.
    embedded = sorted(str(p.relative_to(ROOT)) for p in workflow_dir.glob("*")
                      if p.is_file() and p.suffix in (".yml", ".yaml")
                      and re.search(r"1800[_-]011", p.read_text(encoding="utf-8"), re.I))
    gate("no_hidden_011_workflow_dispatch", not embedded, embedded)
    try:
        yml = (workflow_dir / "package-b-b2-b3-controlled-verify.yml").read_text(encoding="utf-8")
        valid = ("contents: read" in yml and "persist-credentials: false" in yml
                 and "python3 scripts/ci/package_b_guard.py" in yml
                 and "if-no-files-found: error" in yml
                 and "include-hidden-files: true" in yml
                 and not re.search(r"azure/login|\bsqlcmd\b|dotnet ef database update", yml, re.I))
        gate("CI_readonly_no_deploy", valid, "readonly workflow restrictions")
    except OSError as exc:
        gate("CI_readonly_no_deploy", False, exc)
    return results

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    parser.add_argument("--summary", required=True)
    args = parser.parse_args()
    rows = scan()
    passed = all(item["passed"] for item in rows)
    head = git("rev-parse", "HEAD")
    document = {"kind": "PACKAGE_B_B3_B4_NONDEPLOY",
                "timestamp_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "head_sha": head, "frozen_sha": FROZEN,
                "passed": passed, "checks": rows,
                "notice": "Offline source evidence only; SQL/runtime/approvals remain HOLD."}
    report = ROOT / args.report
    summary = ROOT / args.summary
    report.parent.mkdir(parents=True, exist_ok=True)
    summary.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    lines = ["### Package B security-gate evidence", "Candidate: " + head,
             "Protected: " + FROZEN,
             "Outcome: " + ("PASS" if passed else "FAIL CLOSED"), "",
             "| Check | Result | Evidence |", "| --- | --- | --- |"]
    for item in rows:
        result = "PASS" if item["passed"] else "FAIL"
        details = item["details"].replace("|", "/").replace("\n", " ")
        lines.append("| " + item["name"] + " | " + result + " | " + details + " |")
        print(result + " " + item["name"] + " " + details)
    lines.append("")
    lines.append("NO DB, no migration, no feature activation, no deploy. SQL/runtime remains HOLD.")
    summary.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0 if passed else 1

if __name__ == "__main__":
    sys.exit(main())
