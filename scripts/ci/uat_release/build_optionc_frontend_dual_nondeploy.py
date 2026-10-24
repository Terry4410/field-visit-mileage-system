#!/usr/bin/env python3
"""Two build-only UAT010 frontend candidates; neither is authorized for deployment.

OFF is exact source. ON changes ONLY six compile-time Option C constants in a
disposable temp workspace. Git sources, deployed assets, Azure and SQL untouched.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[3]
FLAGS = "frontend/src/optionc-approval-flags.ts"
EXPECTED_FLAG_BLOB = "7c9e8482ffa2bb4a92316bff6f9e4954bf9560be"
EXPECTED_LOCK_BLOB = "2d4cc41d6b4720725b2a0ce6bd1b0d35cfd09c93"
EXPECTED_CONFIG_BLOB = "0f81de4f4fa868c2ce188da9fff21d797ccf7071"
EXPECTED = frozenset({
    "OPTIONC_APPROVAL_UI_ENABLED",
    "OPTIONC_LEADER_REVIEW_UI_ENABLED",
    "OPTIONC_CREATE_LOCATION_UI_ENABLED",
    "OPTIONC_DIRECT_LOWRISK_UI_ENABLED",
    "OPTIONC_EXCEL_CREATE_UI_ENABLED",
    "OPTIONC_EXCEL_UPDATE_UI_ENABLED",
})
DECLARATION = re.compile(
    r"^(export const (OPTIONC_[A-Z_]+_UI_ENABLED): boolean = )(false|true);$",
    re.MULTILINE,
)


class SourceHold(ValueError):
    pass


def h(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def validate_flags(raw: str, enabled: bool) -> None:
    rows = list(DECLARATION.finditer(raw))
    if len(rows) != 6 or {m.group(2) for m in rows} != EXPECTED:
        raise SourceHold("unexpected-optionc-flag-set")
    if any(m.group(3) != ("true" if enabled else "false") for m in rows):
        raise SourceHold("unexpected-optionc-flag-state")


def overlay(raw: str) -> str:
    validate_flags(raw, False)
    converted, count = DECLARATION.subn(lambda m: m.group(1) + "true;", raw)
    if count != 6:
        raise SourceHold("flag-overlay-cardinality")
    validate_flags(converted, True)
    # Absolute source delta: only the approved six literals.
    if DECLARATION.sub(lambda m: m.group(1) + "false;", converted) != raw:
        raise SourceHold("unreviewed-frontend-overlay")
    return converted


def source_guard() -> tuple[str, str]:
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("GITHUB_REPOSITORY") != "Terry4410/field-visit-mileage-system":
        raise SourceHold("not-pinned-ci")
    sha = os.environ.get("GITHUB_SHA", "")
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or git("rev-parse", "HEAD") != sha:
        raise SourceHold("head-drift")
    for path, blob in (
        (FLAGS, EXPECTED_FLAG_BLOB),
        ("frontend/package-lock.json", EXPECTED_LOCK_BLOB),
        ("frontend/public/config.js", EXPECTED_CONFIG_BLOB),
    ):
        if git("hash-object", path) != blob:
            raise SourceHold("source-blob-drift")
    original = (ROOT / FLAGS).read_text(encoding="utf-8")
    converted = overlay(original)
    config = (ROOT / "frontend/public/config.js").read_text(encoding="utf-8")
    if 'AUTH_MODE: "Demo"' not in config or 'enabled:true' in config:
        raise SourceHold("demo-config-drift")
    if os.environ.get("UAT_SQL_CONNECTION") or os.environ.get("PRODUCTION_SQL_CONNECTION"):
        raise SourceHold("live-connection-env")
    return original, converted


def package_tree(dist: pathlib.Path, target: pathlib.Path, sha: str, label: str, enabled: bool) -> dict:
    if not (dist / "index.html").is_file() or not (dist / "config.js").is_file():
        raise SourceHold("build-output-missing")
    if 'AUTH_MODE: "Demo"' not in (dist / "config.js").read_text():
        raise SourceHold("auth-config-drift")
    if (dist / "source-sha.txt").read_text(encoding="ascii") != sha + "\n":
        raise SourceHold("candidate-source-stamp")
    expected_profile = {"sourceSha": sha, "mode": label, "deployAllowed": False,
                        "approval": "NOT_GRANTED", "backendAlignment": "UNKNOWN"}
    if json.loads((dist / "candidate-mode.json").read_text(encoding="utf-8")) != expected_profile:
        raise SourceHold("candidate-profile-drift")
    # Validate actual post-Vite output, not only the edited TypeScript input:
    # these three source-gated routes/actions must be absent in OFF and present in ON.
    bundles = sorted(p for p in (dist / "assets").glob("*.js") if p.is_file())
    if not bundles:
        raise SourceHold("bundled-frontend-missing")
    compiled = b"\n".join(p.read_bytes() for p in bundles)
    for marker in (b"/leader/location-review", b"publish-reviewed",
                   b"LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW"):
        if (marker in compiled) is not enabled:
            raise SourceHold("compiled-feature-gate-drift")
    files = sorted(p for p in dist.rglob("*") if p.is_file())
    if not files or not any(p.suffix == ".js" and "assets" in p.parts for p in files):
        raise SourceHold("missing-bundled-js")
    entries = []
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for file in files:
            rel = file.relative_to(dist).as_posix()
            if rel.startswith("/") or ".." in pathlib.PurePosixPath(rel).parts:
                raise SourceHold("archive-path")
            raw = file.read_bytes()
            info = zipfile.ZipInfo(rel, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            z.writestr(info, raw)
            entries.append({"path": rel, "sha256": h(raw)})
    with zipfile.ZipFile(target) as z:
        if len(z.namelist()) != len(entries):
            raise SourceHold("archive-duplicate")
        for item in entries:
            if h(z.read(item["path"])) != item["sha256"]:
                raise SourceHold("archive-integrity")
    return {"file": target.name, "sha256": h(target.read_bytes()), "files": entries}


def run() -> None:
    orig, enabled = source_guard()
    output = ROOT / "artifacts/uat010-frontend-dual"
    if output.exists():
        raise SourceHold("refuse-overwrite")
    output.mkdir(parents=True)
    with tempfile.TemporaryDirectory(prefix="fieldvisit-ui-nondeploy-") as tmp:
        frontend = pathlib.Path(tmp) / "frontend"
        shutil.copytree(ROOT / "frontend", frontend, ignore=shutil.ignore_patterns("node_modules", "dist", "*.tsbuildinfo"))
        if (frontend / "src/optionc-approval-flags.ts").read_text() != orig:
            raise SourceHold("workspace-drift")
        subprocess.run(["npm", "ci", "--no-audit", "--no-fund"], cwd=frontend, timeout=240, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        receipts = []
        for label, source, allowed in (("OFF", orig, False), ("ON_NONDEPLOY", enabled, True)):
            flags_path = frontend / "src/optionc-approval-flags.ts"
            flags_path.write_text(source, encoding="utf-8")
            validate_flags(flags_path.read_text(), allowed)
            shutil.rmtree(frontend / "dist", ignore_errors=True)
            for tsinfo in frontend.rglob("*.tsbuildinfo"):
                tsinfo.unlink()
            subprocess.run(["npm", "run", "build"], cwd=frontend, timeout=240, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True,
                           env={**os.environ, "VITE_RELEASE_SOURCE_SHA": os.environ["GITHUB_SHA"]})
            dist = frontend / "dist"
            # A future read-only runtime observer can correlate this deployed
            # content to the exact source and candidate mode (not an approval).
            (dist / "source-sha.txt").write_text(os.environ["GITHUB_SHA"] + "\n", encoding="ascii")
            public_profile = {"sourceSha": os.environ["GITHUB_SHA"], "mode": label,
                              "deployAllowed": False, "approval": "NOT_GRANTED",
                              "backendAlignment": "UNKNOWN"}
            (dist / "candidate-mode.json").write_text(
                json.dumps(public_profile, sort_keys=True) + "\n", encoding="utf-8")
            manifest = package_tree(dist, output / ("frontend-optionc-" + label.lower() + ".zip"),
                                    os.environ["GITHUB_SHA"], label, allowed)
            manifest.update({"candidate": label, "enabled": allowed, "flagSourceSha256": h(source.encode("utf-8")),
                             "deployAllowed": False, "liveApiBackendReady": "UNVERIFIED"})
            receipts.append(manifest)
        if receipts[0]["sha256"] == receipts[1]["sha256"]:
            raise SourceHold("candidate-assets-not-distinct")
    # The original Git workspace has never had the flags file edited or deployed.
    if (ROOT / FLAGS).read_text() != orig or git("hash-object", FLAGS) != EXPECTED_FLAG_BLOB:
        raise SourceHold("source-workspace-modified")
    binding = {
        "contract": "uat010-optionc-dual-frontend-build-nondeploy-v1",
        "headSha": os.environ["GITHUB_SHA"],
        "sourceFlagsGitBlob": EXPECTED_FLAG_BLOB,
        "lockGitBlob": EXPECTED_LOCK_BLOB,
        "publicConfigGitBlob": EXPECTED_CONFIG_BLOB,
        "featureFlags": sorted(EXPECTED),
        "modes": receipts,
        "originalSourceAndCurrentDeployFlags": "OFF_UNCHANGED",
        "allSixTemporaryOverlayFlags": "ON_IN_ISOLATED_BUILD_ONLY",
        "uatSiteModified": False, "apiModified": False, "sqlModified": False,
        "azureAccess": False, "deployAllowed": False, "g1": "HOLD",
        "businessUat": "NO_GO", "apiPrincipal": "UNKNOWN", "actualPredecessor": "HOLD",
        "recovery": "HOLD", "activationApproval": "NOT_GRANTED",
    }
    (output / "receipt.json").write_text(json.dumps(binding, indent=2, sort_keys=True) + "\n")
    (output / "checksums.sha256").write_text(
        "".join(h(f.read_bytes()) + "  " + f.name + "\n" for f in sorted(output.iterdir()) if f.is_file() and f.name != "checksums.sha256")
    )
    print("UAT010FrontendDualBuild=PASS; OFF_AND_ON_NONDEPLOY=SEALED; SourceDefault=OFF; Azure=NONE; G1=HOLD;")


def selftest() -> None:
    raw = (ROOT / FLAGS).read_text()
    candidate = overlay(raw)
    assert raw != candidate
    for name in EXPECTED:
        assert f"export const {name}: boolean = false;" in raw
        assert f"export const {name}: boolean = true;" in candidate
    try:
        overlay(raw.replace("OPTIONC_CREATE_LOCATION_UI_ENABLED: boolean = false", "OPTIONC_CREATE_LOCATION_UI_ENABLED: boolean = true"))
    except SourceHold:
        pass
    else:
        raise SourceHold("failed-negative-test")
    try:
        overlay(raw.replace("OPTIONC_EXCEL_UPDATE_UI_ENABLED", "OPTIONC_EXCEL_UPDATE_WRONG"))
    except SourceHold:
        pass
    else:
        raise SourceHold("failed-enum-negative-test")
    print("UAT010FrontendOverlayGuard=PASS; Source=OFF; CI=NO_LIVE_TARGET;")


if __name__ == "__main__":
    try:
        parser = argparse.ArgumentParser()
        parser.add_argument("--self-test", action="store_true")
        args = parser.parse_args()
        selftest() if args.self_test else run()
    except Exception:
        print("UAT010FrontendDualBuild=HOLD; Details=SUPPRESSED; SourceDefault=OFF; Live=NONE;")
        sys.exit(1)
