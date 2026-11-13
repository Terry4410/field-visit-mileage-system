"""NONDEPLOY isolated SELECT22 SSH stage protocol (no live CLI).

Reuses pinned G1 SSH only as a channel, NEVER its installer stage/runner.
Remote receiver allows ONE fixed SELECT22 stage root, verifies the externally
pinned exact distribution SHA before writing, and refuses existing directories.
It does not dispatch SQL, Azure, Token, Blob or Installer.
"""
import base64
import re
from pathlib import Path

from p3_kudu_transport import TransportHold, iter_stage_frame
from pathb_select22_isolated_stage_nondeploy import verify_stage, MAX_ARCHIVE
from pathb_select22_ssh_session_nondeploy import Select22SshSession

SELECT22_STAGE = "/tmp/fv-pathb-select22-isolated-stage-v1"


def _bootstrap_source(expected, size, root):
    """Return fixed stdlib-only receiver source, not an executable action."""
    return f'''import base64,hashlib,io,os,re,sys,zipfile\nfrom pathlib import Path,PurePosixPath\ndef req(condition):\n if not condition:raise SystemExit(90)\nroot=Path({root!r})\nexpected={expected!r};size={size};maximum={MAX_ARCHIVE}\nheader=sys.stdin.buffer.readline(256)\nreq(header==("FV_KUDU_STAGE_V1 %d %s\\n"%(size,expected)).encode())\nchunks=[];count=0\nwhile True:\n line=sys.stdin.buffer.readline(78)\n req(bool(line))\n if line==b"FV_KUDU_STAGE_END\\n":break\n req(line.endswith(b"\\n") and 1<len(line)<=77)\n rawpart=line[:-1]\n req(re.fullmatch(rb"[A-Za-z0-9+/=]+",rawpart) is not None)\n chunks.append(rawpart);count+=len(rawpart)\n req(count<=((maximum+2)//3)*4)\nraw=base64.b64decode(b"".join(chunks),validate=True)\nreq(len(raw)==size and hashlib.sha256(raw).hexdigest()==expected)\nwith zipfile.ZipFile(io.BytesIO(raw)) as z:\n infos=z.infolist();names=[i.filename for i in infos]\n req(bool(names) and len(names)==len(set(names)))\n for info in infos:\n  p=PurePosixPath(info.filename)\n  kind=(info.external_attr>>16)&0o170000\n  req(not info.is_dir() and not p.is_absolute() and ".." not in p.parts and kind==0o100000 and info.file_size<=75_000_000)\n req(not root.exists() and not root.is_symlink())\n old=os.umask(0o077)\n try:\n  root.mkdir(mode=0o700)\n  for info in infos:\n   p=root/PurePosixPath(info.filename)\n   p.parent.mkdir(mode=0o700,parents=True,exist_ok=True)\n   fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_EXCL|getattr(os,"O_NOFOLLOW",0),0o600)\n   with os.fdopen(fd,"wb") as f:f.write(z.read(info))\n finally:os.umask(old)\nprint("\\nFV_KUDU_STAGE_READY",flush=True)\n'''


def select22_bootstrap_command(expected_sha256, size):
    if (not isinstance(expected_sha256, str) or
            re.fullmatch("[a-f0-9]{64}", expected_sha256) is None or
            type(size) is not int or size <= 0 or size > MAX_ARCHIVE):
        raise TransportHold("STAGE_HASH_HOLD")
    source = _bootstrap_source(expected_sha256, size, SELECT22_STAGE)
    encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
    return ("python3 -c \"import base64;exec(compile(base64.b64decode('"
            + encoded + "'),'fv-select22-stage','exec'))\"\n").encode("ascii")


class Select22StageSession(Select22SshSession):
    """Pinned SSH + isolated stage; no installer command is exposed."""

    def stage_select22_archive(self, raw, expected_sha256):
        if self.state.state != "CHANNEL":
            raise TransportHold("PROTOCOL_STATE_HOLD")
        try:
            verify_stage(raw, expected_sha256)
            command = select22_bootstrap_command(expected_sha256, len(raw))
        except Exception:
            raise TransportHold("STAGE_HASH_HOLD") from None
        boundary = len(self.buffer)
        try:
            self.send_public(command, "SELECT22_STAGE_RECEIVER_SENT")
            for chunk in iter_stage_frame(raw, expected_sha256):
                self._write_all(chunk)
            self.audit.append("SELECT22_STAGE_FRAME_SENT")
            self.expect_fixed(b"FV_KUDU_STAGE_READY", 300,
                              "SELECT22_STAGE_READY", after=boundary)
            self.state.advance("CHANNEL", "STAGED")
        except Exception:
            self.state.unknown()
            raise TransportHold("STAGE_CHANNEL_HOLD") from None

    def stage_archive(self, *args, **kwargs):
        """Disable inherited G1 installer stage path."""
        raise TransportHold("STAGE_PATH_HOLD")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_STAGE_ENTRYPOINT")
