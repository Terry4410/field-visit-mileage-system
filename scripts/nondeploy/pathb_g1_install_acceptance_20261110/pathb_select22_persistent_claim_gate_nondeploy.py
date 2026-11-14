"""NONDEPLOY strict durable-claim storage gate for SELECT22 (no live CLI).

A /dev/shm or /tmp O_EXCL claim is *not* restart durable. This gate requires
an existing owner-private directory on a verified /home CIFS/SMB mount BEFORE
the existing O_EXCL/fsync-once control may run. Never creates/removes the claim.
A missing or unproven mount is terminal HOLD, not a fallback to ephemeral RAM.
This policy is still subject to independent live filesystem/mount verification.
"""
import os
from pathlib import Path
import stat

CLAIM_DIR = Path("/home/fv-pathb-select22-claims")
_ALLOWED_FS = frozenset(("cifs", "smb3"))


class PersistentClaimHold(Exception):
    pass


def _mounted_home(mountinfo):
    if not isinstance(mountinfo, str):
        raise PersistentClaimHold("MOUNTINFO_HOLD")
    found = []
    for line in mountinfo.splitlines():
        parts = line.split(" - ", 1)
        if len(parts) != 2:
            continue
        left, right = parts
        a, b = left.split(), right.split()
        if len(a) >= 5 and len(b) >= 1 and a[4] == "/home":
            found.append(b[0])
    if len(found) != 1 or found[0] not in _ALLOWED_FS:
        raise PersistentClaimHold("PERSISTENT_HOME_NOT_PROVEN")
    return True


def verify_persistent_claim_dir(path, *, mountinfo=None):
    """Fail closed if not a known persistent mount; never return a RAM path."""
    p = Path(path)
    if p != CLAIM_DIR or p.is_symlink():
        raise PersistentClaimHold("CLAIM_PATH_HOLD")
    try:
        s = p.lstat()
        if (not stat.S_ISDIR(s.st_mode) or s.st_uid != os.geteuid()
                or stat.S_IMODE(s.st_mode) != 0o700):
            raise PersistentClaimHold("CLAIM_MODE_HOLD")
        if mountinfo is None:
            mountinfo = Path("/proc/self/mountinfo").read_text("utf-8")
        _mounted_home(mountinfo)
    except (OSError, ValueError, UnicodeError):
        raise PersistentClaimHold("PERSISTENCE_UNVERIFIED_HOLD") from None
    return p


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_ENTRYPOINT")
