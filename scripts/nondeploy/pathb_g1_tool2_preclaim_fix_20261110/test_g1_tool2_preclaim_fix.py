import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest import mock
import urllib.error

import g1_preclaim_tools as tools
import g1_tool2_preclaim_handoff as handoff


BASE = Path(__file__).resolve().parent
CANDIDATE = BASE / "FieldVisit_PathB_G1_Consolidated_SQLCMD_ArgFix_SafetyFix_NONDEPLOY_20261110.zip"


class Headers(dict):
    def get(self, key, default=None):
        return super().get(key, default)


class Response:
    def __init__(self, data, *, status=200, headers=None, url="https://example.invalid/x"):
        self.data = data
        self.status = status
        self.headers = Headers(headers or {"Content-Length": str(len(data))})
        self.url = url
    def getcode(self): return self.status
    def geturl(self): return self.url
    def read(self, limit=-1): return self.data[:limit]
    def close(self): pass


class Opener:
    def __init__(self, actions):
        self.actions = list(actions)
        self.calls = []
    def open(self, request, timeout=None):
        self.calls.append(request.full_url)
        action = self.actions.pop(0)
        if isinstance(action, BaseException):
            raise action
        return action


def item(name, url, data):
    return {"name": name, "url": url,
            "sha256": hashlib.sha256(data).hexdigest()}


def tool_items(payloads):
    urls = [
        "https://packages.microsoft.com/debian/12/prod/pool/main/m/msodbcsql18/msodbcsql18_18.6.1.1-1_amd64.deb",
        "https://packages.microsoft.com" + tools.TOOL2_PATH,
        "https://deb.debian.org/debian/pool/main/libt/libtool/libltdl7_2.4.7-7~deb12u1_amd64.deb",
        "https://deb.debian.org/debian/pool/main/u/unixodbc/libodbc2_2.3.11-2+deb12u1_amd64.deb",
        "https://deb.debian.org/debian/pool/main/u/unixodbc/libodbcinst2_2.3.11-2+deb12u1_amd64.deb",
    ]
    names = ["msodbcsql18", "mssql-tools18", "libltdl7", "libodbc2", "libodbcinst2"]
    return [item(n, u, d) for n, u, d in zip(names, urls, payloads)]


def redirect_error(location):
    return urllib.error.HTTPError(
        "https://packages.microsoft.com" + tools.TOOL2_PATH, 302, "Found",
        Headers({"Location": location}), None)


