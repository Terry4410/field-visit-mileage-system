"""NONDEPLOY SELECT22-only SQL client child process, independent of G1 Installer.

The process-control pattern is derived from frozen G1 sql_call; unlike G1,
this module cannot import or invoke installer/Blob/token acquisition.
One verified immutable SELECT, one process spawn, no auto retry.
Offline tests use a harmless synthetic child, never an actual SQL database.
"""
import os
from pathlib import Path
import signal
import subprocess
import time

from pathb_readonly_sql_contract import sqlcmd_argv


class Select22SqlHold(Exception):
    pass


def _alive(pgid):
    try:
        os.killpg(pgid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


def _stop_group(pgid, leader):
    for sig in (signal.SIGTERM, signal.SIGKILL):
        leader.poll()
        if not _alive(pgid):
            return True
        try:
            os.killpg(pgid, sig)
        except ProcessLookupError:
            return True
        stop_at = time.monotonic() + (0.5 if sig == signal.SIGTERM else 5)
        while time.monotonic() < stop_at:
            leader.poll()
            if not _alive(pgid):
                return True
            time.sleep(0.05)
    leader.poll()
    return not _alive(pgid)


def sql_select22_once(binary, env, tokenfile, raw, deadline, index):
    """One SQL child and fixed pinned SQL, always kill/reap process group.

    Even an execution/transport failure is a terminal HOLD. Caller must have
    consumed a persistent claim BEFORE reaching this function.
    """
    if (index != 1 or not isinstance(binary, Path)
            or not isinstance(tokenfile, Path)
            or not isinstance(env, dict) or "PATH" not in env
            or any(name.startswith(("SQLCMD", "OSQL", "AZURE_"))
                   or name in {"IDENTITY_ENDPOINT", "IDENTITY_HEADER",
                               "MSI_ENDPOINT", "MSI_SECRET"}
                   for name in env)
            or time.monotonic() >= deadline):
        raise Select22SqlHold("SQL_PRECONDITION_HOLD")
    try:
        args = sqlcmd_argv(binary, tokenfile, raw)
    except Exception:
        raise Select22SqlHold("SQL_INPUT_HOLD") from None
    child = None
    outcome = None
    try:
        child = subprocess.Popen(
            args,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
            start_new_session=True,
        )
        try:
            out, _err = child.communicate(
                raw, timeout=min(110, max(0.01, deadline - time.monotonic())))
        except subprocess.TimeoutExpired:
            raise Select22SqlHold("SQL_TIMEOUT_OUTCOME_UNKNOWN") from None
        if child.returncode != 0:
            raise Select22SqlHold("SQL_CHILD_NONZERO_HOLD")
        if _alive(child.pid):
            raise Select22SqlHold("SQL_DESCENDANTS_HOLD")
        outcome = out
    except Select22SqlHold:
        raise
    except Exception:
        raise Select22SqlHold("SQL_LAUNCH_HOLD") from None
    finally:
        if child is not None:
            stopped = _stop_group(child.pid, child)
            for stream in (child.stdin, child.stdout, child.stderr):
                if stream is not None and not stream.closed:
                    stream.close()
            if not stopped:
                raise Select22SqlHold("SQL_PROCESS_GROUP_NOT_CLEARED") from None
    return outcome


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_SQL_ENTRYPOINT")
