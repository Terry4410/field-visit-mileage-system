#!/usr/bin/env python3
"""NONDEPLOY read-only capability observation, no Azure/SSH/SQL/Token/files created.

This cannot prove future cross-session persistence or support a LIVE SQL GO.
On a real App Service instance only, under separate Owner authorization, observe
existing /home/LogFiles owner, access, and directory fsync capability.
"""
import os
from pathlib import Path
import stat

TARGET = Path('/home/LogFiles')


def inspect(directory=TARGET, uid=None, fsync_fn=os.fsync, open_fn=os.open, close_fn=os.close):
    if uid is None:
        uid = os.geteuid()
    try:
        directory = Path(directory)
        # No symlink or directory traversal to unrelated storage.
        if not directory.is_absolute() or directory.is_symlink():
            return 'HOLD_TARGET'
        st = directory.lstat()
        if not stat.S_ISDIR(st.st_mode) or st.st_uid != uid:
            return 'HOLD_OWNER_OR_DIRECTORY'
        fd = open_fn(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            actual = os.fstat(fd)
            if (not stat.S_ISDIR(actual.st_mode) or actual.st_dev != st.st_dev
                    or actual.st_ino != st.st_ino or actual.st_uid != uid):
                return 'HOLD_RACE_OR_OWNER'
            fsync_fn(fd)  # does not create or modify a file
        finally:
            close_fn(fd)
        return 'DIRECTORY_FSYNC_SUPPORTED_ONLY'
    except (OSError, ValueError, TypeError):
        return 'HOLD_DIRECTORY_FSYNC_OR_ACCESS'


def main():
    result = inspect()
    print('HOME_DIRECTORY_CAPABILITY=' + result)
    print('FILE_WRITE=NO; TOKEN=NO; SQL=NO; SSH=NO; BLOB=NO; RETRY=FORBIDDEN')
    print('DURABLE_RECEIPT_END_TO_END=UNVERIFIED')
    return 0 if result == 'DIRECTORY_FSYNC_SUPPORTED_ONLY' else 1


if __name__ == '__main__':
    raise SystemExit(main())
