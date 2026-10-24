"""Strict, read-only verification of the immutable PR #48 UAT candidate artifact.

No GitHub/Azure/SQL/network access, no deployment, no activation, and no extraction.
A PASS from this program means the *offline package only* was verified; it
never authorizes changing the current UAT environment or declares UAT GO.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import stat
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


@dataclass(frozen=True)
class Pins:
    candidate_sha: str
    artifact_sha256: str
    run_id: str
    api_zip_sha256: str
    frontend_zip_sha256: str
    api_composite_sha256: str
    schema_sha256: str


# These pins describe the ALREADY ACCEPTED binary release and never follow the
# HEAD SHA of a later tooling-only commit.
ACCEPTED = Pins(
    candidate_sha="9ee5dacceed2bf55c39310d663f3258bcb545989",
    artifact_sha256="88a6bb4a9d88da064da577f9439500d929ec2810581d5982c08665fd1141c324",
    run_id="39998237406",
    api_zip_sha256="9a04e848ae0e6cdbd63a574f4383e074811d71f4fee2c7ea718f5d4df585e848",
    frontend_zip_sha256="f756d00f845752d585a520cd11386a1cc9693c48bee095654728496e0ee00ec7",
    api_composite_sha256="9de20f343388da09d2cd2e4b6cc9a57d1aa93169bb8e9bdc70fd94e47d9ca707",
    schema_sha256="742f2a95f7f58889d1609aceefa656030cf1f1365f10cc9c89968e89cfff0c63",
)

REQUIRED = frozenset({
    "release-manifest.json", "release-contract.json", "schema-contract.json",
    "checksums.sha256", "api-manifest.json", "frontend-manifest.json",
    "api-release-candidate.zip", "frontend-release-candidate.zip",
    "standalone-install-manifest.json", "frozen-25-coverage.json",
    "source-code-immutable-rc.zip", "release-source-delta.zip", "install.py",
    "IT-ACCEPTANCE.md", "sign_activation.py",
})
SHA = re.compile(r"^[0-9a-f]{64}$")


class GateClosed(ValueError):
    """An invalid or incomplete input fails closed without raw diagnostics."""


def digest(buf: bytes) -> str:
    return hashlib.sha256(buf).hexdigest()


def ensure(test: bool) -> None:
    if not test:
        raise GateClosed("CLOSED_CONTRACT")


def archive_members(z: zipfile.ZipFile, *, max_files: int, max_total: int) -> set[str]:
    entries = z.infolist()
    ensure(0 < len(entries) <= max_files)
    names: set[str] = set()
    total = 0
    for entry in entries:
        name = entry.filename
        path = PurePosixPath(name)
        ensure(name not in names and not name.endswith("/"))
        ensure(not path.is_absolute() and bool(path.parts))
        ensure(all(part not in ("", ".", "..") for part in path.parts))
        ensure("\\" not in name and "\x00" not in name)
        mode = (entry.external_attr >> 16) & 0o170000
        ensure(mode in (0, stat.S_IFREG))  # Reject symlinks and special files
        total += entry.file_size
        ensure(0 <= entry.file_size <= max_total and total <= max_total)
        names.add(name)
    return names


def read_small(z: zipfile.ZipFile, name: str, limit: int = 8_000_000) -> bytes:
    info = z.getinfo(name)
    ensure(info.file_size <= limit)
    data = z.read(name)
    ensure(len(data) == info.file_size)
    return data


def read_json(z: zipfile.ZipFile, name: str) -> dict:
    value = json.loads(read_small(z, name))
    ensure(type(value) is dict)
    return value


def check_nested(z: zipfile.ZipFile, package: str, manifest: dict, expected: str) -> None:
    ensure(manifest.get("file") == package)
    ensure(manifest.get("sha256") == expected)
    raw = z.read(package)
    ensure(digest(raw) == expected)
    with zipfile.ZipFile(io.BytesIO(raw)) as nested:
        names = archive_members(nested, max_files=4000, max_total=600_000_000)
        entries = manifest.get("files")
        ensure(type(entries) is list and len(entries) == len(names))
        listed = set()
        for entry in entries:
            ensure(type(entry) is dict)
            name, expected_file_sha = entry.get("path"), entry.get("sha256")
            ensure(type(name) is str and type(expected_file_sha) is str and SHA.fullmatch(expected_file_sha) is not None)
            ensure(name in names and name not in listed)
            listed.add(name)
            ensure(digest(nested.read(name)) == expected_file_sha)
        ensure(listed == names)


def inspect_bytes(blob: bytes, pins: Pins = ACCEPTED) -> dict:
    ensure(digest(blob) == pins.artifact_sha256)
    with zipfile.ZipFile(io.BytesIO(blob)) as z:
        names = archive_members(z, max_files=200, max_total=600_000_000)
        ensure(REQUIRED <= names)
        checks = read_small(z, "checksums.sha256").decode("utf-8")
        listed: set[str] = set()
        for line in checks.splitlines():
            match = re.fullmatch(r"([0-9a-f]{64})  ([^\r\n]+)", line)
            ensure(match is not None)
            sha, name = match.groups()
            ensure(name in names and name != "checksums.sha256" and name not in listed)
            listed.add(name)
            ensure(digest(z.read(name)) == sha)
        ensure(listed == names - {"checksums.sha256"})

        release = read_json(z, "release-manifest.json")
        contract = read_json(z, "release-contract.json")
        api = read_json(z, "api-manifest.json")
        frontend = read_json(z, "frontend-manifest.json")
        coverage = read_json(z, "frozen-25-coverage.json")
        installer = read_json(z, "standalone-install-manifest.json")

        ensure(release.get("candidateSourceSha") == pins.candidate_sha)
        ensure(str(release.get("runId")) == pins.run_id)
        ensure(release.get("activationDefault") == "OFF")
        ensure(release.get("physicalIphone") == "NOT_EXECUTED")
        ensure(release.get("liveUat") == "NOT_AUTHORIZED_NOT_EXECUTED")
        ensure(release.get("binding") == contract)
        ensure(contract.get("candidateSourceSha") == pins.candidate_sha)
        ensure(contract.get("apiSha256") == pins.api_composite_sha256)
        ensure(contract.get("frontendSha256") == pins.frontend_zip_sha256)
        ensure(contract.get("schemaSha256") == pins.schema_sha256)
        ensure(digest(z.read("schema-contract.json")) == pins.schema_sha256)
        packages = release.get("packages")
        ensure(type(packages) is list and len(packages) == 2)
        ensure({(x.get("file"), x.get("sha256")) for x in packages if type(x) is dict} == {
            ("api-release-candidate.zip", pins.api_zip_sha256),
            ("frontend-release-candidate.zip", pins.frontend_zip_sha256),
        })
        check_nested(z, "api-release-candidate.zip", api, pins.api_zip_sha256)
        check_nested(z, "frontend-release-candidate.zip", frontend, pins.frontend_zip_sha256)

        ensure(coverage.get("caseCount") == 25)
        ensure(coverage.get("notAllCasesClaimedPass") is True)
        cases = coverage.get("cases")
        ensure(type(cases) is list and len(cases) == 25)
        ids = set()
        for case in cases:
            ensure(type(case) is dict and type(case.get("case")) is str)
            ensure(re.fullmatch(r"(?:VIS|LEAD|ADMIN|XLS|TRIP|SEC)-\d\d", case["case"]) is not None)
            ensure(case["case"] not in ids)
            ids.add(case["case"])
            ensure(case.get("technicalCoverage") == "PARTIAL")
            ensure(case.get("liveBusinessUat") == "NOT_EXECUTED")
        ensure(installer.get("candidateSourceSha") == pins.candidate_sha)
        ensure(installer.get("executionDefault") == "DRY_RUN_ONLY_REQUIRES_SEPARATE_EXACT_MANIFEST_APPROVAL")
        uat, fixture = installer.get("uat"), installer.get("fixture")
        ensure(type(uat) is dict and type(fixture) is dict)
        ensure(uat.get("server") == "sql-fieldvisit-jpe-uat.database.windows.net")
        ensure(uat.get("database") == "db-fieldvisit-uat")
        for target in (uat, fixture):
            seq = target.get("sequence")
            ensure(type(seq) is list and len(seq) == 14)
            ensure([x.get("step") for x in seq] == list(range(1, 15)))
            for item in seq:
                ensure(type(item) is dict and item.get("file") in names)
                ensure(type(item.get("sha256")) is str and SHA.fullmatch(item["sha256"]) is not None)
                ensure(digest(z.read(item["file"])) == item["sha256"])
        return {
            "status": "HOLD_LIVE_G1",
            "offlinePackage": "PASS",
            "candidateSourceSha": pins.candidate_sha,
            "acceptedRun": pins.run_id,
            "artifacts": "ZIP_AND_NESTED_HASHES_PASS",
            "features": "DEFAULT_OFF",
            "frozen25": "PARTIAL_25_LIVE_NOT_EXECUTED",
            "physicalIphone": "NOT_EXECUTED",
            "deployment": "NOT_PERFORMED",
            "azure": "NOT_ACCESSED",
            "sql": "NOT_ACCESSED",
            "write": "NONE",
        }


def main() -> int:
    parser = argparse.ArgumentParser(description="Strict offline candidate verify; NEVER enables UAT")
    parser.add_argument("--artifact", type=Path, required=True, help="Original GitHub Actions Artifact ZIP (unmodified)")
    args = parser.parse_args()
    try:
        if not args.artifact.is_file() or args.artifact.stat().st_size > 150_000_000:
            raise GateClosed("INPUT")
        report = inspect_bytes(args.artifact.read_bytes())
        print(json.dumps(report, sort_keys=True, separators=(",", ":")))
        return 0  # This process PASS means offline-package-only; status remains HOLD.
    except Exception:  # never print raw inputs, paths, secrets, payload, exception
        print('{"status":"HOLD","offlinePackage":"UNKNOWN","azure":"NOT_ACCESSED","sql":"NOT_ACCESSED","deployment":"NOT_PERFORMED","write":"NONE"}')
        return 1


if __name__ == "__main__":
    sys.exit(main())
