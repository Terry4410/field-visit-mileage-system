"""NONDEPLOY Owner SELECT22 receiver for already-verified App Service shared /home.

This is a command SOURCE FACTORY ONLY; it never connects, stages, creates a
claim, requests a Token, runs SQL, or invokes Azure when imported/called.
A distinct authenticated and newly authorized Owner Parent must establish
App instance/identity provenance, ordered Blob PUT+GET proof, and safe token
transport before dispatching any generated receiver. NOT a live entrypoint.
"""
import base64
import re

ROOT = "/home/fv-select22-transfer-20261114"
APP = "api-fieldvisit-pathb-uat"
MANIFEST_SHA = "67fbd193705e93815f71a39d59c3f4a2ff9cf4d2b5702510a329fa25d338d298"
SQL_SHA = "e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add"
FILES = frozenset({
    "binding-index.json", "executor_config.json", "preflight.sql",
    "p3_ssh_transport_contract.py", "pathb_connection_only_tool_stage_nondeploy.py",
    "pathb_readonly_durable_once_20261113.py", "pathb_readonly_isolated_gate.py",
    "pathb_readonly_receipt_parser.py", "pathb_readonly_sql_contract.py",
    "pathb_readonly_stdin_frame_20261113.py", "pathb_select22_app_once_candidate.py",
    "pathb_select22_bounded_sql_nondeploy.py",
    "pathb_select22_persistent_claim_gate_nondeploy.py",
    "pathb_select22_receipt_bridge_nondeploy.py",
    "debs/libltdl7.deb", "debs/libodbc2.deb", "debs/libodbcinst2.deb",
    "debs/msodbcsql18.deb", "debs/mssql-tools18.deb",
})
READY = b"FV_SELECT22_READY\n"
HOLD = b'FV_SELECT22_SANITIZED_V1={"status":"HOLD"}\n'


class OwnerSharedReceiverHold(Exception):
    pass


def source(nonce):
    """Return inert, fixed Python source; no archive extraction or tool recheck.

    READY is emitted only after an exact pinned 20-file manifest verification.
    A future trusted Owner parent MUST prove the external Blob claim BEFORE
    delivering any confidential SQL frame. This function itself cannot do so.
    """
    if type(nonce) is not str or re.fullmatch(r"[0-9a-f]{32}", nonce) is None:
        raise OwnerSharedReceiverHold("OWNER_RECEIVER_NONCE_HOLD")
    return f'''import hashlib,json,os,stat,sys\nfrom pathlib import Path\nroot=Path({ROOT!r})\nwant={sorted(FILES)!r}\ndef hashfile(p):\n h=hashlib.sha256()\n with p.open("rb") as f:\n  for chunk in iter(lambda:f.read(1048576),b""):\n   h.update(chunk)\n return h.hexdigest()\ndef regular(p):return stat.S_ISREG(p.lstat().st_mode)\ntry:\n if os.environ.get("WEBSITE_SITE_NAME")!={APP!r}:raise ValueError()\n if not stat.S_ISDIR(root.lstat().st_mode):raise ValueError()\n debs=root/"debs"\n if not stat.S_ISDIR(debs.lstat().st_mode):raise ValueError()\n m=root/"select22-stage-manifest.json"\n if not regular(m) or hashfile(m)!={MANIFEST_SHA!r}:raise ValueError()\n spec=json.loads(m.read_bytes())\n if set(spec)!={{"protocol","files"}} or spec["protocol"]!="FV_PATHB_SELECT22_STAGE_NONDEPLOY_V1":raise ValueError()\n if set(spec["files"])!=set(want):raise ValueError()\n if {{x.name for x in os.scandir(root)}}!={{"debs","select22-stage-manifest.json"}}|{{n for n in want if "/" not in n}}:raise ValueError()\n if {{x.name for x in os.scandir(debs)}}!={{n.split("/",1)[1] for n in want if n.startswith("debs/")}}:raise ValueError()\n for name in want:\n  p=root/name\n  if not regular(p) or hashfile(p)!=spec["files"][name]:raise ValueError()\n if hashfile(root/"preflight.sql")!={SQL_SHA!r}:raise ValueError()\n sys.path.insert(0,str(root))\n from pathb_readonly_isolated_gate import verify\n gates=verify(root=root)\n if any(gates.get(k)!="PASS" for k in ("SOURCE_BINDING","PINNED_SQL","EXACT_TARGET")):raise ValueError()\n from pathb_select22_receipt_bridge_nondeploy import app_receive_after_external_claim\nexcept Exception:\n sys.stdout.buffer.write({HOLD!r});sys.stdout.buffer.flush()\nelse:\n sys.stdout.buffer.write({READY!r});sys.stdout.buffer.flush()\n try:\n  frame=sys.stdin.buffer.readline(17001)\n  output=app_receive_after_external_claim(frame,{nonce!r},debs_dir=str(root/"debs"))\n except Exception:\n  output={HOLD!r}\n sys.stdout.buffer.write(output);sys.stdout.buffer.flush()\n'''


def command(nonce):
    """Non-secret command bytes, for source review only; no remote dispatch."""
    raw = base64.b64encode(source(nonce).encode("ascii")).decode("ascii")
    return ("python3 -c \"import base64;exec(compile(base64.b64decode('"
            + raw + "'),'fv-owner-select22-shared-receiver','exec'))\"\n").encode("ascii")


def release_live(*args, **kwargs):
    raise OwnerSharedReceiverHold("OWNER_SHARED_LIVE_PARENT_NOT_RELEASED")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_OWNER_BLOB_OR_SQL_ENTRYPOINT")
