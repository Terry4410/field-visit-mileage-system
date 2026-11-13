"""NONDEPLOY exact remote SELECT22 receive-command factory, no live CLI.

The parent may construct an inert public command ONLY for the isolated
verified SELECT22 stage, after external Owner authorization/instance checks.
The Owner Parent MUST consume external Blob claim before requesting SQL Token.
The command does not contain a token. A future live caller must send exactly
one SQL-only nonce-bound frame over the existing pinned SSH and read a single
redacted receipt. No G1 Installer, Blob, Azure calls or second SQL query.
"""
import base64
import re

from pathb_select22_isolated_ssh_stage_nondeploy import SELECT22_STAGE

READY = b"FV_SELECT22_READY"
HOLD_WIRE = b'FV_SELECT22_SANITIZED_V1={"status":"HOLD"}\n'


class RemoteCommandHold(Exception):
    pass


def _source(nonce, stage_root):
    """Fixed remote stdlib receiver. NO READY until exact package/source gates pass.

    This runs only in a future separately approved authenticated SSH session.
    All pre-READY work is non-SQL and non-Token. A failed preflight emits only
    one redacted HOLD line and MUST NOT cause the Parent to create Blob claim.
    """
    return f'''import sys
from pathlib import Path
sys.path.insert(0,{stage_root!r})
try:
 from pathb_select22_receipt_bridge_nondeploy import app_receive_after_external_claim
 from pathb_connection_only_tool_stage_nondeploy import load_bound_config,stage_offline
 from pathb_readonly_isolated_gate import verify as verify_fixed_sql
 from shutil import which
 cfg=load_bound_config()
 gates=verify_fixed_sql()
 if gates.get("SOURCE_BINDING")!="PASS" or gates.get("PINNED_SQL")!="PASS" or gates.get("EXACT_TARGET")!="PASS":
  raise ValueError("SOURCE_HOLD")
 if which("dpkg-deb") is None:
  raise ValueError("TOOL_HOLD")
 if stage_offline(cfg,Path({stage_root!r})/"debs")!="PASS":
  raise ValueError("TOOL_HOLD")
except Exception:
 sys.stdout.buffer.write({HOLD_WIRE!r});sys.stdout.buffer.flush()
else:
 sys.stdout.buffer.write(b"FV_SELECT22_READY\\n");sys.stdout.buffer.flush()
 try:
  frame=sys.stdin.buffer.readline(17001)
  out=app_receive_after_external_claim(frame,{nonce!r},debs_dir={stage_root!r}+"/debs")
 except Exception:
  out={HOLD_WIRE!r}
 sys.stdout.buffer.write(out);sys.stdout.buffer.flush()
'''

def select22_remote_command(nonce):
    if not isinstance(nonce, str) or re.fullmatch("[a-f0-9]{32}", nonce) is None:
        raise RemoteCommandHold("NONCE_HOLD")
    source = _source(nonce, SELECT22_STAGE)
    encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
    return ("python3 -c \"import base64;exec(compile(base64.b64decode('"
            + encoded + "'),'fv-select22-receiver','exec'))\"\n").encode("ascii")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_REMOTE_ENTRYPOINT")
