"""NONDEPLOY durable one-use gate for a future separately reviewed operator.

Atomic O_EXCL creates a terminal claim *before* any token or SQL dispatch.
A crash leaves the claim spent; no automatic retry. Never delete the claim.
The caller supplies a private, already-validated directory. No live entrypoint.
"""
import os
import stat
from pathlib import Path

class DurableHold(Exception):
    pass

CLAIM_NAME = "readonly-sql-attempt-spent"

def consume_once(private_dir):
    try:
        root = Path(private_dir)
        s = root.lstat()
        if (not stat.S_ISDIR(s.st_mode) or s.st_uid != os.geteuid()
                or stat.S_IMODE(s.st_mode) != 0o700):
            raise DurableHold("PRIVATE_DIRECTORY_HOLD")
        fd = os.open(str(root / CLAIM_NAME),
                     os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
                     0o600)
        try:
            os.write(fd, b"SPENT\n")
            os.fsync(fd)
        finally:
            os.close(fd)
        dir_fd = os.open(str(root), os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(dir_fd)
        finally:
            os.close(dir_fd)
        return "SPENT"
    except Exception:
        raise DurableHold("ATTEMPT_SPENT_OR_UNVERIFIED") from None

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_DISPATCH")
