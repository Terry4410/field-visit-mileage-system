#!/usr/bin/env python3
"""Seal the exact Path B create-only SQL package from pinned accepted inputs.

This is a package-only operation. It never opens a SQL connection or deploys.
"""
from __future__ import annotations
import argparse, hashlib, json, os, shutil, subprocess, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
INSTALL = ROOT / "scripts/ci/uat_release/install.py"
SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DATABASE = "db-fieldvisit-pathb-uat"
ACCEPTED_SHA = "39c86f03cc8d66881a78a800ded974b2f338cf07"
ACCEPTED_RUN = "39981135659"


def sha(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def promoted(raw: bytes, name: str) -> bytes:
    # Reuse the exact guarded rewrite routine shipped with this source SHA.
    from install import promoted as promote_sql
    return promote_sql(raw, name, SERVER, DATABASE)


def verify_inner(root: Path, manifest: dict) -> None:
    mode = manifest["pathb-uat"]
    if (mode.get("server"), mode.get("database"), mode.get("schemaProfile")) != (SERVER, DATABASE, "PathBNewEmpty"):
        raise ValueError("PATHB_PROFILE_MISMATCH")
    sequence = mode.get("sequence")
    if not isinstance(sequence, list) or len(sequence) != 14:
        raise ValueError("PATHB_INSTALL_SEQUENCE_COUNT")
    if [x.get("step") for x in sequence] != list(range(1, 15)):
        raise ValueError("PATHB_INSTALL_SEQUENCE_ORDER")
    for entry in sequence:
        item = root / entry["file"]
        if not item.is_file() or sha(item.read_bytes()) != entry["sha256"]:
            raise ValueError("PATHB_INSTALL_INPUT_HASH")
    if manifest.get("executionDefault") != "DRY_RUN_ONLY_REQUIRES_SEPARATE_EXACT_MANIFEST_APPROVAL":
        raise ValueError("PATHB_EXECUTION_DEFAULT")


def seal_zip(root: Path, destination: Path) -> str:
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for item in sorted(p for p in root.rglob("*") if p.is_file()):
            rel = item.relative_to(root).as_posix()
            info = zipfile.ZipInfo(rel, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, item.read_bytes())
    with zipfile.ZipFile(destination) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("PATHB_PACKAGE_DUPLICATE_PATH")
        for name in names:
            if name.startswith("/") or ".." in Path(name).parts:
                raise ValueError("PATHB_PACKAGE_UNSAFE_PATH")
    return sha(destination.read_bytes())


def self_test() -> None:
    route = b"IF DB_NAME() NOT IN\n   (N'OptionCIsolated_FieldVisitV180CleanInstall',\n    N'OptionCIsolated_FieldVisitV180Replay')\n    THROW 56040, N'CLEAN_ROUTE_DISPOSABLE_TARGET_REQUIRED', 1;\n"
    got = promoted(route, "20_route_reason_check.sql")
    assert got.count(f"DB_NAME()<>N'{DATABASE}'".encode()) == 1
    caps = b"IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%' THROW 56600,N'DISPOSABLE_ONLY',1;\n"
    assert f"DB_NAME()<>N'{DATABASE}'".encode() in promoted(caps, "60_final_functional_capabilities.sql")
    try:
        from install import promoted as promote_sql
        promote_sql(route, "20_route_reason_check.sql", "sql-fieldvisit-jpe-uat.database.windows.net", DATABASE)
    except ValueError:
        pass
    else:
        raise AssertionError("CROSS_PROFILE_TARGET_ACCEPTED")
    print("PATHB_INSTALL_SEAL=GUARD_REWRITE_AND_TARGET_NEGATIVES_PASS; SQL=NOT_ACCESSED")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--accepted", type=Path)
    parser.add_argument("--build", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not all((args.accepted, args.build, args.output)):
        parser.error("--accepted, --build and --output are required")
    source = os.environ.get("GITHUB_SHA", "")
    if len(source) != 40 or any(c not in "0123456789abcdef" for c in source):
        raise ValueError("EXACT_SOURCE_SHA_REQUIRED")
    baseline = json.loads((args.accepted / "it-sql-install-order.json").read_text())
    if baseline.get("candidateSourceSha") != ACCEPTED_SHA or baseline.get("workflowRunId") != ACCEPTED_RUN or baseline.get("historicalMigrationsApplied") is not False:
        raise ValueError("ACCEPTED_INSTALL_BASELINE_MISMATCH")
    if len(baseline.get("sequence", [])) != 13:
        raise ValueError("ACCEPTED_BASELINE_SEQUENCE_MISMATCH")
    build_manifest = json.loads((args.build / "pathb-build-manifest.json").read_text())
    if build_manifest.get("candidateSourceSha") != source or build_manifest.get("profile") != "PathBNewEmpty":
        raise ValueError("PATHB_BUILD_SOURCE_MISMATCH")
    if build_manifest.get("activation") != "OFF" or build_manifest.get("liveSql") != "NOT_ACCESSED" or build_manifest.get("deployment") != "NOT_EXECUTED":
        raise ValueError("PATHB_BUILD_STATE_MISMATCH")
    api = args.build / "api-release-candidate.zip"
    frontend = args.build / "frontend-release-candidate.zip"
    if sha(api.read_bytes()) != build_manifest.get("apiZipSha256") or sha(frontend.read_bytes()) != build_manifest.get("frontendZipSha256"):
        raise ValueError("PATHB_BUILD_ARTIFACT_HASH_MISMATCH")
    final_sql = args.build / "60_final_functional_capabilities.sql"
    if not final_sql.is_file():
        raise ValueError("PATHB_FINAL_SQL_MISSING")

    root = args.output
    if root.exists():
        shutil.rmtree(root)
    (root / "sql").mkdir(parents=True)
    (root / "pathb-uat-sql").mkdir()
    entries = []
    for item in baseline["sequence"]:
        source_file = args.accepted / item["file"]
        raw = source_file.read_bytes()
        if sha(raw) != item["sha256"]:
            raise ValueError("ACCEPTED_BASELINE_INPUT_HASH")
        entries.append({"step": item["step"], "file": item["file"], "sha256": item["sha256"]})
    last = final_sql.read_bytes()
    entries.append({"step": 14, "file": "60_final_functional_capabilities.sql", "sha256": sha(last)})
    for entry in entries:
        name = Path(entry["file"]).name
        source_file = final_sql if entry["step"] == 14 else args.accepted / entry["file"]
        original = source_file.read_bytes()
        fixture_path = root / "sql" / name
        fixture_path.write_bytes(original)
        installed = promoted(original, name)
        pathb_path = root / "pathb-uat-sql" / name
        pathb_path.write_bytes(installed)
        entry["file"] = "pathb-uat-sql/" + name
        entry["sha256"] = sha(installed)
        entry["sourceSha256"] = sha(original)
    # The fixture profile is local-only. The production-adjacent old UAT target
    # is intentionally omitted from this Path B package.
    fixture_sequence = []
    for n, entry in enumerate(baseline["sequence"], 1):
        fixture_sequence.append({"step": n, "file": "sql/" + Path(entry["file"]).name, "sha256": entry["sha256"]})
    fixture_sequence.append({"step": 14, "file": "sql/60_final_functional_capabilities.sql", "sha256": sha(last)})
    manifest = {
        "candidateSourceSha": source,
        "fixture": {"sequence": fixture_sequence},
        "pathb-uat": {"server": SERVER, "database": DATABASE, "schemaProfile": "PathBNewEmpty", "sequence": entries},
        "executionDefault": "DRY_RUN_ONLY_REQUIRES_SEPARATE_EXACT_MANIFEST_APPROVAL"
    }
    (root / "standalone-install-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    shutil.copyfile(INSTALL, root / "install.py")
    shutil.copyfile(ROOT / "scripts/ci/uat_release/sign_activation.py", root / "sign_activation.py")
    shutil.copyfile(api, root / api.name)
    shutil.copyfile(frontend, root / frontend.name)
    shutil.copyfile(args.build / "pathb-build-manifest.json", root / "pathb-build-manifest.json")
    shutil.copyfile(args.build / "release-source-overlay.json", root / "release-source-overlay.json")
    for rel in ("docs/release/g1-execution/promotion/API-RUNTIME-SQL-GRANT-CANDIDATE.sql", "docs/release/g1-execution/promotion/API-RUNTIME-SQL-CRUD-MATRIX.csv", "docs/release/pathb/PATHB-UAT-RELEASE.md"):
        shutil.copyfile(ROOT / rel, root / Path(rel).name)
    package_manifest = {
        "format": "field-visit-pathb-create-only-install-v1",
        "candidateSourceSha": source,
        "acceptedBaseline": {"sha": ACCEPTED_SHA, "run": ACCEPTED_RUN, "status": "NONDEPLOY_SOURCE_BOUND_SCOPE_ONLY_NOT_FINAL_IT_INSTALLER"},
        "profile": "PathBNewEmpty",
        "target": {"server": SERVER, "database": DATABASE, "engineEdition": 5},
        "schema": {"dboTableCount": 69, "schemaSha256": "NOT_BOUND_UNTIL_LIVE_INSTALL_VERIFY"},
        "sequenceCount": 14,
        "installMode": "CREATE_ONLY_EMPTY_DATABASE_REQUIRED",
        "activation": "OFF",
        "runtimeAcl": "CANDIDATE_NOT_APPLIED",
        "liveSql": "NOT_ACCESSED",
        "deployment": "NOT_EXECUTED"
    }
    (root / "pathb-install-package-manifest.json").write_text(json.dumps(package_manifest, indent=2) + "\n")
    verify_inner(root, manifest)
    dry = subprocess.run([sys.executable, str(root / "install.py"), "--root", str(root), "--target", "pathb-uat"], check=True, capture_output=True, text=True)
    if "HASH_VERIFIED_DRY_RUN_NO_CONNECTION" not in dry.stdout:
        raise ValueError("PATHB_INSTALL_DRY_RUN_FAILED")
    checksum = "".join(sha(p.read_bytes()) + "  " + p.relative_to(root).as_posix() + "\n" for p in sorted(root.rglob("*")) if p.is_file() and p.name != "checksums.sha256")
    (root / "checksums.sha256").write_text(checksum)
    digest = seal_zip(root, root.parent / "pathb-install-package.zip")
    print("PATHB_INSTALL_PACKAGE=SEALED_NONDEPLOY_HASHED_DRYRUN_PASS")
    print("PATHB_INSTALL_SEQUENCE=14; TABLES=69; PROFILE=PathBNewEmpty")
    print("PATHB_INSTALL_PACKAGE_SHA256=" + digest)
    print("LIVE_SQL=NOT_ACCESSED; DEPLOYMENT=NOT_EXECUTED; ACTIVATION=OFF")


if __name__ == "__main__":
    main()