class ToolAcquisitionTests(unittest.TestCase):
    def test_five_tools_success_with_exact_tool2_redirect(self):
        payloads = [f"tool-{i}".encode() for i in range(1, 6)]
        entries = tool_items(payloads)
        location = "https://pmc-geofence.trafficmanager.net" + tools.TOOL2_PATH + "?geofence=true"
        actions = [Response(payloads[0]), redirect_error(location), Response(payloads[1]),
                   Response(payloads[2]), Response(payloads[3]), Response(payloads[4])]
        opener = Opener(actions)
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw) / "cache"
            root.mkdir(mode=0o700)
            manifest = tools.acquire_all(entries, root, opener=opener)
            self.assertEqual(set(manifest), {x["name"] for x in entries})
            self.assertEqual(len(opener.calls), 6)
            for entry in entries:
                path = root / (entry["name"] + ".deb")
                self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o600)
                self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), entry["sha256"])

    def test_tool2_http_failure_is_fixed_safe_code(self):
        data = b"tool-2"
        entry = tool_items([b"1", data, b"3", b"4", b"5"])[1]
        with tempfile.TemporaryDirectory() as raw:
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_2_HTTP_HOLD$"):
                tools.fetch_one(2, entry, Path(raw) / "x", opener=Opener([
                    urllib.error.HTTPError(entry["url"], 503, "x", Headers(), None)]))

    def test_unapproved_redirect_rejected_without_second_request(self):
        entry = tool_items([b"1", b"2", b"3", b"4", b"5"])[1]
        opener = Opener([redirect_error("https://evil.invalid/payload")])
        with tempfile.TemporaryDirectory() as raw:
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_2_REDIRECT_POLICY_HOLD$"):
                tools.fetch_one(2, entry, Path(raw) / "x", opener=opener)
        self.assertEqual(len(opener.calls), 1)

    def test_second_redirect_rejected(self):
        entry = tool_items([b"1", b"2", b"3", b"4", b"5"])[1]
        good = "https://pmc-geofence.trafficmanager.net" + tools.TOOL2_PATH + "?geofence=true"
        opener = Opener([redirect_error(good), redirect_error(good)])
        with tempfile.TemporaryDirectory() as raw:
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_2_HTTP_HOLD$"):
                tools.fetch_one(2, entry, Path(raw) / "x", opener=opener)
        self.assertEqual(len(opener.calls), 2)

    def test_sha_mismatch(self):
        entry = tool_items([b"1", b"2", b"3", b"4", b"5"])[0]
        with tempfile.TemporaryDirectory() as raw:
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_1_SHA256_HOLD$"):
                tools.fetch_one(1, entry, Path(raw) / "x", opener=Opener([Response(b"wrong")]))

    def test_size_mismatch(self):
        entry = tool_items([b"1", b"2", b"3", b"4", b"5"])[0]
        response = Response(b"1", headers={"Content-Length": "2"})
        with tempfile.TemporaryDirectory() as raw:
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_1_SIZE_HOLD$"):
                tools.fetch_one(1, entry, Path(raw) / "x", opener=Opener([response]))

    def test_symlink_destination_rejected(self):
        entry = tool_items([b"1", b"2", b"3", b"4", b"5"])[0]
        with tempfile.TemporaryDirectory() as raw:
            dest = Path(raw) / "x"
            dest.symlink_to(Path(raw) / "target")
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_1_DESTINATION_HOLD$"):
                tools.fetch_one(1, entry, dest, opener=Opener([Response(b"1")]))

    def test_unsafe_cache_file_rejected(self):
        with tempfile.TemporaryDirectory() as raw:
            path = Path(raw) / "x"
            path.write_bytes(b"x")
            path.chmod(0o644)
            with self.assertRaisesRegex(tools.ToolHold, "^TOOL_3_CACHE_HOLD$"):
                tools.verify_private_tool(3, path, hashlib.sha256(b"x").hexdigest())

    def test_parent_copy_is_network_free_and_exact(self):
        payloads = [f"tool-{i}".encode() for i in range(1, 6)]
        entries = tool_items(payloads)
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            cache = root / "cache"; cache.mkdir(mode=0o700)
            dest = root / "dest"; dest.mkdir(mode=0o700)
            for entry, payload in zip(entries, payloads):
                path = cache / (entry["name"] + ".deb")
                path.write_bytes(payload); path.chmod(0o600)
            for entry, payload in zip(entries, payloads):
                out = dest / (entry["name"] + ".deb")
                tools.copy_preverified(entries, cache, entry["url"], out, entry["sha256"])
                self.assertEqual(out.read_bytes(), payload)


