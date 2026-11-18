#!/usr/bin/env python3
"""Offline-only receiver contract for Path B MI Login artifact.

No network, SSH, Azure, SQL, token, extraction or executable entry point.
This is a candidate component, NOT a live transfer authorization.
"""
import hashlib
import os
from pathlib import Path
import re
import stat

MAX_BYTES = 16 * 1024 * 1024
HEX_SHA256 = re.compile(r"[0-9a-f]{64}\Z")


class Hold(Exception):
    pass


def receive_once(directory: Path, name: str, payload: bytes, expected_sha256: str) -> str:
    """Atomically create one non-executable file; fail closed on any ambiguity.

    The caller must establish the identity of the main-container process and
    trusted /tmp ancestry before calling. This function cannot prove either.
    """
    if not isinstance(directory, Path) or not isinstance(payload, bytes):
        raise Hold("input")
    if name != "mi-login-candidate.zip" or not isinstance(expected_sha256, str):
        raise Hold("contract")
    if HEX_SHA256.fullmatch(expected_sha256) is None:
        raise Hold("digest_format")
    if not (0 < len(payload) <= MAX_BYTES):
        raise Hold("size")
    digest = hashlib.sha256(payload).hexdigest()
    if digest != expected_sha256:
        raise Hold("digest")
    if directory.is_symlink() or not directory.is_dir():
        raise Hold("directory")
    st = directory.stat(follow_symlinks=False)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != os.geteuid() or stat.S_IMODE(st.st_mode) != 0o700:
        raise Hold("ownership")
    # Refuse directory replacement between precheck and write using dirfd.
    flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
    dirfd = os.open(directory, flags)
    try:
        opened = os.fstat(dirfd)
        if (opened.st_dev, opened.st_ino) != (st.st_dev, st.st_ino):
            raise Hold("directory_changed")
        fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     0o600, dir_fd=dirfd)
        try:
            with os.fdopen(fd, "wb") as output:
                output.write(payload)
                output.flush()
                os.fsync(output.fileno())
        except BaseException:
            # Leave uncertain partial files in place: never overwrite or retry.
            raise
        checkfd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW, dir_fd=dirfd)
        try:
            check = os.fstat(checkfd)
            if not stat.S_ISREG(check.st_mode) or check.st_nlink != 1 or check.st_size != len(payload):
                raise Hold("postcheck")
            with os.fdopen(checkfd, "rb") as source:
                checkfd = -1
                if hashlib.sha256(source.read(MAX_BYTES + 1)).hexdigest() != digest:
                    raise Hold("readback")
        finally:
            if checkfd >= 0:
                os.close(checkfd)
        return digest
    finally:
        os.close(dirfd)
