#!/usr/bin/env python3
"""Create a deterministic, NONDEPLOY package for the tested RC-derived API."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def candidate_is_default_off(root: Path) -> bool:
    configs = sorted(root.glob("appsettings*.json"))
    if not configs:
        raise ValueError("CANDIDATE_CONFIG_MISSING")
    for path in configs:
        data = json.loads(path.read_text(encoding="utf-8"))
        option_c = data.get("OptionC", {})
        candidate = option_c.get("Candidate", {})
        if candidate.get("Enabled", False) is not False:
            return False
        connection = data.get("ConnectionStrings", {}).get("DefaultConnection")
        if connection != "SET-IN-AZURE-APP-SERVICE":
            return False
        auth = data.get("Auth", {})
        if (auth.get("JwtKey") != "SET-IN-AZURE-APP-SERVICE-AT-LEAST-32-CHARS"
                or auth.get("DemoPassword") != "SET-IN-AZURE-APP-SERVICE"):
            return False
    return True


def package(api_root: Path, output: Path, source_sha: str, rc_sha: str,
            overlay_path: Path) -> dict:
    api_root = api_root.resolve()
    output = output.resolve()
    if not (api_root / "FieldVisit.Api.dll").is_file():
        raise ValueError("CANDIDATE_DLL_MISSING")
    if len(source_sha) != 40 or any(c not in "0123456789abcdef" for c in source_sha):
        raise ValueError("SOURCE_SHA_INVALID")
    if rc_sha != "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a":
        raise ValueError("RC_SHA_INVALID")
    overlay = json.loads(overlay_path.read_text(encoding="utf-8"))
    if (overlay.get("format") != "field-visit-clean-install-rc-runtime-overlay-v1"
            or overlay.get("rcSourceSha") != rc_sha
            or overlay.get("candidateSourceSha256") !=
            "40d7ce4079e634b2d048eb9badc209518a622dfe7bbb7a5f1c80d325e580d77f"
            or overlay.get("deploymentStatus") != "NONDEPLOY_DISPOSABLE_CANDIDATE_ONLY"):
        raise ValueError("CANDIDATE_OVERLAY_INVALID")
    if not candidate_is_default_off(api_root):
        raise ValueError("CANDIDATE_DEFAULT_OR_CONFIGURATION_UNSAFE")

    files = sorted(p for p in api_root.rglob("*") if p.is_file())
    if not files:
        raise ValueError("CANDIDATE_PACKAGE_EMPTY")
    file_entries = [
        {"path": p.relative_to(api_root).as_posix(), "sha256": sha256(p.read_bytes()),
         "sizeBytes": p.stat().st_size}
        for p in files
    ]
    zip_path = output / "api-candidate-nondeploy.zip"
    output.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=9, strict_timestamps=True) as archive:
        for p in files:
            info = zipfile.ZipInfo(p.relative_to(api_root).as_posix(), date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, p.read_bytes(), compress_type=zipfile.ZIP_DEFLATED,
                             compresslevel=9)
    package_bytes = zip_path.read_bytes()
    manifest = {
        "format": "field-visit-v180-runtime-candidate-package-v1",
        "status": "NONDEPLOY_TESTED_CANDIDATE_NOT_UAT_AUTHORIZED",
        "sourceCommit": source_sha,
        "immutableRuntimeRc": rc_sha,
        "runtimeOverlay": overlay,
        "runtimeOverlayManifestSha256": sha256(overlay_path.read_bytes()),
        "optionCCandidateDefault": "OFF",
        "liveDeploymentAuthorized": False,
        "files": file_entries,
        "package": {"file": zip_path.name, "sizeBytes": len(package_bytes),
                    "sha256": sha256(package_bytes)},
    }
    (output / "api-candidate-manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    # Independent readback verifies every packaged path/hash and the package digest.
    with zipfile.ZipFile(zip_path, "r") as archive:
        if set(archive.namelist()) != {x["path"] for x in file_entries}:
            raise ValueError("CANDIDATE_ZIP_ENTRY_MISMATCH")
        for item in file_entries:
            if sha256(archive.read(item["path"])) != item["sha256"]:
                raise ValueError("CANDIDATE_ZIP_READBACK_MISMATCH")
    if sha256(zip_path.read_bytes()) != manifest["package"]["sha256"]:
        raise ValueError("CANDIDATE_ZIP_DIGEST_MISMATCH")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--api-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--rc-sha", required=True)
    parser.add_argument("--overlay-manifest", type=Path, required=True)
    args = parser.parse_args()
    try:
        manifest = package(args.api_root, args.output, args.source_sha,
                           args.rc_sha, args.overlay_manifest)
    except (OSError, ValueError, json.JSONDecodeError):
        print("CLEAN_INSTALL_CANDIDATE_PACKAGE=FAIL_CLOSED")
        return 1
    print("CLEAN_INSTALL_CANDIDATE_PACKAGE=NONDEPLOY_PASS")
    print("CLEAN_INSTALL_CANDIDATE_PACKAGE_SHA256=" + manifest["package"]["sha256"])
    print("CLEAN_INSTALL_CANDIDATE_DEFAULT=OFF")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