class HandoffOrderTests(unittest.TestCase):
    def _run_with_mocks(self, acquire_side_effect=None, parent_rc=0):
        payloads = [f"tool-{i}".encode() for i in range(1, 6)]
        order = []
        def fake_stage(root, archive):
            (root / "p3_kudu_cloudshell_parent.py").write_bytes(b"parent")
            (root / "binding-index.json").write_text(json.dumps({"files":{"p3_kudu_cloudshell_parent.py":hashlib.sha256(b"parent").hexdigest()}}))
            (root / "executor_config.json").write_text(json.dumps({"tools": tool_items(payloads)}))
            return root / "p3_kudu_cloudshell_parent.py"
        def fake_acquire(*a, **k):
            order.append("tools")
            if acquire_side_effect: raise acquire_side_effect
            return {}
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            claim_dir = root / "claims"; claim_dir.mkdir(mode=0o700)
            candidate = root / "candidate.zip"; candidate.write_bytes(b"candidate")
            auth = root / "auth.json"; auth.write_text("{}")
            args = mock.Mock(candidate=str(candidate), live=True,
                             authorization=str(auth), claim_dir=str(claim_dir),
                             owner_confirm="EXACT_G1_ONE_SHOT")
            claim = mock.Mock(side_effect=lambda *a: order.append("claim"))
            parent = mock.Mock(side_effect=lambda *a, **k: order.append("parent") or parent_rc)
            with mock.patch.object(handoff, "inspect_candidate", return_value=30), \
                 mock.patch.object(handoff, "validate_auth_file", return_value="a"*32), \
                 mock.patch.object(handoff, "secure_regular"), \
                 mock.patch.object(handoff, "stage", side_effect=fake_stage), \
                 mock.patch.object(handoff, "preclaim_parent_gates", side_effect=lambda *a: order.append("gates")), \
                 mock.patch.object(handoff, "acquire_all", side_effect=fake_acquire), \
                 mock.patch.object(handoff, "consume_claim", claim), \
                 mock.patch.object(handoff.subprocess, "call", parent):
                try:
                    rc = handoff.run(args)
                except Exception as exc:
                    rc = exc
            return order, rc, claim.call_count, parent.call_count

    def test_source_order_tools_before_claim_before_parent(self):
        source = (BASE / "g1_tool2_preclaim_handoff.py").read_text()
        self.assertLess(source.index("acquire_all(cfg['tools']"),
                        source.index("consume_claim(claim_directory"))
        self.assertLess(source.index("consume_claim(claim_directory"),
                        source.index("subprocess.call(["))

    def test_preclaim_tool_failure_does_not_claim_or_launch_parent(self):
        order, rc, claims, parents = self._run_with_mocks(
            acquire_side_effect=tools.ToolHold("TOOL_2_HTTP_HOLD"))
        self.assertIsInstance(rc, tools.ToolHold)
        self.assertEqual(order, ["gates", "tools"])
        self.assertEqual((claims, parents), (0, 0))

    def test_parent_failure_is_called_once_and_never_retried(self):
        order, rc, claims, parents = self._run_with_mocks(parent_rc=1)
        self.assertEqual(rc, 1)
        self.assertEqual(order, ["gates", "tools", "claim", "parent"])
        self.assertEqual((claims, parents), (1, 1))

    def test_parent_timeout_or_unknown_exit_is_not_retried(self):
        order, rc, claims, parents = self._run_with_mocks(parent_rc=124)
        self.assertEqual(rc, 1)
        self.assertEqual(order, ["gates", "tools", "claim", "parent"])
        self.assertEqual((claims, parents), (1, 1))

    def test_old_operation_id_cannot_be_reused(self):
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw); root.chmod(0o700)
            handoff.consume_claim(root, "a" * 32)
            with self.assertRaisesRegex(handoff.GateHold, "ALREADY_CONSUMED"):
                handoff.consume_claim(root, "a" * 32)

    def test_parent_wrapper_replaces_only_download_once(self):
        source = (BASE / "g1_preverified_parent.py").read_text()
        self.assertIn("parent.download_once = lambda", source)
        self.assertNotIn("urllib", source)
        self.assertIn("return parent.execute(namespace)", source)

    def test_no_retry_constructs_in_new_sources(self):
        for name in ["g1_preclaim_tools.py", "g1_preverified_parent.py",
                     "g1_tool2_preclaim_handoff.py"]:
            source = (BASE / name).read_text()
            self.assertNotIn("for attempt in", source)
            self.assertNotIn("while True", source)

    def test_original_candidate_and_binding_unchanged(self):
        self.assertEqual(hashlib.sha256(CANDIDATE.read_bytes()).hexdigest(),
                         "8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141")
        import zipfile
        with zipfile.ZipFile(CANDIDATE) as archive:
            self.assertEqual(hashlib.sha256(archive.read("binding-index.json")).hexdigest(),
                             "302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45")
            binding = json.loads(archive.read("binding-index.json"))
            for name in ["frozen_install.py.snapshot", "p3_app_installer_runner.py",
                         "p3_blob_guard.py", "preflight.sql", "postcheck.sql"]:
                self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), binding["files"][name])


if __name__ == "__main__":
    unittest.main()

