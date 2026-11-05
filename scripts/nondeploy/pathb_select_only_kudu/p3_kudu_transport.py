#!/usr/bin/env python3
"""Authorization-locked Kudu transport adapter for Field Visit Path B P3.

This module is transport-only.  Importing it performs no Azure, Kudu, Blob,
SSH, token, SQL, marker, or deployment operation.  It deliberately has no
live CLI entrypoint.  A future exact-SHA parent may call the explicitly named
functions only after separate Owner authorization and all existing P3 gates.

The adapter uses the official preview ``az webapp exec`` interactive channel
to the *main app container*.  It never uses detached execute mode because an
accepted detached request does not prove execution, completion, exit status,
or exactly-once behavior.  The command has no built-in file transfer; a
hash-bound Base64 frame is carried over authenticated interactive stdin.
"""

from __future__ import annotations

import base64
import hashlib
import io
import json
import os
import pty
import re
import select
import signal
import subprocess
import termios
import time
import zipfile
from pathlib import Path, PurePosixPath
from typing import Callable, Iterable, Iterator, Mapping, Sequence

RESOURCE_GROUP = "rg-fieldvisit-uat"
APP = "api-fieldvisit-pathb-uat"
SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DATABASE = "db-fieldvisit-pathb-uat"
MIN_AZURE_CLI = (2, 90, 0)
SHELL = "/bin/bash"
TRANSPORT_CONTRACT = "FIELDVISIT_PATHB_P3_KUDU_STDIN_TRANSPORT_V1"
REMOTE_STAGE = "/tmp/fv-pathb-select-only-live-v1-20261105"
MAX_STAGE_BYTES = 64 * 1024 * 1024
MAX_OUTPUT_BYTES = 2 * 1024 * 1024
FRAME_WIDTH = 76

SAFE_CODES = frozenset({
    "AZ_CLI_VERSION_HOLD",
    "INSTANCE_LIST_HOLD",
    "INSTANCE_NOT_EXACTLY_ONE",
    "INSTANCE_ID_HOLD",
    "KUDU_PROCESS_HOLD",
    "KUDU_CHANNEL_TIMEOUT",
    "KUDU_CHANNEL_CLOSED",
    "KUDU_OUTPUT_LIMIT",
    "KUDU_SECRET_ECHO",
    "KUDU_WRITE_HOLD",
    "STAGE_SHAPE_HOLD",
    "STAGE_SIZE_HOLD",
    "STAGE_HASH_HOLD",
    "STAGE_ARCHIVE_HOLD",
    "STAGE_PATH_HOLD",
    "TOKEN_HANDOFF_HOLD",
    "PROTOCOL_STATE_HOLD",
    "ONCE_ONLY_HOLD",
    "UNKNOWN_COMMIT_HOLD",
    "CLEANUP_HOLD",
    "UNCLASSIFIED_HOLD",
})


class TransportHold(Exception):
    """Machine-safe failure.  Never contains raw Azure/Kudu/container text."""

    def __init__(self, code: str):
        self.code = code if code in SAFE_CODES else "UNCLASSIFIED_HOLD"
        super().__init__(self.code)


def require(condition: bool, code: str) -> None:
    if not condition:
        raise TransportHold(code)


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def safe_code(exc: BaseException) -> str:
    """Return a fixed code only; never serialize the exception or raw output."""

    return exc.code if isinstance(exc, TransportHold) else "UNCLASSIFIED_HOLD"


def validate_cli_version(record: Mapping[str, object]) -> tuple[int, int, int]:
    """Require Azure CLI >=2.90.0 from the parsed ``az version`` JSON."""

    require(isinstance(record, Mapping), "AZ_CLI_VERSION_HOLD")
    value = str(record.get("azure-cli", ""))
    match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z.-]+)?", value)
    require(match is not None, "AZ_CLI_VERSION_HOLD")
    version = tuple(int(part) for part in match.groups())
    require(version >= MIN_AZURE_CLI, "AZ_CLI_VERSION_HOLD")
    return version


