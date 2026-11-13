"""NONDEPLOY exact remote SELECT22 receive-command factory, no live CLI.

The parent may construct an inert public command ONLY for the isolated
verified SELECT22 stage, after external Owner authorization/instance checks.
The command does not contain a token. A future live caller must send exactly
one SQL-only nonce-bound frame over the existing pinned SSH and read a single
redacted receipt. No G1 Installer, Blob, Azure calls or second SQL query.
"""
import base64
import re

from pathb_select22_isolated_ssh_stage_nondeploy import SELECT22_STAGE

CLAIM_DIR = "/home/fv-pathb-select22-claims"
READY = b"FV_SELECT22_READY"
HOLD_WIRE = b'FV_SELECT22_SANITIZED_V1={"status":"HOLD"}\n'


class RemoteCommandHold(Exception):
    pass


def _source(nonce, stage_root):
    return f'''import sys\nsys.path.insert(0,{stage_root!r})\nsys.stdout.buffer.write(b"FV_SELECT22_READY\\n");sys.stdout.buffer.flush()\ntry:\n from pathb_select22_receipt_bridge_nondeploy import app_receive_once\n frame=sys.stdin.buffer.readline(17001)\n out=app_receive_once(frame,{nonce!r},debs_dir={stage_root!r}+"/debs",claim_dir={CLAIM_DIR!r})\nexcept Exception:\n out={HOLD_WIRE!r}\nsys.stdout.buffer.write(out);sys.stdout.buffer.flush()\n'''


def select22_remote_command(nonce):
    if not isinstance(nonce, str) or re.fullmatch("[a-f0-9]{32}", nonce) is None:
        raise RemoteCommandHold("NONCE_HOLD")
    source = _source(nonce, SELECT22_STAGE)
    encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
    return ("python3 -c \"import base64;exec(compile(base64.b64decode('"
            + encoded + "'),'fv-select22-receiver','exec'))\"\n").encode("ascii")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_REMOTE_ENTRYPOINT")
