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
import secrets
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
SHELL = "/bin/sh"
TRANSPORT_CONTRACT = "FIELDVISIT_PATHB_P3_KUDU_STDIN_TRANSPORT_V1"
REMOTE_STAGE = "/tmp/fv-pathb-g1-original-installer-20261110-stage"
DIAGNOSTIC_REMOTE_STAGE = "/tmp/fv-pathb-kudu-channel-diagnostic-v1-20261105"
# Only pre-existing, source-scoped paths are observed; no /tmp globbing/deletion.
REMOTE_RESIDUE_PATHS = (
    REMOTE_STAGE,
    "/tmp/fv-pathb-g1-original-installer-20261110-work",
    "/dev/shm/fv-pathb-g1-original-installer-20261110",
    DIAGNOSTIC_REMOTE_STAGE,
    "/tmp/fv-pathb-p3-kudu-install-d6c2208-v1",
    "/tmp/fv-pathb-p3-kudu-install-d6c2208-v1-work",
    "/dev/shm/fv-pathb-p3-kudu-install-d6c2208-v1",
)
MAX_STAGE_BYTES = 64 * 1024 * 1024
MAX_OUTPUT_BYTES = 2 * 1024 * 1024
FRAME_WIDTH = 76
STAGE_BATCH_LINES = 1024  # bound public-stage output before next receipt