def select_single_instance(records: object) -> str:
    """Pin one exact main-container instance; zero or multiple is a HOLD."""

    require(isinstance(records, list), "INSTANCE_LIST_HOLD")
    require(len(records) == 1, "INSTANCE_NOT_EXACTLY_ONE")
    row = records[0]
    require(isinstance(row, Mapping), "INSTANCE_LIST_HOLD")
    # Azure CLI returns the runnable worker identifier as ``name``.  ``id`` is
    # the ARM resource ID and must never be passed to --instance.
    candidates = [row.get("name"), row.get("instanceId")]
    values = [str(value) for value in candidates if value not in (None, "")]
    require(bool(values) and len(set(values)) == 1, "INSTANCE_ID_HOLD")
    instance = values[0]
    require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{7,255}", instance) is not None,
            "INSTANCE_ID_HOLD")
    return instance


def interactive_exec_args(instance: str) -> list[str]:
    """Exact official interactive command for the app's main container.

    There is intentionally no ``--mode execute``, ``--command``,
    ``--shell-command``, ``--target kudu``, slot, or multi-instance selector.
    """

    require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{7,255}", str(instance)) is not None,
            "INSTANCE_ID_HOLD")
    return [
        "az", "webapp", "exec",
        "--resource-group", RESOURCE_GROUP,
        "--name", APP,
        "--mode", "shell",
        "--target", "app",
        "--instance", str(instance),
        "--shell", SHELL,
        "--only-show-errors",
    ]


def _validate_stage(stage: bytes, expected_sha256: str) -> None:
    require(isinstance(stage, bytes), "STAGE_SHAPE_HOLD")
    require(0 < len(stage) <= MAX_STAGE_BYTES, "STAGE_SIZE_HOLD")
    require(re.fullmatch(r"[a-f0-9]{64}", str(expected_sha256)) is not None,
            "STAGE_HASH_HOLD")
    require(sha256(stage) == expected_sha256, "STAGE_HASH_HOLD")


def validate_stage_archive(stage: bytes) -> list[str]:
    """Reject duplicate, absolute, traversal, symlink, and oversized members."""

    try:
        with zipfile.ZipFile(io.BytesIO(stage)) as archive:
            infos = archive.infolist()
            names = [item.filename for item in infos]
            require(bool(names) and len(names) == len(set(names)), "STAGE_ARCHIVE_HOLD")
            for item in infos:
                path = PurePosixPath(item.filename)
                mode = (item.external_attr >> 16) & 0o170000
                require(not item.is_dir() and not path.is_absolute() and ".." not in path.parts,
                        "STAGE_PATH_HOLD")
                require(mode != 0o120000 and item.file_size <= MAX_STAGE_BYTES,
                        "STAGE_PATH_HOLD")
            return names
    except TransportHold:
        raise
    except Exception as exc:
        raise TransportHold("STAGE_ARCHIVE_HOLD") from exc


def iter_stage_frame(stage: bytes, expected_sha256: str) -> Iterator[bytes]:
    """Yield an ASCII-only exact frame suitable for the interactive PTY."""

    _validate_stage(stage, expected_sha256)
    validate_stage_archive(stage)
    encoded = base64.b64encode(stage)
    yield f"FV_KUDU_STAGE_V1 {len(stage)} {expected_sha256}\n".encode("ascii")
    for offset in range(0, len(encoded), FRAME_WIDTH):
        yield encoded[offset:offset + FRAME_WIDTH] + b"\n"
    yield b"FV_KUDU_STAGE_END\n"


