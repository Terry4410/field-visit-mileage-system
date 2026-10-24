"""Synthetic/offline-only release bridge tests. No Azure/SQL/SCM/CI use."""
import hashlib
import io
import json
import sys
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release_bridge import ACCEPTED, Pins, GateClosed, inspect_bytes


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def pack(data):
    dest = io.BytesIO()
    with zipfile.ZipFile(dest, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for name in sorted(data):
            z.writestr(name, data[name])
    return dest.getvalue()


def dump(data):
    return json.dumps(data, sort_keys=True).encode()


def fixture(*, status="PARTIAL", activation="OFF", live="NOT_AUTHORIZED_NOT_EXECUTED",
            case_count=25, seq_count=14, db="db-fieldvisit-uat", duplicate=False, traverse=False):
    candidate = ACCEPTED.candidate_sha
    nested_api_bytes = pack({"FieldVisit.Api.dll": b"api_test_binary", "release-contract.json": b"rc"})
    nested_frontend_bytes = pack({"index.html": b"index_fixture", "config.js": b"OFF"})
    api_sha, frontend_sha = sha(nested_api_bytes), sha(nested_frontend_bytes)
    schema_bytes = b'{"objectManifest":"synthetic"}'
    schema_sha = sha(schema_bytes)
    assembly_sha = "d" * 64
    contract = {"candidateSourceSha": candidate, "apiSha256": assembly_sha,
                "frontendSha256": frontend_sha, "schemaSha256": schema_sha}
    manifest = {"candidateSourceSha": candidate, "runId": ACCEPTED.run_id,
                "activationDefault": activation, "physicalIphone": "NOT_EXECUTED",
                "liveUat": live, "binding": contract,
                "packages": [{"file":"api-release-candidate.zip","sha256":api_sha},
                             {"file":"frontend-release-candidate.zip","sha256":frontend_sha}]}
    cases = [dict(case=k, technicalCoverage=status, liveBusinessUat="NOT_EXECUTED")
        for k in ([f"VIS-{x:02}" for x in range(1,7)] +
                  [f"LEAD-{x:02}" for x in range(1,7)] +
                  [f"ADMIN-{x:02}" for x in range(1,5)] +
                  [f"XLS-{x:02}" for x in range(1,6)] +
                  [f"TRIP-{x:02}" for x in range(1,3)] +
                  [f"SEC-{x:02}" for x in range(1,3)])]
    cases = cases[:case_count]
    entries = {}
    for i in range(1, 15):
        entries[f"sql/file_{i}.sql"] = f"SELECT {i};".encode()
        entries[f"uat-sql/file_{i}.sql"] = f"SELECT {i}; -- synthetic uat".encode()
    sequence_fixture = [dict(step=i, file=f"sql/file_{i}.sql", sha256=sha(entries[f"sql/file_{i}.sql"])) for i in range(1,seq_count + 1)]
    sequence_uat = [dict(step=i, file=f"uat-sql/file_{i}.sql", sha256=sha(entries[f"uat-sql/file_{i}.sql"])) for i in range(1,seq_count + 1)]
    installer = {"candidateSourceSha": candidate,
                 "executionDefault":"DRY_RUN_ONLY_REQUIRES_SEPARATE_EXACT_MANIFEST_APPROVAL",
                 "fixture":{"sequence":sequence_fixture},
                 "uat":{"server":"sql-fieldvisit-jpe-uat.database.windows.net",
                        "database":db,"sequence":sequence_uat}}
    for package, raw in [("api", nested_api_bytes), ("frontend", nested_frontend_bytes)]:
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            entries[f"{package}-manifest.json"] = dump({"file":f"{package}-release-candidate.zip",
                "sha256":sha(raw),"files":[{"path":name,"sha256":sha(z.read(name))} for name in z.namelist()]})
        entries[f"{package}-release-candidate.zip"] = raw
    entries.update({
        "schema-contract.json":schema_bytes,
        "release-contract.json":dump(contract),
        "release-manifest.json":dump(manifest),
        "frozen-25-coverage.json":dump({"caseCount":25, "cases":cases,"notAllCasesClaimedPass":True}),
        "standalone-install-manifest.json":dump(installer),
        "source-code-immutable-rc.zip":b"source-synthetic",
        "release-source-delta.zip":b"delta-synthetic",
        "install.py":b"dry-run-only",
        "IT-ACCEPTANCE.md":b"HOLD",
        "sign_activation.py":b"never activate",
    })
    if traverse:
        entries["../bad-secret.txt"] = b"blocked"
    entries["checksums.sha256"] = "".join(
        f"{sha(raw)}  {name}\n" for name,raw in sorted(entries.items())).encode()
    original_zip = pack(entries)
    if duplicate:
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as z:
            for name, raw in entries.items():
                z.writestr(name, raw)
            z.writestr("install.py", entries["install.py"])
        original_zip = buf.getvalue()
    pins = Pins(candidate, sha(original_zip), ACCEPTED.run_id, api_sha, frontend_sha, assembly_sha, schema_sha)
    return original_zip, pins


class ReleaseBridgeTests(unittest.TestCase):
    def test_valid_synthetic_package_reports_hold_live(self):
        data, pins = fixture()
        result = inspect_bytes(data, pins)
        self.assertEqual(result["offlinePackage"], "PASS")
        self.assertEqual(result["status"], "HOLD_LIVE_G1")
        self.assertEqual(result["azure"], "NOT_ACCESSED")
        self.assertEqual(result["deployment"], "NOT_PERFORMED")
        self.assertEqual(result["frozen25"], "PARTIAL_25_LIVE_NOT_EXECUTED")

    def test_actual_candidate_sha_is_immutable(self):
        self.assertEqual(ACCEPTED.candidate_sha,"9ee5dacceed2bf55c39310d663f3258bcb545989")

    def test_corrupted_outer_zip_denied(self):
        data, pins = fixture()
        with self.assertRaises(GateClosed):
            inspect_bytes(data + b"not-the-accepted-zip", pins)

    def test_changed_candidate_sha_denied_even_if_zip_hash_valid(self):
        data, pins = fixture()
        forged = Pins("a"*40,pins.artifact_sha256,pins.run_id,pins.api_zip_sha256,
                      pins.frontend_zip_sha256,pins.api_composite_sha256,pins.schema_sha256)
        with self.assertRaises(GateClosed):
            inspect_bytes(data, forged)

    def test_live_marked_executed_denied(self):
        data, pins = fixture(live="PASS")
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_unapproved_feature_activation_denied(self):
        data, pins = fixture(activation="ON")
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_full_business_case_pass_forgery_denied(self):
        data, pins = fixture(status="PASS")
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_case_omission_denied(self):
        data, pins = fixture(case_count=24)
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_incomplete_sql_layer_plan_denied(self):
        data, pins = fixture(seq_count=13)
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_wrong_uat_target_denied(self):
        data, pins = fixture(db="db-production")
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_duplicate_zip_entry_denied(self):
        data, pins = fixture(duplicate=True)
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)

    def test_path_traversal_denied(self):
        data, pins = fixture(traverse=True)
        with self.assertRaises(GateClosed):
            inspect_bytes(data, pins)


if __name__ == "__main__":
    unittest.main()
