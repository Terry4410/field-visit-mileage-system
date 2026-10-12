import json
import os
import tempfile
import unittest
from pathlib import Path

from package_b_evidence import (collect, parse_junit, parse_trx,
                                REQUIRED_SECURITY_GATES, FROZEN_PROTECTED_SHA,
                                REQUIRED_SUCCESSFUL_STEPS, validate_step_outcomes,
                                RELEASE_HOLD_CONTRACT, HTTP_REQUIRED,
                                HTTP_PREFIX,validate_http_off_trx,
                                HR_HTTP_PREFIX,HR_HTTP_REQUIRED,validate_hr_login_http_trx,
                                SESSION_HTTP_PREFIX,SESSION_HTTP_REQUIRED,validate_session_guard_http_trx,
                                ROLE_HTTP_PREFIX,ROLE_HTTP_REQUIRED,validate_role_http_trx,
                                TEAM_SCOPE_HTTP_PREFIX,TEAM_HTTP_REQUIRED,
                                SUPERVISOR_SCOPE_HTTP_REQUIRED,validate_team_scope_http_trx,
                                SITE_RUNTIME_PREFIX,SITE_RUNTIME_REQUIRED,validate_dated_site_runtime_trx,
                                SITE_HTTP_PREFIX,SITE_HTTP_REQUIRED,validate_site_http_trx)


class PackageBEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name)
        self.guard = base / "guard.json"
        self.trx = base / "backend.trx"
        self.junit = base / "frontend.xml"
        self.matrix = base / "matrix.json"
        self.matrix.write_text(json.dumps({
            "passed": True, "status": "DOCUMENTED_ONLY_SQL_RUNTIME_NOT_TESTED",
            "candidate_sha": os.environ.get("GITHUB_SHA") or ("a" * 40),
            "present": 22, "required": 22,
            "ids": [f"B4-SQL-{i:02d}" for i in range(1, 23)]
        }))
        self.guard.write_text(json.dumps({
            "kind": "PACKAGE_B_B3_B4_NONDEPLOY",
            "head_sha": os.environ.get("GITHUB_SHA") or ("a" * 40),
            "frozen_sha": FROZEN_PROTECTED_SHA,
            "passed": True,
            "checks": [{"name": name, "passed": True}
                       for name in sorted(REQUIRED_SECURITY_GATES)]}))
        self.trx.write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '<ResultSummary><Counters total="2" passed="2" failed="0" error="0" '
            'notExecuted="0"/></ResultSummary></TestRun>')
        self.junit.write_text('<testsuites><testsuite tests="2" failures="0" '
                              'errors="0" skipped="0">'
                              '<testcase name="a"/><testcase name="b"/>'
                              '</testsuite></testsuites>')

    def test_even_perfect_offline_ci_does_not_authorize_business_uat_or_production(self):
        rows = {key: {"outcome": "success"} for key in REQUIRED_SUCCESSFUL_STEPS}
        report = collect(self.guard, self.trx, self.junit, self.matrix, rows)
        self.assertEqual("PASS", report["result"])
        self.assertFalse(report["release_go"])
        self.assertEqual("HOLD", report["release_authorization"]["business_uat"])
        self.assertEqual("HARD_HOLD", report["release_authorization"]["production"])
        self.assertEqual("NOT_TESTED",
                         report["release_authorization"]["sql_server_runtime_and_concurrency"])
        self.assertEqual("NOT_AUTHORIZED",
                         report["release_authorization"]["schema_1800_011"])
        self.assertEqual(RELEASE_HOLD_CONTRACT, report["release_authorization"])

    def test_all_required_successful_actions_steps_are_verified(self):
        step_data = {key: {"outcome": "success"}
                     for key in REQUIRED_SUCCESSFUL_STEPS}
        validated = validate_step_outcomes(step_data)
        self.assertEqual(len(REQUIRED_SUCCESSFUL_STEPS), validated["succeeded"])
        result = collect(self.guard, self.trx, self.junit, self.matrix,
                         step_outcomes=step_data)
        self.assertEqual("PASS", result["result"])

    def test_new_012_guard_cannot_be_omitted_from_merged_evidence(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"] = [row for row in payload["checks"]
                             if row["name"] != "no_executable_012_migration"]
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_IT_handoff_step_skipped_fails_even_with_green_other_steps(self):
        steps = {key: {"outcome": "success"} for key in REQUIRED_SUCCESSFUL_STEPS}
        steps["it_handoff_ledger"]["outcome"] = "skipped"
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix,
            step_outcomes=steps)["result"])

    def test_backend_build_failure_cannot_be_masked_by_passing_tests(self):
        step_data = {key: {"outcome": "success"}
                     for key in REQUIRED_SUCCESSFUL_STEPS}
        step_data["backend_build"]["outcome"] = "failure"
        result = collect(self.guard, self.trx, self.junit, self.matrix,
                         step_outcomes=step_data)
        self.assertEqual("FAIL_CLOSED", result["result"])
        self.assertIn("backend_build", " ".join(result["errors"]))

    def test_skipped_baseline_gate_cannot_be_masked_by_passing_tests(self):
        step_data = {key: {"outcome": "success"}
                     for key in REQUIRED_SUCCESSFUL_STEPS}
        step_data["baseline_gate"]["outcome"] = "skipped"
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix,
            step_outcomes=step_data)["result"])

    def test_missing_or_malformed_github_step_evidence_is_denied(self):
        step_data = {key: {"outcome": "success"}
                     for key in REQUIRED_SUCCESSFUL_STEPS}
        del step_data["frontend_build"]
        with self.assertRaisesRegex(ValueError, "missing"):
            validate_step_outcomes(step_data)
        step_data["frontend_build"] = "success"
        with self.assertRaisesRegex(ValueError, "unverifiable"):
            validate_step_outcomes(step_data)

    def test_missing_B4_matrix_fails_closed(self):
        self.matrix.unlink()
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_B4_matrix_sha_mismatch_fails_closed(self):
        data = json.loads(self.matrix.read_text())
        data["candidate_sha"] = "f" * 40
        self.matrix.write_text(json.dumps(data))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_B4_matrix_fabricated_execution_status_fails_closed(self):
        data = json.loads(self.matrix.read_text())
        data["status"] = "SQL_SERVER_SUCCESS"
        self.matrix.write_text(json.dumps(data))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_valid_offline_evidence_passes(self):
        self.assertEqual("PASS", collect(self.guard, self.trx, self.junit, self.matrix)["result"])
        self.assertEqual(2, parse_trx(self.trx)["passed"])
        self.assertEqual(2, parse_junit(self.junit)["passed"])

    def test_missing_named_security_gate_is_not_a_pass(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"] = [c for c in payload["checks"]
                             if c["name"] != "B3_approve_executor_DENY_ALL"]
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_checked_in_appsettings_override_gate_is_mandatory(self):
        self.assertIn("checked_in_appsettings_overrides_off",
                      REQUIRED_SECURITY_GATES)
        payload=json.loads(self.guard.read_text())
        payload["checks"]=[c for c in payload["checks"]
                           if c["name"]!="checked_in_appsettings_overrides_off"]
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED",collect(
            self.guard,self.trx,self.junit,self.matrix)["result"])

    def test_duplicate_named_security_gate_is_not_a_pass(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"].append(payload["checks"][0])
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_false_protected_sha_is_not_a_pass(self):
        payload = json.loads(self.guard.read_text())
        payload["frozen_sha"] = "b" * 40
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_non_boolean_success_flag_fails_closed(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"][0]["passed"] = "true"
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_extra_fake_check_fails_closed(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"].append({"name": "fake_ok", "passed": True})
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_sha_mismatch_fails_closed(self):
        payload = json.loads(self.guard.read_text())
        payload["head_sha"] = "an-intentionally-incorrect-sha"
        self.guard.write_text(json.dumps(payload))
        # Explicitly simulates the real GitHub Actions SHA guard.
        from unittest.mock import patch
        with patch.dict(os.environ, {"GITHUB_SHA": "the-real-head-sha"}):
            self.assertEqual("FAIL_CLOSED", collect(
                self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_guard_failure_fails_closed(self):
        data = json.loads(self.guard.read_text())
        data["checks"][0]["passed"] = False
        self.guard.write_text(json.dumps(data))
        result = collect(self.guard, self.trx, self.junit, self.matrix)
        self.assertEqual("FAIL_CLOSED", result["result"])

    def test_backend_missing_fails_closed(self):
        self.trx.unlink()
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_frontend_failure_fails_closed(self):
        self.junit.write_text('<testsuite tests="2" failures="1" '
                              'errors="0" skipped="0"/>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_skipped_test_fails_closed(self):
        self.trx.write_text(
            '<TestRun><ResultSummary><Counters total="2" passed="1" '
            'failed="0" error="0" notExecuted="1"/></ResultSummary></TestRun>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_junit_forged_count_cannot_mark_one_case_as_many_passes(self):
        xml = self.junit.read_text().replace('tests="2"', 'tests="500"')
        self.junit.write_text(xml)
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_junit_embedded_failure_cannot_be_hidden_by_suite_zero_failures(self):
        self.junit.write_text('<testsuite tests="1" failures="0" errors="0" '
                              'skipped="0"><testcase name="bad">'
                              '<failure message="hidden"/></testcase></testsuite>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_trx_missing_not_executed_counter_is_invalid(self):
        self.trx.write_text(self.trx.read_text().replace('notExecuted="0"', ''))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_nested_junit_counts_leaf_suites_once(self):
        self.junit.write_text(
            '<testsuites tests="3"><testsuite name="parent" tests="3">'
            '<testsuite name="a" tests="1" failures="0"><testcase name="a"/></testsuite>'
            '<testsuite name="b" tests="2" failures="0">'
            '<testcase name="b"/><testcase name="c"/></testsuite>'
            '</testsuite></testsuites>')
        self.assertEqual(3, parse_junit(self.junit)["total"])



class B3HttpOffEvidenceCases(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.trx=Path(self.folder.name)/"b3-http.trx"
        self.names=sorted(HTTP_REQUIRED)
        self.make(self.names)

    def make(self,names,failed=False,missing_ids=False):
        rows=[]
        for i,name in enumerate(names):
            outcome="Failed" if i==0 and failed else "Passed"
            ids="" if i==0 and missing_ids else f' testId="id-{i}" executionId="run-{i}"'
            safe_name=name.replace("&","&amp;").replace('"',"&quot;")
            rows.append(f'<UnitTestResult testName="{HTTP_PREFIX}{safe_name}" outcome="{outcome}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_all_28_named_cases_preserve_uat_hold(self):
        x=validate_http_off_trx(self.trx)
        self.assertEqual(28,x["passed"])
        self.assertEqual("HOLD",x["business_uat"])

    def test_missing_case_denied(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_http_off_trx(self.trx)

    def test_duplicate_case_denied(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"duplicated"):
            validate_http_off_trx(self.trx)

    def test_failed_result_denied(self):
        self.make(self.names,failed=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_http_off_trx(self.trx)

    def test_missing_test_id_denied(self):
        self.make(self.names,missing_ids=True)
        with self.assertRaisesRegex(ValueError,"ID"):
            validate_http_off_trx(self.trx)



class B4HrHttpEvidenceCases(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.trx=Path(self.folder.name)/"hr-login.trx"
        self.names=sorted(HR_HTTP_REQUIRED)
        self.make()

    def make(self,names=None,failed=False,missing_id=False):
        names=self.names if names is None else names
        rows=[]
        for i,name in enumerate(names):
            outcome="Failed" if failed and i==0 else "Passed"
            ids="" if missing_id and i==0 else f' testId="case-{i}" executionId="exec-{i}"'
            rows.append(f'<UnitTestResult testName="{HR_HTTP_PREFIX}{name}" outcome="{outcome}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_all_11_actual_login_HTTP_cases_preserve_uat_hold(self):
        result=validate_hr_login_http_trx(self.trx)
        self.assertEqual(11,result["passed"])
        self.assertEqual("NOT_TESTED",result["full_api_off_08_deployed_session_revocation"])
        self.assertEqual("HOLD",result["business_uat"])

    def test_missing_login_case_denied(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_hr_login_http_trx(self.trx)

    def test_forged_duplicate_case_denied(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"duplicated"):
            validate_hr_login_http_trx(self.trx)

    def test_forged_pass_counter_with_failed_login_case_denied(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_hr_login_http_trx(self.trx)

    def test_missing_individual_login_case_id_denied(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"ID"):
            validate_hr_login_http_trx(self.trx)


class B4LiveSessionGuardEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.trx=Path(self.folder.name)/"session.trx"
        self.names=sorted(SESSION_HTTP_REQUIRED)
        self.make()

    def make(self,names=None,fail=False,missing_id=False):
        names=self.names if names is None else names
        rows=[]
        for i,name in enumerate(names):
            status="Failed" if fail and i==0 else "Passed"
            ids="" if missing_id and i==0 else f' testId="test-{i}" executionId="exec-{i}"'
            safe=name.replace("&","&amp;").replace('"',"&quot;")
            rows.append(f'<UnitTestResult testName="{SESSION_HTTP_PREFIX}{safe}" outcome="{status}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_all_12_named_optin_and_default_off_scenarios_keep_release_hold(self):
        result=validate_session_guard_http_trx(self.trx)
        self.assertEqual(12,result["passed"])
        self.assertEqual("OFF_REQUIRED",result["live_session_guard_deployed"])
        self.assertEqual("NOT_TESTED",result["uat_hr_session_revocation"])
        self.assertEqual("HARD_HOLD",result["production"])

    def test_missing_named_case_cannot_hide_under_backend_green(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_session_guard_http_trx(self.trx)

    def test_duplicate_named_case_cannot_replace_required_case(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_session_guard_http_trx(self.trx)

    def test_failed_case_cannot_fake_pass(self):
        self.make(fail=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_session_guard_http_trx(self.trx)

    def test_case_without_independent_test_and_execution_ids_fails(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"ID"):
            validate_session_guard_http_trx(self.trx)


class B4CombinedHttpTrxSeparationTests(unittest.TestCase):
    """Both suites share one C# class; one cannot substitute for the other."""
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.trx=Path(self.folder.name)/"combined.trx"

    def make(self,drop=None):
        names=[HR_HTTP_PREFIX+x for x in HR_HTTP_REQUIRED]
        names += [SESSION_HTTP_PREFIX+x for x in SESSION_HTTP_REQUIRED]
        names=sorted(n for n in names if n!=drop)
        result=[]
        for i,name in enumerate(names):
            safe=name.replace("&","&amp;").replace('"',"&quot;")
            result.append(f'<UnitTestResult testName="{safe}" outcome="Passed" testId="t-{i}" executionId="e-{i}"/>')
        self.trx.write_text("<TestRun><Results>"+"".join(result)+"</Results></TestRun>")

    def test_mixed_23_results_validate_both_exact_named_suites(self):
        self.make()
        self.assertEqual(11,validate_hr_login_http_trx(self.trx)["passed"])
        self.assertEqual(12,validate_session_guard_http_trx(self.trx)["passed"])

    def test_missing_one_HR_case_cannot_be_substituted_by_session_case(self):
        self.make(drop=HR_HTTP_PREFIX+sorted(HR_HTTP_REQUIRED)[0])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_hr_login_http_trx(self.trx)
        self.assertEqual(12,validate_session_guard_http_trx(self.trx)["passed"])

    def test_missing_one_session_case_cannot_be_substituted_by_HR_case(self):
        self.make(drop=SESSION_HTTP_PREFIX+sorted(SESSION_HTTP_REQUIRED)[0])
        self.assertEqual(11,validate_hr_login_http_trx(self.trx)["passed"])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_session_guard_http_trx(self.trx)



class B4RoleHttpNamedEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.trx=Path(self.temp.name)/"role.trx"
        self.names=sorted(ROLE_HTTP_REQUIRED)
        self.make()

    def make(self,names=None,failed=False,missing_id=False):
        names=self.names if names is None else names
        rows=[]
        for i,name in enumerate(names):
            status="Failed" if failed and i==0 else "Passed"
            ids="" if missing_id and i==0 else f' testId="t-{i}" executionId="e-{i}"'
            rows.append(f'<UnitTestResult testName="{ROLE_HTTP_PREFIX}{name}" outcome="{status}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_all_nine_exact_roles_still_hold_release(self):
        result=validate_role_http_trx(self.trx)
        self.assertEqual(9,result["passed"])
        self.assertEqual("OFF_REQUIRED",result["live_guard_deployed"])
        self.assertEqual("PENDING",result["formal_b4"])
        self.assertEqual("HARD_HOLD",result["production"])

    def test_missing_role_case_fails_closed(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_role_http_trx(self.trx)

    def test_duplicate_role_case_fails_closed(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_role_http_trx(self.trx)

    def test_failed_role_case_fails_closed(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_role_http_trx(self.trx)

    def test_missing_individual_role_test_id_fails_closed(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"ID"):
            validate_role_http_trx(self.trx)

    def test_role_suite_cannot_replace_login_or_hr_session_suite(self):
        self.assertEqual(9,validate_role_http_trx(self.trx)["passed"])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_hr_login_http_trx(self.trx)
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_session_guard_http_trx(self.trx)



class B4TeamSupervisorHttpEvidenceTests(unittest.TestCase):
    """A passing aggregate cannot replace either live-scope named suite."""
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.trx=Path(self.temp.name)/"team-scope.trx"
        self.team=sorted(TEAM_HTTP_REQUIRED)
        self.scope=sorted(SUPERVISOR_SCOPE_HTTP_REQUIRED)
        self.make()

    def make(self,teams=None,scopes=None,failed=False,missing_id=False):
        ts=self.team if teams is None else teams
        ss=self.scope if scopes is None else scopes
        rows=[]
        for i,name in enumerate(ts+ss):
            ids="" if i==0 and missing_id else f' testId="T-{i}" executionId="E-{i}"'
            state="Failed" if i==0 and failed else "Passed"
            rows.append(f'<UnitTestResult testName="{TEAM_SCOPE_HTTP_PREFIX}{name}" outcome="{state}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_all_16_isolated_cases_still_mean_zero_live_B4(self):
        r=validate_team_scope_http_trx(self.trx)
        self.assertEqual(16,r["passed"])
        self.assertEqual(9,r["team_passed"])
        self.assertEqual(7,r["supervisor_scope_passed"])
        self.assertEqual("OFF_REQUIRED",r["live_session_guard_deployed"])
        self.assertEqual("NOT_PRESENT",r["deployment_site_jwt_claim"])
        self.assertEqual("NOT_EXECUTED",r["formal_api_b4"])
        self.assertEqual("HARD_HOLD",r["production"])

    def test_missing_team_not_replaced_by_scope_pass(self):
        self.make(teams=self.team[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_team_scope_http_trx(self.trx)

    def test_missing_scope_not_replaced_by_team_pass(self):
        self.make(scopes=self.scope[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_team_scope_http_trx(self.trx)

    def test_forged_duplicate_named_case_fails(self):
        self.make(teams=self.team[:-1]+self.team[:1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_team_scope_http_trx(self.trx)

    def test_failed_live_scope_http_fails_closed(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_team_scope_http_trx(self.trx)

    def test_missing_independent_trx_identifiers_fails_closed(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"ID"):
            validate_team_scope_http_trx(self.trx)

    def test_team_scope_results_do_not_substitute_for_role_HR_or_session(self):
        self.assertEqual(16,validate_team_scope_http_trx(self.trx)["passed"])
        for validator in (validate_hr_login_http_trx,
                          validate_role_http_trx,
                          validate_session_guard_http_trx):
            with self.subTest(validator=validator.__name__):
                with self.assertRaisesRegex(ValueError,"mandatory"):
                    validator(self.trx)



class DatedSiteRuntimeEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.trx=Path(self.temp.name)/"dated-site.trx"
        self.names=sorted(SITE_RUNTIME_REQUIRED)
        self.make()

    def make(self,names=None,failed=False,missing_id=False):
        names=self.names if names is None else names
        rows=[]
        for i,name in enumerate(names):
            outcome="Failed" if failed and i==0 else "Passed"
            ids="" if missing_id and i==0 else f' testId="s-{i}" executionId="e-{i}"'
            rows.append(f'<UnitTestResult testName="{SITE_RUNTIME_PREFIX}{name}" outcome="{outcome}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_twenty_exact_dated_site_cases_keep_B4_and_UAT_hold(self):
        result=validate_dated_site_runtime_trx(self.trx)
        self.assertEqual(20,result["passed"])
        self.assertEqual(0,result["formal_B4_acceptance"])
        self.assertEqual("NOT_TESTED",result["trip_context_http_endpoint"])
        self.assertEqual("NOT_TESTED",result["deployed_sql_server"])
        self.assertEqual("HARD_HOLD",result["production"])

    def test_missing_site_test_denies_evidence(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_dated_site_runtime_trx(self.trx)

    def test_duplicate_site_name_denies_evidence(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_dated_site_runtime_trx(self.trx)

    def test_failed_site_test_denies_evidence(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_dated_site_runtime_trx(self.trx)

    def test_site_evidence_requires_unique_result_ids(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"IDs"):
            validate_dated_site_runtime_trx(self.trx)

    def test_site_suite_cannot_replace_live_session_coverage(self):
        self.assertEqual(20,validate_dated_site_runtime_trx(self.trx)["passed"])
        for fn in (validate_role_http_trx,validate_hr_login_http_trx,
                   validate_session_guard_http_trx,validate_team_scope_http_trx):
            with self.subTest(kind=fn.__name__):
                with self.assertRaisesRegex(ValueError,"mandatory"):
                    fn(self.trx)



class RealDatedSiteHttpEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.trx=Path(self.tmp.name)/"site-http.trx"
        self.names=sorted(SITE_HTTP_REQUIRED)
        self.make()

    def make(self,names=None,failed=False,missing_id=False):
        names=self.names if names is None else names
        rows=[]
        for i,name in enumerate(names):
            ids="" if missing_id and i==0 else f' testId="t-{i}" executionId="e-{i}"'
            outcome="Failed" if failed and i==0 else "Passed"
            rows.append(f'<UnitTestResult testName="{SITE_HTTP_PREFIX}{name}" outcome="{outcome}"{ids}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(rows)+"</Results></TestRun>")

    def test_all_six_HTTP_cases_are_explicitly_not_UAT_acceptance(self):
        x=validate_site_http_trx(self.trx)
        self.assertEqual(6,x["passed"])
        self.assertEqual(0,x["formal_b4_accepted"])
        self.assertEqual("NOT_TESTED",x["real_azure_sql_http"])
        self.assertEqual("HARD_HOLD",x["production"])

    def test_missing_HTTP_case_cannot_hide_in_reader_suite(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_site_http_trx(self.trx)

    def test_duplicate_HTTP_name_does_not_replace_required(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"mandatory"):
            validate_site_http_trx(self.trx)

    def test_failed_HTTP_case_rejected(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"failed"):
            validate_site_http_trx(self.trx)

    def test_missing_HTTP_test_execution_id_rejected(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"IDs"):
            validate_site_http_trx(self.trx)

    def test_HTTP_case_cannot_replace_reader_or_HR_authority(self):
        self.assertEqual(6,validate_site_http_trx(self.trx)["passed"])
        for validator in (validate_dated_site_runtime_trx,
                          validate_hr_login_http_trx,
                          validate_session_guard_http_trx,
                          validate_role_http_trx):
            with self.subTest(validator=validator.__name__):
                with self.assertRaisesRegex(ValueError,"mandatory"):
                    validator(self.trx)


if __name__ == "__main__":
    unittest.main()
