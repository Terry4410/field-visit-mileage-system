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
        "runtimeOverlay