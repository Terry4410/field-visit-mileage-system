"""NONDEPLOY isolated SELECT22 stage builder and exact stage archive validator.

The stage deliberately EXCLUDES the frozen G1 Installer, Blob clients,
installation authorization, original SQL package and ALL 14 SQL layers.
No SSH/Azure or token access. It cannot execute SQL; future independently
reviewed sender must pin the archive SHA and use a SELECT22-only receiver.
"""
import hashlib
import io
import json
from pathlib import Path
import re
import zipfile

from pathb_connection_only_tool_stage_nondeploy import (
    HERE, load_bound_config, verify_packages, sha256_file,
    BOUND_CONFIG_SHA256,
)
from pathb_readonly_isolated_gate import verify as verify_fixed_sql

SOURCE_FILES = (
    "binding-index.json",
    "executor_config.json",
    "preflight.sql",
    "pathb_connection_only_tool_stage_nondeploy.py",
    "pathb_readonly_durable_once_20261113.py",
    "pathb_readonly_isolated_gate.py",
    "pathb_readonly_receipt_parser.py",
    "pathb_readonly_sql_contract.py",
    "pathb_readonly_stdin_frame_20261113.py",
    "pathb_select22_persistent_claim_gate_nondeploy.py",
    "pathb_select22_bounded_sql_nondeploy.py",
    "pathb_select22_app_once_candidate.py",
    "pathb_select22_receipt_bridge_nondeploy.py",
    "p3_ssh_transport_contract.py",
)
MANIFEST = "select22-stage-manifest.json"
MAX_ARCHIVE = 64 * 1024 * 1024
MAX_SOURCE_FILE = 4 * 1024 * 1024
MAX_DEB = 75_000_000


class StageHold(Exception):
    pass


def _digest(raw):
    return hashlib.sha256(raw).hexdigest()


def _zip_bytes(members):
    with io.BytesIO() as sink:
        with zipfile.ZipFile(sink, "w") as z:
            for name in sorted(members):
                item = zipfile.ZipInfo(name, (2026, 11, 13, 0, 0, 0))
                item.compress_type = zipfile.ZIP_DEFLATED
                item.external_attr = 0o100600 << 16
                z.writestr(item, members[name])
        value = sink.getvalue()
    if len(value) > MAX_ARCHIVE:
        raise StageHold("STAGE_TOO_LARGE")
    return value


def _names(cfg):
    return sorted(
        [*SOURCE_FILES,
         *(f"debs/{item['name']}.deb" for item in cfg["tools"]),
         MANIFEST])


def build_stage(debs_dir, *, config_loader=load_bound_config, root=HERE):
    """Offline-only: real pinned .deb bytes required; no network download."""
    cfg = config_loader()
    verify_fixed_sql()
    root = Path(root)
    packages = verify_packages(Path(debs_dir), cfg)
    members = {}
    for name in SOURCE_FILES:
        path = root / name
        if (not path.is_file() or path.is_symlink() or
                path.stat().st_size > MAX_SOURCE_FILE):
            raise StageHold("SOURCE_FILE_HOLD")
        members[name] = path.read_bytes()
    if _digest(members["executor_config.json"]) != BOUND_CONFIG_SHA256:
        raise StageHold("CONFIG_PIN_HOLD")
    for item, source in zip(cfg["tools"], packages):
        members["debs/" + item["name"] + ".deb"] = source.read_bytes()
    manifest = {
        "protocol": "FV_PATHB_SELECT22_STAGE_NONDEPLOY_V1",
        "files": {name: _digest(raw) for name, raw in sorted(members.items())},
    }
    members[MANIFEST] = json.dumps(
        manifest, sort_keys=True, separators=(",", ":")).encode("ascii")
    archive = _zip_bytes(members)
    return archive, _digest(archive)


def verify_stage(archive, expected_sha256, *, config_loader=load_bound_config):
    """Always require trusted SHA OUTSIDE the ZIP; self-manifest alone not trust."""
    if (not isinstance(archive, bytes) or len(archive) > MAX_ARCHIVE or
            not isinstance(expected_sha256, str) or
            re.fullmatch("[a-f0-9]{64}", expected_sha256) is None or
            _digest(archive) != expected_sha256):
        raise StageHold("STAGE_EXTERNAL_SHA_HOLD")
    cfg = config_loader()
    wanted = _names(cfg)
    try:
        with zipfile.ZipFile(io.BytesIO(archive)) as z:
            infos = z.infolist()
            names = [i.filename for i in infos]
            if sorted(names) != wanted or len(set(names)) != len(names):
                raise StageHold("STAGE_MEMBER_SET_HOLD")
            for info in infos:
                kind = (info.external_attr >> 16) & 0o170000
                if (info.is_dir() or kind != 0o100000 or
                        info.file_size > (MAX_DEB if info.filename.startswith("debs/")
                                          else MAX_SOURCE_FILE)):
                    raise StageHold("STAGE_UNSAFE_ENTRY_HOLD")
            files = {name: z.read(name) for name in names}
        parsed = json.loads(files.pop(MANIFEST).decode("ascii"))
        if (set(parsed) != {"protocol", "files"} or
                parsed["protocol"] != "FV_PATHB_SELECT22_STAGE_NONDEPLOY_V1" or
                parsed["files"] != {n: _digest(v) for n, v in sorted(files.items())}):
            raise StageHold("STAGE_MANIFEST_HOLD")
        if _digest(files["executor_config.json"]) != BOUND_CONFIG_SHA256:
            raise StageHold("STAGE_CONFIG_PIN_HOLD")
        if _digest(files["preflight.sql"]) != cfg["preflight_sha256"]:
            raise StageHold("STAGE_SQL_PIN_HOLD")
        for item in cfg["tools"]:
            name = "debs/" + item["name"] + ".deb"
            if _digest(files[name]) != item["sha256"]:
                raise StageHold("STAGE_DEB_PIN_HOLD")
        return {"source_files": len(SOURCE_FILES),
                "packages": 5, "sha256": expected_sha256}
    except StageHold:
        raise
    except Exception:
        raise StageHold("STAGE_CONTENT_HOLD") from None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_STAGE_ENTRYPOINT")
