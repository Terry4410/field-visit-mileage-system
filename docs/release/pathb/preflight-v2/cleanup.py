#!/usr/bin/env python3
"""Independent local watchdog/cleanup/verify. No SQL, network or credentials read."""
import os,shutil,signal,sys,time
from pathlib import Path
ROOT=Path('/tmp/fv-pathb-install-d6c2208-once1');RAM=Path('/dev/shm/fv-pathb-install-d6c2208-once1')
def owned_processes():
    found=[]
    for p in Path('/proc').glob('[0-9]*'):
        try:
            pid=int(p.name)
            if pid in [os.getpid(),os.getppid()]:continue
            parts=(p/'cmdline').read_bytes().split(b'\0')
            if any(x.startswith(str(ROOT).encode()+b'/') for x in parts):found.append(pid)
        except (OSError,ValueError):pass
    return found
def cleanup(kill_only=False):
    for pid in owned_processes():
        try:os.kill(pid,signal.SIGKILL)
        except ProcessLookupError:pass
    if not kill_only:
        for p in [RAM,ROOT]:
            if p.is_symlink():p.unlink()
            elif p.exists():shutil.rmtree(p)
def watch(parent,deadline):
    while time.monotonic()<deadline:
        try:os.kill(parent,0)
        except ProcessLookupError:break
        time.sleep(.25)
    else:
        try:os.kill(parent,signal.SIGTERM)
        except ProcessLookupError:pass
        time.sleep(2)
        try:os.kill(parent,signal.SIGKILL)
        except ProcessLookupError:pass
    cleanup()
if __name__=='__main__':
    if '--watch' in sys.argv:watch(int(sys.argv[2]),float(sys.argv[3]))
    elif '--verify' in sys.argv:sys.exit(int(ROOT.exists() or ROOT.is_symlink() or RAM.exists() or RAM.is_symlink() or bool(owned_processes())))
    else:cleanup('--kill-only' in sys.argv)