SAFE_CODES = frozenset({
    "AZ_CLI_VERSION_HOLD",
    "INSTANCE_LIST_HOLD",
    "INSTANCE_NOT_EXACTLY_ONE",
    "INSTANCE_ID_HOLD",
    "KUDU_PROCESS_HOLD",
    "SSH_OWNER_CONTEXT_HOLD",
    "SSH_TUNNEL_EXIT_HOLD",
    "SSH_TUNNEL_TIMEOUT_HOLD",
    "SSH_TUNNEL_SETUP_HOLD",
    "SSH_HOST_KEY_NOT_OBSERVED_HOLD",
    "SSH_HOST_KEY_MISMATCH_HOLD",
    "SSH_HOST_KEY_AMBIGUOUS_HOLD",
    "SSH_SESSION_SPAWN_HOLD",
    "KUDU_CHANNEL_TIMEOUT",
    "KUDU_CHANNEL_CLOSED",
    "KUDU_OUTPUT_LIMIT",
    "KUDU_SECRET_ECHO",
    "KUDU_WRITE_HOLD",
    "REMOTE_RESIDUE_HOLD",
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


def consume_exact_marker_line(buffer: bytes, marker: bytes, *, after: int = 0
                              ) -> tuple[bool, bytes]:
    """Consume only a complete independent marker line after a boundary."""

    require(isinstance(buffer, bytes) and isinstance(marker, bytes),
            "PROTOCOL_STATE_HOLD")
    require(bool(marker) and b"\n" not in marker and b"\r" not in marker,
            "PROTOCOL_STATE_HOLD")
    require(isinstance(after, int) and 0 <= after <= len(buffer),
            "PROTOCOL_STATE_HOLD")
    cursor = after
    if cursor and buffer[cursor - 1:cursor] not in {b"\n", b"\r"}:
        end = buffer.find(b"\n", cursor)
        if end < 0:
            return False, buffer
        cursor = end + 1
    while True:
        end = buffer.find(b"\n", cursor)
        if end < 0:
            return False, buffer
        line = buffer[cursor:end]
        if line.endswith(b"\r"):
            line = line[:-1]
        if line == marker:
            return True, buffer[end + 1:]
        cursor = end + 1


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


def remote_bootstrap_command(expected_sha256: str, expected_size: int, *,
                             remote_stage: str = REMOTE_STAGE) -> bytes:
    """Return a fixed, non-secret receiver command for interactive stdin.

    The generated receiver verifies the complete frame in memory before its
    first filesystem write, creates a distinct exclusive /tmp directory,
    performs safe ZIP extraction, and prints only a fixed readiness marker.
    """

    require(re.fullmatch(r"[a-f0-9]{64}", str(expected_sha256)) is not None,
            "STAGE_HASH_HOLD")
    require(isinstance(expected_size, int) and 0 < expected_size <= MAX_STAGE_BYTES,
            "STAGE_SIZE_HOLD")
    require(remote_stage in {REMOTE_STAGE, DIAGNOSTIC_REMOTE_STAGE},
            "STAGE_PATH_HOLD")
    source = f'''import base64,hashlib,io,os,re,sys,zipfile\nfrom pathlib import Path,PurePosixPath\ndef req(value):\n if not value:raise SystemExit(90)\nroot=Path({REMOTE_STAGE!r})\nsize={expected_size}\nexpected={expected_sha256!r}\nmax_bytes={MAX_STAGE_BYTES}\nheader=sys.stdin.buffer.readline(256)\nwant=("FV_KUDU_STAGE_V1 %d %s\\n"%(size,expected)).encode("ascii")\nreq(header==want)\nparts=[];total=0\nwhile True:\n line=sys.stdin.buffer.readline({FRAME_WIDTH + 2})\n req(bool(line))\n if line==b"FV_KUDU_STAGE_END\\n":break\n req(line.endswith(b"\\n") and 1<len(line)<={FRAME_WIDTH + 1})\n chunk=line[:-1];req(re.fullmatch(rb"[A-Za-z0-9+/=]+",chunk) is not None)\n total+=len(chunk);req(total<=((max_bytes+2)//3)*4)\n parts.append(chunk)\n if len(parts)%1024==0:print('\\nFV_KUDU_STAGE_CHUNK_READY',flush=True)\nraw=base64.b64decode(b"".join(parts),validate=True)\nreq(len(raw)==size and hashlib.sha256(raw).hexdigest()==expected)\nwith zipfile.ZipFile(io.BytesIO(raw)) as z:\n infos=z.infolist();names=[i.filename for i in infos]\n req(bool(names) and len(names)==len(set(names)))\n for item in infos:\n  p=PurePosixPath(item.filename);mode=(item.external_attr>>16)&0o170000\n  req(not item.is_dir() and not p.is_absolute() and ".." not in p.parts and mode!=0o120000 and item.file_size<=max_bytes)\n req(not root.exists() and not root.is_symlink())\n old=os.umask(0o077)\n try:\n  root.mkdir(mode=0o700)\n  for item in infos:\n   target=root/PurePosixPath(item.filename);target.parent.mkdir(parents=True,exist_ok=True)\n   fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)\n   with os.fdopen(fd,"wb") as f:f.write(z.read(item.filename))\n finally:os.umask(old)\nprint("\\nFV_KUDU_STAGE_READY",flush=True)\n'''
    source = source.replace(repr(REMOTE_STAGE), repr(remote_stage), 1)
    encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
    command = (
        "python3 -c \"import base64;exec(compile(base64.b64decode('"
        + encoded + "'),'fv-kudu-stage','exec'))\"\n"
    )
    return command.encode("ascii")


def runner_command(artifact_sha256: str) -> bytes:
    """Return the only permitted full-installer command for this stage.

    This merely constructs a command.  It does not authorize or execute it.
    The caller must separately bind the exact distribution, instance and
    short-lived Owner authorization before opening a live Kudu session.
    """

    require(re.fullmatch(r"[a-f0-9]{64}", str(artifact_sha256)) is not None,
            "STAGE_HASH_HOLD")
    return (
        "python3 " + REMOTE_STAGE +
        "/p3_app_installer_runner.py --execute --authorization " +
        REMOTE_STAGE + "/owner-authorization.json --artifact-sha256 " +
        artifact_sha256 + "\n"
    ).encode("ascii")


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


def remote_residue_probe_command(nonce: str) -> bytes:
    """Read-only, no Token, no stage/write, no cleanup: one closed-scoped probe.

    The remote emits a dynamic SHA receipt only after examining the existing
    filesystem/process namespace. The exact proof digest is absent from stdin.
    """
    require(re.fullmatch(r"[a-f0-9]{32}", nonce or "") is not None,
            "PROTOCOL_STATE_HOLD")
    source = r'''import hashlib,os
paths = (__PATHS__)
nonce = __NONCE__
def clean():
    # lexists detects normal files, directories and even broken symlinks.
    if any(os.path.lexists(path) for path in paths):
        return False
    needles = tuple(path.encode('utf-8') for path in paths) + (
        b'p3_app_installer_runner.py', b'token_adapter_g1_REVIEW_ONLY.py',
    )
    me = os.getpid()
    parent = os.getppid()  # the verified interactive shell running this probe
    try:
        pids = os.listdir('/proc')
        for pid in pids:
            if not pid.isdecimal() or int(pid) in (me, parent):
                continue
            try:
                with open('/proc/' + pid + '/cmdline','rb') as handle:
                    argv = handle.read(32769)
                # Truncated argv cannot establish process absence.
                if len(argv)>32768 or any(needle in argv for needle in needles):
                    return False
                # Old installer bootstrap may hide paths in Base64 argv.
                if b'base64.b64decode(' in argv and b'exec(compile(' in argv:
                    return False
                cwd = os.readlink('/proc/' + pid + '/cwd')
                if any(cwd==p or cwd.startswith(p + '/') for p in paths):
                    return False
            except (FileNotFoundError, ProcessLookupError):
                # A PID that exited before its metadata could be read is gone.
                continue
            except OSError:
                # Permission failures or unknown /proc failures are HOLD.
                return False
        # Recheck path absence after scanning processes, without changing it.
        return all(not os.path.lexists(path) for path in paths)
    except Exception:
        return False
status = 'CLEAR' if clean() else 'HOLD'
prefix = 'FV_G1_REMOTE_RESIDUE_' + status + ':'
proof = hashlib.sha256((prefix + nonce).encode('ascii')).hexdigest()
print('\n' + prefix + proof, flush=True)
'''
    source = source.replace('__PATHS__', repr(REMOTE_RESIDUE_PATHS))
    source = source.replace('__NONCE__', repr(nonce))
    encoded = base64.b64encode(source.encode('utf-8')).decode('ascii')
    return ("python3 -c \"import base64;exec(compile(base64.b64decode('" +
            encoded + "'),'fv-g1-residue-preflight','exec'))\"\n").encode('ascii')


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
        try:
            attrs = termios.tcgetattr(slave)
            # ECHO and canonical line buffering must both be disabled before
            # transferring up to 16 KiB Owner tokens through this PTY.
            attrs[3] &= ~(termios.ECHO | termios.ICANON)
            attrs[6][termios.VMIN] = 1
            attrs[6][termios.VTIME] = 0
            termios.tcsetattr(slave, termios.TCSANOW, attrs)
            self.proc = self._popen(interactive_exec_args(self.instance), stdin=slave,
                                    stdout=slave, stderr=slave, start_new_session=True)
        except Exception:
            # Covers termios setup as well as process creation failure.
            # Never expose underlying CLI/PTY exceptions or leave open FDs.
            os.close(master)
            raise TransportHold("KUDU_PROCESS_HOLD") from None
        finally:
            os.close(slave)
        self.fd = master
        self.audit.append("PROCESS_START")

    @classmethod
    def _consume_shell_challenge(cls, buffer: bytes, expected: bytes) -> tuple[bool, bytes]:
        """Consume an exact dynamic digest on a line, optionally after an actual prompt.

        Interactive PTYs commonly print the next command's result on the same
        line as the prior bare PS1 prompt (which has no newline). The original
        exact-line parser rejected that genuine result and timed out. The only
        allowed prefix is the same tightly constrained shell prompt checked
        during admission; arbitrary preceding text and trailing bytes are NOT
        accepted. No echo of the expected digest exists in the sent command.
        """
        require(isinstance(buffer, bytes) and isinstance(expected, bytes) and
                0 < len(expected) <= 256 and b"\n" not in expected,
                "PROTOCOL_STATE_HOLD")
        cursor = 0
        for raw in buffer.splitlines(keepends=True):
            if not raw.endswith(b"\n"):
                break
            line = raw[:-1].removesuffix(b"\r")
            if line == expected or (
                line.endswith(expected) and
                cls._remote_shell_prompt_present(line[:-len(expected)])
            ):
                return True, buffer[cursor + len(raw):]
            cursor += len(raw)
        return False, buffer

    def _challenge(self, *, tag: str, seconds: float, no_echo: bool = False,
                   disable_echo_first: bool = False) -> None:
        """Prove a command executed in this interactive shell, not just a CLI banner.

        The expected SHA-256 marker does not occur in stdin; an echoed command
        cannot forge it. No token or remote stage is sent during this phase.
        """
        require(self.state.state == "CHANNEL" and self.fd is not None and
                tag in {"SHELL", "ECHO"}, "PROTOCOL_STATE_HOLD")
        nonce = secrets.token_hex(16)
        expression = "FV_G1_KUDU_" + tag + ":" + nonce
        expected = ("FV_G1_KUDU_" + tag + ":" +
                    hashlib.sha256(expression.encode("ascii")).hexdigest()).encode("ascii")
        code = ('import hashlib;print("FV_G1_KUDU_' + tag + ':"+'
                'hashlib.sha256(b"' + expression + '").hexdigest())')
        command = ("python3 -c '" + code + "'\n").encode("ascii")
        if disable_echo_first:
            require(tag == "ECHO" and not no_echo, "PROTOCOL_STATE_HOLD")
            # One ordered remote shell command. The nonce/hash receipt proves
            # stty and umask completed before another challenge is sent.
            command = (b"stty -echo -icanon min 1 time 0 && umask 077 && " + command)
        # Do not take the presence of a startup banner as a security receipt.
        # The CLI is already bound to --target app and the exact unique instance.
        self.send_public(command, "KUDU_" + tag + "_CHALLENGE_SENT")
        end = min(self.deadline, self._clock() + seconds)
        while self._clock() < end:
            self._check_secret_echo()
            if no_echo and command.rstrip(b"\n") in self.buffer:
                raise TransportHold("KUDU_SECRET_ECHO")
            found, remaining = self._consume_shell_challenge(self.buffer, expected)
            if found:
                self.buffer = remaining
                self.audit.append("CLI_SHELL_BANNER_READY" if tag == "SHELL" else "ECHO_READY")
                return
            readable = select.select([self.fd], [], [], 0.2)[0]
            if readable:
                try:
                    chunk = os.read(self.fd, 65536)
                except Exception:
                    raise TransportHold("KUDU_CHANNEL_CLOSED") from None
                require(bool(chunk), "KUDU_CHANNEL_CLOSED")
                self.buffer += chunk
                require(len(self.buffer) <= MAX_OUTPUT_BYTES, "KUDU_OUTPUT_LIMIT")
            elif self.proc is None or self.proc.poll() is not None:
                raise TransportHold("KUDU_CHANNEL_CLOSED")
        raise TransportHold("KUDU_CHANNEL_TIMEOUT")

    @staticmethod
    def _remote_shell_prompt_present(buffer: bytes) -> bool:
        """Conservative prompt hint; never sufficient to authorize a command.

        Require a complete, trailing main-container shell prompt, not a CLI
        progress line, warning, password request or historical terminal text.
        A nonstandard/colored PS1 deliberately fails closed. The later
        unpredictable nonce/hash challenge is still mandatory.
        """
        if not isinstance(buffer, bytes):
            return False
        return re.search(
            rb"(?:^|[\r\n])(?:[A-Za-z][A-Za-z0-9._-]{0,63}@"
            rb"[A-Za-z0-9._-]{1,100}:[^\r\n\x00-\x1f]{0,128})?[#$] ?$",
            buffer[-2048:],
        ) is not None

    def await_cli_shell_banner(self, seconds: float = 120) -> None:
        """Wait for an actual shell prompt BEFORE the one in-band challenge.

        Starting the local 'az webapp exec' process does not mean the remote
        shell is ready. Commands written during startup may be silently lost.
        One bounded session, one challenge, no retries, no tokens or stage
        until both prompt hint and nonce/hash verification have succeeded.
        """
        require(self.state.state == "CHANNEL" and self.fd is not None,
                "PROTOCOL_STATE_HOLD")
        end = min(self.deadline, self._clock() + seconds)
        while self._clock() < end:
            if self._remote_shell_prompt_present(self.buffer):
                self.audit.append("SHELL_PROMPT_OBSERVED")
                self._challenge(tag="SHELL", seconds=max(0, end - self._clock()))
                return
            readable = select.select([self.fd], [], [], 0.2)[0]
            if readable:
                try:
                    chunk = os.read(self.fd, 65536)
                except Exception:
                    raise TransportHold("KUDU_CHANNEL_CLOSED") from None
                require(bool(chunk), "KUDU_CHANNEL_CLOSED")
                self.buffer += chunk
                require(len(self.buffer) <= MAX_OUTPUT_BYTES, "KUDU_OUTPUT_LIMIT")
            elif self.proc is None or self.proc.poll() is not None:
                raise TransportHold("KUDU_CHANNEL_CLOSED")
        raise TransportHold("KUDU_CHANNEL_TIMEOUT")

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

    def _write_stage_public(self, raw: bytes) -> None:
        """Full-duplex stage-only public write; no secret token is handled here."""
        require(self.fd is not None and isinstance(raw, bytes), "KUDU_WRITE_HOLD")
        offset = 0
        while offset < len(raw):
            remaining = self.deadline - self._clock()
            require(remaining > 0, "KUDU_CHANNEL_TIMEOUT")
            readable, writable, _ = select.select(
                [self.fd], [self.fd], [], min(0.2, remaining))
            if readable:
                try:
                    chunk = os.read(self.fd, 65536)
                except Exception:
                    raise TransportHold("KUDU_CHANNEL_CLOSED") from None
                require(bool(chunk), "KUDU_CHANNEL_CLOSED")
                self.buffer += chunk
                require(len(self.buffer) <= MAX_OUTPUT_BYTES, "KUDU_OUTPUT_LIMIT")
            if writable:
                try:
                    count = os.write(self.fd, raw[offset:offset + 4096])
                except (BlockingIOError, InterruptedError):
                    continue
                except Exception:
                    raise TransportHold("KUDU_WRITE_HOLD") from None
                require(0 < count <= min(4096, len(raw) - offset),
                        "KUDU_WRITE_HOLD")
                offset += count
            if self.proc is None or self.proc.poll() is not None:
                raise TransportHold("KUDU_CHANNEL_CLOSED")

    def send_public(self, raw: bytes, audit_code: str) -> None:
        require(re.fullmatch(r"[A-Z0-9_]{3,80}", audit_code) is not None,
                "PROTOCOL_STATE_HOLD")
        self._write_all(raw)
        self.audit.append(audit_code)

    def establish_safe_channel(self) -> None:
        """Disable terminal echo, then require an unechoed dynamic proof.

        Unlike a static marker, the expected SHA is absent from the input
        command. No token or archive may be transferred until this passes.
        """
        require(self.state.state == "CHANNEL", "PROTOCOL_STATE_HOLD")
        # Never pipeline a no-echo challenge behind an unacknowledged stty.
        # Kudu PTY latency can leave a subsequent command echoed even if the
        # earlier stty eventually succeeds. The first authenticated receipt
        # proves completion; the second independently proves echo is off.
        self._challenge(tag="ECHO", seconds=120, disable_echo_first=True)
        self._challenge(tag="ECHO", seconds=120, no_echo=True)

    def require_clean_remote(self, gate: str) -> None:
        """One safe read-only probe per gate, in the SAME authorized session.

        A missing, forged, stale, or HOLD receipt must stop before any Stage,
        SQL/Storage token handoff, Claim, or installer call. Never clean/retry.
        """
        require(gate in {"PRETOKEN", "PRESTAGE"} and
                self.state.state == "CHANNEL" and self.fd is not None and
                self._token_count == 0 and not self._secrets and
                self.audit.count("ECHO_READY") >= 2,
                "PROTOCOL_STATE_HOLD")
        require(("PRETOKEN_REMOTE_CLEAR" in self.audit) == (gate == "PRESTAGE"),
                "PROTOCOL_STATE_HOLD")
        nonce = secrets.token_hex(16)
        pass_prefix = "FV_G1_REMOTE_RESIDUE_CLEAR:"
        hold_prefix = "FV_G1_REMOTE_RESIDUE_HOLD:"
        passed = (pass_prefix + hashlib.sha256(
            (pass_prefix + nonce).encode("ascii")).hexdigest()).encode("ascii")
        held = (hold_prefix + hashlib.sha256(
            (hold_prefix + nonce).encode("ascii")).hexdigest()).encode("ascii")
        start = len(self.buffer)
        self.send_public(remote_residue_probe_command(nonce),
                         gate + "_REMOTE_PROBE_SENT")
        end = min(self.deadline, self._clock() + 60)
        while self._clock() < end:
            self._check_secret_echo()
            # Do not count a marker from any previous command or PTY buffer.
            current = self.buffer[start:]
            is_hold, _ = self._consume_shell_challenge(current, held)
            if is_hold:
                raise TransportHold("REMOTE_RESIDUE_HOLD")
            is_clean, rest = self._consume_shell_challenge(current, passed)
            if is_clean:
                self.buffer = rest
                self.audit.append(gate + "_REMOTE_CLEAR")
                return
            readable = select.select([self.fd], [], [], 0.2)[0]
            if readable:
                try:
                    raw = os.read(self.fd, 65536)
                except Exception:
                    raise TransportHold("KUDU_CHANNEL_CLOSED") from None
                require(bool(raw), "KUDU_CHANNEL_CLOSED")
                self.buffer += raw
                require(len(self.buffer) <= MAX_OUTPUT_BYTES, "KUDU_OUTPUT_LIMIT")
            elif self.proc is None or self.proc.poll() is not None:
                raise TransportHold("KUDU_CHANNEL_CLOSED")
        raise TransportHold("KUDU_CHANNEL_TIMEOUT")

    def stage_archive(self, stage: bytes, expected_sha256: str, *,
                      remote_stage: str = REMOTE_STAGE) -> None:
        """Send one hash-bound non-secret stage, then advance exactly once."""

        require(self.state.state == "CHANNEL", "PROTOCOL_STATE_HOLD")
        _validate_stage(stage, expected_sha256)
        validate_stage_archive(stage)
        boundary = len(self.buffer)
        self.send_public(remote_bootstrap_command(expected_sha256, len(stage),
                                                  remote_stage=remote_stage),
                         "STAGE_RECEIVER_SENT")
        frames = iter_stage_frame(stage, expected_sha256)
        self._write_stage_public(next(frames))
        batch = bytearray()
        count = 0
        for chunk in frames:
            if chunk == b"FV_KUDU_STAGE_END\n":
                if batch:
                    self._write_stage_public(bytes(batch))
                    batch.clear()
                self._write_stage_public(chunk)
                break
            batch.extend(chunk)
            count += 1
            if count % STAGE_BATCH_LINES == 0:
                checkpoint = len(self.buffer)
                self._write_stage_public(bytes(batch))
                batch.clear()
                self.expect_fixed(b"FV_KUDU_STAGE_CHUNK_READY", 600,
                                  "STAGE_CHUNK_READY", after=checkpoint)
        self.audit.append("STAGE_FRAME_SENT")
        self.expect_fixed(b"FV_KUDU_STAGE_READY", 600, "STAGE_READY",
                          after=0)
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

    def expect_fixed(self, marker: bytes, seconds: float, audit_code: str, *,
                     after: int | None = None) -> None:
        require(marker in {
            b"FV_KUDU_CHANNEL_READY", b"FV_KUDU_STAGE_READY",
            b"FV_P3_TOKENS_READY", b"FV_P3_SQL_TOKEN_RECEIVED",
            b"FV_KUDU_STAGE_CHUNK_READY",
            b"FV_P3_FINAL_RESULT=",
            b"FV_INDEPENDENT_CLEANUP=PASS",
            b"FV_KUDU_OWNED_CLEANUP=PASS",
            b"FV_KUDU_OWNED_CLEANUP_VERIFIED=PASS",
        }, "PROTOCOL_STATE_HOLD")
        if marker == b"FV_P3_SQL_TOKEN_RECEIVED":
            require(self.state.state == "STAGED" and self._token_count == 1 and
                    audit_code == "SQL_TOKEN_ACK", "PROTOCOL_STATE_HOLD")
        boundary = len(self.buffer) if after is None else after
        require(isinstance(boundary, int) and 0 <= boundary <= len(self.buffer),
                "PROTOCOL_STATE_HOLD")
        end = min(self.deadline, self._clock() + seconds)
        while self._clock() < end:
            self._check_secret_echo()
            found, remaining = consume_exact_marker_line(
                self.buffer, marker, after=boundary)
            if found:
                self.buffer = remaining
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