def decode_stage_frame(frame: Iterable[bytes], expected_sha256: str,
                       expected_size: int) -> bytes:
    """Pure receiver used by tests and mirrored by the remote bootstrap."""

    require(isinstance(expected_size, int) and 0 < expected_size <= MAX_STAGE_BYTES,
            "STAGE_SIZE_HOLD")
    iterator = iter(frame)
    try:
        header = next(iterator)
    except StopIteration as exc:
        raise TransportHold("STAGE_SHAPE_HOLD") from exc
    expected_header = f"FV_KUDU_STAGE_V1 {expected_size} {expected_sha256}\n".encode("ascii")
    require(header == expected_header, "STAGE_SHAPE_HOLD")
    chunks: list[bytes] = []
    encoded_size = 0
    terminated = False
    for line in iterator:
        if line == b"FV_KUDU_STAGE_END\n":
            terminated = True
            break
        require(line.endswith(b"\n") and 1 < len(line) <= FRAME_WIDTH + 1,
                "STAGE_SHAPE_HOLD")
        chunk = line[:-1]
        require(re.fullmatch(rb"[A-Za-z0-9+/=]+", chunk) is not None,
                "STAGE_SHAPE_HOLD")
        chunks.append(chunk)
        encoded_size += len(chunk)
        require(encoded_size <= ((MAX_STAGE_BYTES + 2) // 3) * 4,
                "STAGE_SIZE_HOLD")
    require(terminated, "STAGE_SHAPE_HOLD")
    try:
        raw = base64.b64decode(b"".join(chunks), validate=True)
    except Exception as exc:
        raise TransportHold("STAGE_SHAPE_HOLD") from exc
    _validate_stage(raw, expected_sha256)
    require(len(raw) == expected_size, "STAGE_SIZE_HOLD")
    validate_stage_archive(raw)
    return raw


def remote_bootstrap_command(expected_sha256: str, expected_size: int) -> bytes:
    """Return a fixed, non-secret receiver command for interactive stdin.

    The generated receiver verifies the complete frame in memory before its
    first filesystem write, creates a distinct exclusive /tmp directory,
    performs safe ZIP extraction, and prints only a fixed readiness marker.
    """

    require(re.fullmatch(r"[a-f0-9]{64}", str(expected_sha256)) is not None,
            "STAGE_HASH_HOLD")
    require(isinstance(expected_size, int) and 0 < expected_size <= MAX_STAGE_BYTES,
            "STAGE_SIZE_HOLD")
    source = f'''import base64,hashlib,io,os,re,sys,zipfile\nfrom pathlib import Path,PurePosixPath\ndef req(value):\n if not value:raise SystemExit(90)\nroot=Path({REMOTE_STAGE!r})\nsize={expected_size}\nexpected={expected_sha256!r}\nmax_bytes={MAX_STAGE_BYTES}\nheader=sys.stdin.buffer.readline(256)\nwant=("FV_KUDU_STAGE_V1 %d %s\\n"%(size,expected)).encode("ascii")\nreq(header==want)\nparts=[];total=0\nwhile True:\n line=sys.stdin.buffer.readline({FRAME_WIDTH + 2})\n req(bool(line))\n if line==b"FV_KUDU_STAGE_END\\n":break\n req(line.endswith(b"\\n") and 1<len(line)<={FRAME_WIDTH + 1})\n chunk=line[:-1];req(re.fullmatch(rb"[A-Za-z0-9+/=]+",chunk) is not None)\n total+=len(chunk);req(total<=((max_bytes+2)//3)*4)\n parts.append(chunk)\nraw=base64.b64decode(b"".join(parts),validate=True)\nreq(len(raw)==size and hashlib.sha256(raw).hexdigest()==expected)\nwith zipfile.ZipFile(io.BytesIO(raw)) as z:\n infos=z.infolist();names=[i.filename for i in infos]\n req(bool(names) and len(names)==len(set(names)))\n for item in infos:\n  p=PurePosixPath(item.filename);mode=(item.external_attr>>16)&0o170000\n  req(not item.is_dir() and not p.is_absolute() and ".." not in p.parts and mode!=0o120000 and item.file_size<=max_bytes)\n req(not root.exists() and not root.is_symlink())\n old=os.umask(0o077)\n try:\n  root.mkdir(mode=0o700)\n  for item in infos:\n   target=root/PurePosixPath(item.filename);target.parent.mkdir(parents=True,exist_ok=True)\n   fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)\n   with os.fdopen(fd,"wb") as f:f.write(z.read(item.filename))\n finally:os.umask(old)\nprint("FV_KUDU_STAGE_READY",flush=True)\n'''
    encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
    command = (
        "python3 -c \"import base64;exec(compile(base64.b64decode('"
        + encoded + "'),'fv-kudu-stage','exec'))\"\n"
    )
    return command.encode("ascii")


def runner_command(artifact_sha256: str) -> bytes:
    """Legacy full-installer launch is deliberately disabled in this candidate."""

    require(re.fullmatch(r"[a-f0-9]{64}", str(artifact_sha256)) is not None,
            "STAGE_HASH_HOLD")
    raise TransportHold("PROTOCOL_STATE_HOLD")


class OnceOnlyState:
    """One invocation, no automatic retry, fail closed on unknown commit."""

    _ORDER = ("NEW", "CHANNEL", "STAGED", "TOKENS", "SQL_STARTED", "COMPLETE", "CLEANED")

    def __init__(self) -> None:
        self.state = "NEW"
        self.attempts = 0

    def begin(self) -> None:
        require(self.attempts == 0 and self.state == "NEW", "ONCE_ONLY_HOLD")
        self.attempts = 1
        self.state = "CHANNEL"

    def advance(self, expected: str, target: str) -> None:
        require(self.state == expected and target in self._ORDER,
                "PROTOCOL_STATE_HOLD")
        require(self._ORDER.index(target) == self._ORDER.index(expected) + 1,
                "PROTOCOL_STATE_HOLD")
        self.state = target

    def unknown(self) -> None:
        self.state = "UNKNOWN"

    @property
    def retry_allowed(self) -> bool:
        return self.attempts == 0 and self.state == "NEW"

    def require_known_complete(self) -> None:
        require(self.state in {"COMPLETE", "CLEANED"}, "UNKNOWN_COMMIT_HOLD")


class KuduExecSession:
    """Minimal PTY driver for an exact interactive ``az webapp exec`` process.

    The caller supplies validated tokens and fixed markers.  Raw terminal text
    is kept only in a bounded in-memory buffer and is never returned or logged.
    """

    def __init__(self, instance: str, *, deadline: float,
                 popen: Callable[..., subprocess.Popen] = subprocess.Popen,
                 clock: Callable[[], float] = time.monotonic) -> None:
        self.instance = instance
        self.deadline = deadline
        self._popen = popen
        self._clock = clock
        self.proc = None
        self.fd = None
        self.buffer = b""
        self._secrets: list[bytearray] = []
        self._token_count = 0
        self.audit: list[str] = []
        self.state = OnceOnlyState()

    def start(self) -> None:
        self.state.begin()
        master, slave = pty.openpty()
        attrs = termios.tcgetattr(slave)
        attrs[3] &= ~termios.ECHO
        termios.tcsetattr(slave, termios.TCSANOW, attrs)
        try:
            self.proc = self._popen(interactive_exec_args(self.instance), stdin=slave,
                                    stdout=slave, stderr=slave, start_new_session=True)
        except Exception as exc:
            os.close(master)
            raise TransportHold("KUDU_PROCESS_HOLD") from exc
        finally:
            os.close(slave)
        self.fd = master
        self.audit.append("KUDU_PROCESS_STARTED")

    def _check_secret_echo(self) -> None:
        for secret in self._secrets:
            if secret and bytes(secret) in self.buffer:
                self.buffer = b""
                raise TransportHold("KUDU_SECRET_ECHO")

    def _write_all(self, raw: bytes) -> None:
        require(self.fd is not None and isinstance(raw, bytes), "KUDU_WRITE_HOLD")
        offset = 0
        while offset < len(raw):
            remaining = self.deadline - self._clock()
            require(remaining > 0, "KUDU_CHANNEL_TIMEOUT")
            writable = select.select([], [self.fd], [], min(0.2, remaining))[1]
            if not writable:
                require(self.proc is not None and self.proc.poll() is None,
                        "KUDU_CHANNEL_CLOSED")
                continue
            try:
                count = os.write(self.fd, raw[offset:offset + 4096])
            except (BlockingIOError, InterruptedError):
                continue
            except Exception as exc:
                raise TransportHold("KUDU_WRITE_HOLD") from exc
            require(0 < count <= min(4096, len(raw) - offset), "KUDU_WRITE_HOLD")
            offset += count

    def send_public(self, raw: bytes, audit_code: str) -> None:
        require(re.fullmatch(r"[A-Z0-9_]{3,80}", audit_code) is not None,
                "PROTOCOL_STATE_HOLD")
        self._write_all(raw)
        self.audit.append(audit_code)

    def establish_safe_channel(self) -> None:
        """Disable remote echo before any secret can be supplied."""

        require(self.state.state == "CHANNEL", "PROTOCOL_STATE_HOLD")
        self.send_public(
            b"stty -echo; umask 077; printf 'FV_KUDU_CHANNEL_READY\\n'\n",
            "ECHO_DISABLE_SENT",
        )
        self.expect_fixed(b"FV_KUDU_CHANNEL_READY", 30, "KUDU_CHANNEL_READY")

    def stage_archive(self, stage: bytes, expected_sha256: str) -> None:
        """Send one hash-bound non-secret stage, then advance exactly once."""

        require(self.state.state == "CHANNEL", "PROTOCOL_STATE_HOLD")
        _validate_stage(stage, expected_sha256)
        validate_stage_archive(stage)
        self.send_public(remote_bootstrap_command(expected_sha256, len(stage)),
                         "STAGE_RECEIVER_SENT")
        for chunk in iter_stage_frame(stage, expected_sha256):
            self._write_all(chunk)
        self.audit.append("STAGE_FRAME_SENT")
        self.expect_fixed(b"FV_KUDU_STAGE_READY", 300, "STAGE_READY")
        self.state.advance("CHANNEL", "STAGED")

    def send_secret_line(self, token: str, *, validator: Callable[[str], object],
                         audit_code: str) -> None:
        require(self.state.state == "STAGED", "PROTOCOL_STATE_HOLD")
        expected_codes = ("SQL_TOKEN_SENT", "STORAGE_TOKEN_SENT")
        require(self._token_count < len(expected_codes) and
                audit_code == expected_codes[self._token_count],
                "TOKEN_HANDOFF_HOLD")
        require(isinstance(token, str) and token.isascii() and
                re.fullmatch(r"[A-Za-z0-9._-]{50,16384}", token) is not None,
                "TOKEN_HANDOFF_HOLD")
        try:
            require(validator(token) is True, "TOKEN_HANDOFF_HOLD")
        except Exception as exc:
            raise TransportHold("TOKEN_HANDOFF_HOLD") from exc
        secret = bytearray(token.encode("ascii"))
        self._secrets.append(secret)
        try:
            self._write_all(bytes(secret) + b"\n")
        except Exception:
            self.state.unknown()
            raise
        self._token_count += 1
        self.audit.append(audit_code)
        if self._token_count == 2:
            self.state.advance("STAGED", "TOKENS")

    def expect_fixed(self, marker: bytes, seconds: float, audit_code: str) -> None:
        require(marker in {
            b"FV_KUDU_CHANNEL_READY", b"FV_KUDU_STAGE_READY",
            b"FV_P3_TOKENS_READY", b"FV_P3_FINAL_RESULT=",
            b"FV_INDEPENDENT_CLEANUP=PASS",
        }, "PROTOCOL_STATE_HOLD")
        end = min(self.deadline, self._clock() + seconds)
        while self._clock() < end:
            self._check_secret_echo()
            if marker in self.buffer:
                self.buffer = self.buffer.split(marker, 1)[1]
                self.audit.append(audit_code)
                return
            readable = select.select([self.fd], [], [], 0.2)[0]
            if readable:
                try:
                    chunk = os.read(self.fd, 65536)
                except Exception as exc:
                    raise TransportHold("KUDU_CHANNEL_CLOSED") from exc
                require(bool(chunk), "KUDU_CHANNEL_CLOSED")
                self.buffer += chunk
                require(len(self.buffer) <= MAX_OUTPUT_BYTES, "KUDU_OUTPUT_LIMIT")
            elif self.proc is None or self.proc.poll() is not None:
                raise TransportHold("KUDU_CHANNEL_CLOSED")
        raise TransportHold("KUDU_CHANNEL_TIMEOUT")

    def clear_secrets(self) -> None:
        for secret in self._secrets:
            for index in range(len(secret)):
                secret[index] = 0
        self._secrets.clear()
        self.buffer = b""

    def mark_sql_started(self) -> None:
        self.state.advance("TOKENS", "SQL_STARTED")

    def mark_complete(self) -> None:
        self.state.advance("SQL_STARTED", "COMPLETE")

    def close(self) -> None:
        if self.state.state == "SQL_STARTED":
            self.state.unknown()
        self.clear_secrets()
        if self.proc is not None and self.proc.poll() is None:
            try:
                os.killpg(self.proc.pid, signal.SIGTERM)
                self.proc.wait(timeout=5)
            except Exception:
                try:
                    os.killpg(self.proc.pid, signal.SIGKILL)
                    self.proc.wait(timeout=5)
                except Exception:
                    pass
        if self.fd is not None:
            try:
                os.close(self.fd)
            except OSError:
                pass
        self.fd = None
        self.proc = None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY: NO LIVE KUDU/AZURE/SQL ENTRYPOINT")
