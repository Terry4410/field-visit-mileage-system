"""Offline-tested POSIX process deadline. No Azure/SQL/token acquisition.

Not a live host. A future independently reviewed host must create the CLOSED
connection INSIDE its dedicated child and call adapter.ps1 there. Objects/tokens
are never serialized through this supervisor. Raw child streams are discarded.
"""
import hashlib
import os
from pathlib import Path
import signal
import subprocess
import time

CODES = frozenset({'ADAPTER_COLLECTED_HOLD', 'ADAPTER_GUARD_STOP',
                   'ADAPTER_DEADLINE_STOP', 'ADAPTER_CLEANUP_STOP'})


def supervise(argv, *, reviewed_worker_sha256, result_path, deadline=240):
    """No retries. Deadline covers process launch/wait; OS scheduling is not real time.

    SIGTERM at deadline-2; SIGKILL at deadline-1; bounded reap at deadline.
    Killing the process group is NOT evidence SQL stopped, finally ran, or cost ended.
    Production callers may not extend the 240s ceiling. Short bounds support tests.
    """
    started = time.monotonic()
    child = None
    try:
        if os.name != 'posix' or not 0.1 <= deadline <= 240:
            return 'ADAPTER_GUARD_STOP'
        # A fixed worker file, not a shell expression or arbitrary command string.
        if len(argv) != 2 or not Path(argv[0]).is_absolute():
            return 'ADAPTER_GUARD_STOP'
        worker = Path(argv[1])
        if (not worker.is_absolute() or worker.is_symlink() or
            hashlib.sha256(worker.read_bytes()).hexdigest() != reviewed_worker_sha256):
            return 'ADAPTER_GUARD_STOP'
        result = Path(result_path)
        if result.exists() or not result.parent.is_dir():
            return 'ADAPTER_GUARD_STOP'
        # No token, credential, argv or child output is logged. Future host identity
        # supply and environment allowlist are STILL an integration prerequisite.
        child = subprocess.Popen(argv, stdin=subprocess.DEVNULL,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                 start_new_session=True)
        reserve = min(2.0, deadline * .2)
        try:
            child.wait(timeout=max(.001, deadline-reserve-(time.monotonic()-started)))
        except subprocess.TimeoutExpired:
            os.killpg(child.pid, signal.SIGTERM)
            try:
                child.wait(timeout=max(.001, reserve/2))
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait(timeout=max(.001, deadline-(time.monotonic()-started)))
            return 'ADAPTER_DEADLINE_STOP'
        if child.returncode != 0:
            return 'ADAPTER_GUARD_STOP'
        if result.is_symlink() or result.stat().st_size > 64:
            return 'ADAPTER_GUARD_STOP'
        code = result.read_text(encoding='ascii').strip()
        return code if code in CODES else 'ADAPTER_GUARD_STOP'
    except Exception:
        return 'ADAPTER_GUARD_STOP'
    finally:
        # Also terminate descendants left after normal leader exit. Best effort;
        # process group escape by an untrusted host is not supported or sandboxed.
        if child is not None:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            except OSError:
                pass
