#!/usr/bin/env python3
"""Authorization-locked P3 NON-SQL App Service SSH transport.

This parent transports one already-built FIELDVISIT_P3_NONSQL_HANDOFF_V1
archive to the exact running App Service instance, runs only the pinned static
ABI probe, receives one nonce-bound receipt, and independently cleans its own
temporary directory.  It never requests a token, opens SQL, touches Blob
markers, runs the installer, or retries a failed/unknown operation.
"""
import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import pty
import re
import select
import signal
import socket
import stat
import subprocess
import tempfile
import termios
import time
import zipfile

RG = "rg-fieldvisit-uat"
APP = "api-fieldvisit-pathb-uat"
RELEASE = "d6c220877b9976b6478d85b5171d4e06293595ae"
OWNER_SHA = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"
TENANT_SHA = "976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c"
SUB_SHA = "7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47"
HOST_FP = "SHA256:qlMlSfV+nfx3Y5BdqQC+4WY1VFChCdRZMjqE+T/NgOI"
HANDOFF_CONTRACT = "FIELDVISIT_P3_NONSQL_HANDOFF_V1"
AUTH_DECISION = "OWNER_APPROVED_PATHB_P3_NONSQL_TRANSPORT_EXACT_SHA_ONCE"
RECEIPT_CONTRACT = "FIELDVISIT_P3_NONSQL_RECEIPT_V1"
MAX_HANDOFF_BYTES = 130 * 1024 * 1024
MAX_DURATION_SECONDS = 600
TRANSFER_CHUNK = 3000  # base64 line length is 4000, below a 4096-byte PTY line
REMOTE_PREFIX = "/tmp/fv-p3-nonsql-"
RECEIPT_PREFIX = b"FV_P3_NONSQL_RECEIPT="
SCRIPT_MEMBERS = ("p3_nonsql_stage_handler.py", "p3_nonsql_abi_probe.py")


class Hold(Exception):
    pass


def demand(ok):
    if not ok:
        raise Hold()


def sha256(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path):
    demand(path.is_file() and not path.is_symlink() and path.stat().st_nlink == 1)


def canonical_json(obj):
    return (json.dumps(obj, sort_keys=True, separators=(",", ":")) + "\n").encode()


def safe_json(path, max_bytes=65536):
    regular(path)
    demand(path.stat().st_size <= max_bytes)
    data = json.loads(path.read_text(encoding="utf-8"))
    demand(isinstance(data, dict))
    return data


def git_blob_sha1(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()


def inspect_handoff(raw, binding):
    demand(isinstance(raw, bytes) and 0 < len(raw) <= MAX_HANDOFF_BYTES)
    expected = {"manifest.json", "stage.zip", *SCRIPT_MEMBERS}
    with zipfile.ZipFile(io.BytesIO(raw), "r") as archive:
        items = archive.infolist()
        demand(len(items) == 4 and {x.filename for x in items} == expected)
        demand(len({x.filename for x in items}) == len(items))
        records = {}
        for item in items:
            mode = item.external_attr >> 16
            demand(item.filename in expected and item.file_size <= MAX_HANDOFF_BYTES)
            demand(item.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED))
            demand(not (item.flag_bits & 1) and not stat.S_ISLNK(mode) and not stat.S_ISDIR(mode))
            records[item.filename] = archive.read(item)
    manifest = json.loads(records["manifest.json"])
    demand(manifest.get("contract") == HANDOFF_CONTRACT)
    demand(manifest.get("live_execution_authorized") is False)
    demand(manifest.get("remote_entrypoint") == "p3_nonsql_stage_handler.py")
    demand(manifest.get("remote_sql_token_ddl") == "FORBIDDEN")
    demand(manifest.get("stage_sha256") == sha256(records["stage.zip"]))
    scripts = manifest.get("scripts")
    demand(isinstance(scripts, dict) and set(scripts) == set(SCRIPT_MEMBERS))
    for name in SCRIPT_MEMBERS:
        rule = binding["files"][name]
        demand(scripts[name].get("sha256") == rule["sha256"])
        demand(scripts[name].get("git_blob_sha1") == rule["git_blob_sha1"])
        demand(sha256(records[name]) == rule["sha256"])
        demand(git_blob_sha1(records[name]) == rule["git_blob_sha1"])
    return {"manifest": manifest, "stage_sha256": manifest["stage_sha256"]}


def validate_binding(binding):
    demand(binding.get("contract") == "FIELDVISIT_P3_NONSQL_TRANSPORT_BINDING_V1")
    demand(binding.get("approved_release") == RELEASE)
    demand(binding.get("resource_group") == RG and binding.get("app") == APP)
    demand(binding.get("max_duration_seconds") == MAX_DURATION_SECONDS)
    demand(binding.get("max_live_transport_attempts") == 1)
    demand(binding.get("max_ssh_sessions") == 2)
    demand(binding.get("sql_token_select_ddl_blob_marker") == "FORBIDDEN")
    demand(binding.get("transport_file") == "p3_nonsql_ssh_transport.py")
    demand(binding.get("authorization_pins") == [
        "transport_sha256", "binding_sha256", "handoff_sha256", "instance_id_sha256"])
    demand(set(binding.get("files", {})) == set(SCRIPT_MEMBERS))
    for value in binding["files"].values():
        demand(re.fullmatch(r"[0-9a-f]{64}", value.get("sha256", "")) is not None)
        demand(re.fullmatch(r"[0-9a-f]{40}", value.get("git_blob_sha1", "")) is not None)


def validate_authorization(auth, transport_path, binding_path, handoff_raw, instance_id, now):
    demand(auth.get("decision") == AUTH_DECISION)
    demand(auth.get("approved_release") == RELEASE)
    demand(auth.get("resource_group") == RG and auth.get("app") == APP)
    demand(re.fullmatch(r"[0-9a-f]{32}", auth.get("operation_id", "")) is not None)
    demand(auth.get("max_duration_seconds") == MAX_DURATION_SECONDS)
    demand(auth.get("max_live_transport_attempts") == 1)
    expires = auth.get("expires_at_epoch")
    demand(isinstance(expires, int) and now < expires <= now + 3600)
    demand(auth.get("transport_sha256") == sha256(transport_path.read_bytes()))
    demand(auth.get("binding_sha256") == sha256(binding_path.read_bytes()))
    demand(auth.get("handoff_sha256") == sha256(handoff_raw))
    demand(auth.get("instance_id_sha256") == sha256(instance_id.lower().encode()))


def run(argv, timeout=60, json_output=False):
    cp = subprocess.run(argv, capture_output=True, timeout=timeout, check=False)
    demand(cp.returncode == 0)
    if json_output:
        value = json.loads(cp.stdout)
        demand(value is not None)
        return value
    return cp.stdout


def az(args, timeout=60):
    return run(["az", *args, "-o", "json", "--only-show-errors"], timeout, True)


def management_preflight(subscription, expected_instance_id):
    account = az(["account", "show"])
    demand(sha256(str(account.get("id", "")).lower().encode()) == SUB_SHA)
    demand(sha256(str(account.get("tenantId", "")).lower().encode()) == TENANT_SHA)
    demand(str(account.get("user", {}).get("type", "")).lower() == "user")
    user = az(["ad", "signed-in-user", "show"])
    demand(sha256(str(user.get("id", "")).lower().encode()) == OWNER_SHA)
    app = az(["webapp", "show", "--subscription", subscription, "-g", RG, "-n", APP])
    demand(app.get("name") == APP and app.get("state") == "Running")
    instances = az(["webapp", "list-instances", "--subscription", subscription, "-g", RG, "-n", APP])
    demand(isinstance(instances, list) and len(instances) == 1)
    actual = str(instances[0].get("id", ""))
    name = str(instances[0].get("name", ""))
    demand(actual.lower() == expected_instance_id.lower())
    demand(re.fullmatch(r"[A-Za-z0-9._-]{8,256}", name) is not None)
    help_text = run(["az", "webapp", "create-remote-connection", "-h"], 30)
    demand(all(x in help_text for x in (b"--instance", b"--port", b"--timeout")))
    return name


class Tunnel:
    """One official Azure tunnel; credentials stay in memory and are never logged."""
    def __init__(self, subscription, instance, port):
        self.subscription, self.instance, self.port = subscription, instance, port
        self.proc = None
        self.buffers = []
        self.password = None

    def __enter__(self):
        argv = ["az", "webapp", "create-remote-connection", "--subscription", self.subscription,
                "-g", RG, "-n", APP, "--instance", self.instance, "--port", str(self.port),
                "--timeout", "60"]
        self.proc = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     start_new_session=True)
        for stream in (self.proc.stdout, self.proc.stderr):
            os.set_blocking(stream.fileno(), False)
        deadline = time.monotonic() + 60
        combined = b""
        while time.monotonic() < deadline:
            demand(self.proc.poll() is None)
            ready = select.select([self.proc.stdout, self.proc.stderr], [], [], .2)[0]
            for stream in ready:
                chunk = stream.read(8192) or b""
                combined = (combined + chunk)[-32768:]
            match = re.search(rb"username:\s*root\s*,\s*password:\s*([^\s}\r\n]{1,128})", combined, re.I)
            if match:
                self.password = bytearray(match.group(1))
            try:
                with socket.create_connection(("127.0.0.1", self.port), timeout=.2):
                    if self.password:
                        combined = b""
                        return self
            except OSError:
                pass
        raise Hold()

    def __exit__(self, *_):
        if self.password:
            for i in range(len(self.password)):
                self.password[i] = 0
            self.password = None
        if self.proc and self.proc.poll() is None:
            os.killpg(self.proc.pid, signal.SIGTERM)
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(self.proc.pid, signal.SIGKILL)
                self.proc.wait()


def known_host(port, path):
    raw = run(["ssh-keyscan", "-p", str(port), "-t", "ed25519", "127.0.0.1"], 20)
    demand(bool(raw))
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "wb") as out:
        out.write(raw)
    fingerprint = run(["ssh-keygen", "-lf", str(path)], 20).decode("ascii", "replace")
    demand(HOST_FP in fingerprint.split())


def ssh_argv(port, known_hosts):
    return ["ssh", "-tt", "-p", str(port), "-m", "hmac-sha1",
            "-o", "PreferredAuthentications=password", "-o", "PubkeyAuthentication=no",
            "-o", "NumberOfPasswordPrompts=1", "-o", "ConnectTimeout=15",
            "-o", "ServerAliveInterval=10", "-o", "ServerAliveCountMax=2",
            "-o", "HostKeyAlgorithms=ssh-ed25519", "-o", "StrictHostKeyChecking=yes",
            "-o", "UserKnownHostsFile=" + str(known_hosts), "root@127.0.0.1"]


class SSH:
    def __init__(self, argv, password, deadline):
        self.deadline, self.buf = deadline, b""
        self.fd, slave = pty.openpty()
        attrs = termios.tcgetattr(slave)
        attrs[3] &= ~termios.ECHO
        termios.tcsetattr(slave, termios.TCSANOW, attrs)
        self.proc = subprocess.Popen(argv, stdin=slave, stdout=slave, stderr=slave,
                                     start_new_session=True)
        os.close(slave)
        self.password = bytes(password)

    def send(self, data):
        demand(isinstance(data, bytes))
        offset = 0
        while offset < len(data):
            remaining = self.deadline - time.monotonic()
            demand(remaining > 0 and self.proc.poll() is None)
            if not select.select([], [self.fd], [], min(.2, remaining))[1]:
                continue
            try:
                count = os.write(self.fd, data[offset:offset + 2048])
            except (InterruptedError, BlockingIOError):
                continue
            demand(0 < count <= min(2048, len(data) - offset))
            offset += count

    def expect(self, pattern, seconds):
        rx = re.compile(pattern)
        end = min(self.deadline, time.monotonic() + seconds)
        while time.monotonic() < end:
            demand(self.password not in self.buf)
            match = rx.search(self.buf)
            if match:
                found = match.group(0)
                self.buf = self.buf[match.end():]
                return found
            if select.select([self.fd], [], [], .2)[0]:
                chunk = os.read(self.fd, 65536)
                demand(bool(chunk))
                self.buf = (self.buf + chunk)[-1048576:]
            else:
                demand(self.proc.poll() is None)
        raise Hold()

    def login(self, operation_id):
        self.expect(rb"(?i)password:", 30)
        self.send(self.password + b"\n")
        self.expect(rb"(?m)^root@[^\r\n]*#[ ]*", 30)
        marker = ("FV_NONSQL_ECHO_OFF=" + operation_id).encode()
        # First wait for the shell to complete stty: matching a marker in an
        # echoed compound command could otherwise falsely prove echo was off.
        self.send(b"stty -echo\n")
        self.expect(rb"(?m)^root@[^\r\n]*#[ ]*", 10)
        self.send(b"printf '" + marker + b"\\n'\n")
        self.expect(rb"(?m)^" + re.escape(marker) + rb"\r?$", 10)
        self.password = b"\0" * len(self.password)

    def close(self):
        try:
            self.send(b"\x03\nexit\n")
        except Exception:
            pass
        try:
            self.proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            os.killpg(self.proc.pid, signal.SIGKILL)
            self.proc.wait()
        os.close(self.fd)
        self.buf = b""
        self.password = b""


REMOTE_BOOTSTRAP = r'''import base64,hashlib,io,json,os,re,shutil,stat,subprocess,sys,zipfile
from pathlib import Path
PFX="FV_P3_NONSQL_RECEIPT="
MAX=136314880
root=Path(sys.argv[1]);expected=sys.argv[2];op=sys.argv[3];instance_hash=sys.argv[4]
receipt={"contract":"FIELDVISIT_P3_NONSQL_RECEIPT_V1","operation_id":op,"instance_id_sha256":instance_hash,"handoff_sha256":expected,"status":"HOLD","cleanup":"HOLD","sql_token_select_ddl_blob_marker":"NOT_RUN"}
def need(v):
 if not v:raise RuntimeError()
try:
 need(re.fullmatch(r"/tmp/fv-p3-nonsql-[0-9a-f]{32}",str(root)) is not None and not root.exists() and not root.is_symlink())
 old=os.umask(0o077);root.mkdir(mode=0o700);os.umask(old)
 st=root.stat();need(st.st_uid==os.geteuid() and stat.S_IMODE(st.st_mode)==0o700)
 target=root/"handoff.zip";fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
 total=0;digest=hashlib.sha256()
 with os.fdopen(fd,"wb") as out:
  header=sys.stdin.buffer.readline(512).decode("ascii").strip().split()
  need(len(header)==3 and header[0]=="FV_PAYLOAD" and header[2]==op);declared=int(header[1]);need(0<declared<=MAX)
  while True:
   line=sys.stdin.buffer.readline(8192);need(bool(line))
   if line.strip()==b"FV_PAYLOAD_END":break
   need(len(line)<=4097);part=base64.b64decode(line.strip(),validate=True);total+=len(part);need(total<=declared)
   digest.update(part);out.write(part)
 need(total==declared and digest.hexdigest()==expected)
 with zipfile.ZipFile(target) as z:
  names=z.namelist();need(len(names)==4 and len(set(names))==4 and set(names)=={"manifest.json","stage.zip","p3_nonsql_stage_handler.py","p3_nonsql_abi_probe.py"})
  m=json.loads(z.read("manifest.json"));need(m["contract"]=="FIELDVISIT_P3_NONSQL_HANDOFF_V1" and m["live_execution_authorized"] is False and m["remote_sql_token_ddl"]=="FORBIDDEN")
  need(hashlib.sha256(z.read("stage.zip")).hexdigest()==m["stage_sha256"])
  for n in ("stage.zip","p3_nonsql_stage_handler.py","p3_nonsql_abi_probe.py"):
   p=root/n;fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
   with os.fdopen(fd,"wb") as f:f.write(z.read(n))
  receipt["stage_sha256"]=m["stage_sha256"]
 cp=subprocess.run([sys.executable,str(root/"p3_nonsql_stage_handler.py"),"--stage",str(root/"stage.zip"),"--expected-sha256",m["stage_sha256"]],cwd=root,capture_output=True,timeout=240,check=False)
 need(cp.returncode==0 and cp.stdout==b"P3_NONSQL_STAGE=PASS_STATIC_ONLY\nSSH_TOKEN_SQL_MARKER=NOT_RUN\n" and cp.stderr==b"")
 receipt["status"]="PASS_STATIC_ONLY"
except Exception:
 pass
finally:
 try:
  if root.exists() and not root.is_symlink():shutil.rmtree(root)
  receipt["cleanup"]="PASS" if not root.exists() else "HOLD"
 except Exception:pass
print(PFX+json.dumps(receipt,sort_keys=True,separators=(",",":")),flush=True)
'''


REMOTE_CLEANUP = r'''import json,os,re,shutil,stat,sys
from pathlib import Path
root=Path(sys.argv[1]);op=sys.argv[2];status="HOLD"
try:
 if re.fullmatch(r"/tmp/fv-p3-nonsql-[0-9a-f]{32}",str(root)) is None:raise RuntimeError()
 if root.exists():
  if root.is_symlink() or not root.is_dir():raise RuntimeError()
  st=root.stat()
  if st.st_uid!=os.geteuid() or stat.S_IMODE(st.st_mode)!=0o700:raise RuntimeError()
  shutil.rmtree(root)
 status="PASS" if not root.exists() else "HOLD"
except Exception:pass
print("FV_P3_NONSQL_CLEANUP="+json.dumps({"operation_id":op,"status":status},sort_keys=True,separators=(",",":")),flush=True)
'''


def python_command(source, args):
    encoded = base64.b64encode(source.encode()).decode("ascii")
    demand(len(encoded) < 32768)
    quoted = " ".join("'" + x + "'" for x in args)
    return ("python3 -c \"import base64;exec(base64.b64decode('" + encoded + "'))\" " + quoted + "\n").encode()


def send_handoff(session, raw, operation_id):
    session.send((f"FV_PAYLOAD {len(raw)} {operation_id}\n").encode())
    for offset in range(0, len(raw), TRANSFER_CHUNK):
        session.send(base64.b64encode(raw[offset:offset + TRANSFER_CHUNK]) + b"\n")
    session.send(b"FV_PAYLOAD_END\n")


def parse_receipt(line, auth, handoff_sha, stage_sha):
    demand(line.startswith(RECEIPT_PREFIX))
    receipt = json.loads(line.split(b"=", 1)[1])
    expected = {
        "contract": RECEIPT_CONTRACT,
        "operation_id": auth["operation_id"],
        "instance_id_sha256": auth["instance_id_sha256"],
        "handoff_sha256": handoff_sha,
        "stage_sha256": stage_sha,
        "status": "PASS_STATIC_ONLY",
        "cleanup": "PASS",
        "sql_token_select_ddl_blob_marker": "NOT_RUN",
    }
    demand(receipt == expected)
    return receipt


def consume_journal(path, operation_id, transport_sha, handoff_sha):
    demand(path.is_absolute() and path.parent.is_dir() and not path.exists() and not path.is_symlink())
    payload = canonical_json({"contract": "FIELDVISIT_P3_NONSQL_LOCAL_ONCE_V1",
                              "operation_id": operation_id, "status": "ATTEMPTED",
                              "transport_sha256": transport_sha, "handoff_sha256": handoff_sha})
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "wb") as out:
        out.write(payload)


def update_journal(path, status):
    data = safe_json(path)
    data["status"] = status
    temp = path.with_name(path.name + ".new")
    demand(not temp.exists() and not temp.is_symlink())
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "wb") as out:
        out.write(canonical_json(data))
    os.replace(temp, path)


def execute(args):
    started = time.time()
    transport_path = Path(__file__).resolve()
    binding_path, handoff_path, auth_path, journal = map(Path,
        (args.binding, args.handoff, args.authorization, args.journal))
    for item in (transport_path, binding_path, handoff_path, auth_path):
        regular(item)
    binding = safe_json(binding_path)
    validate_binding(binding)
    handoff_raw = handoff_path.read_bytes()
    inspected = inspect_handoff(handoff_raw, binding)
    auth = safe_json(auth_path)
    validate_authorization(auth, transport_path, binding_path, handoff_raw,
                           args.instance_id, int(started))
    demand(sha256(args.subscription.lower().encode()) == SUB_SHA)
    instance_name = management_preflight(args.subscription, args.instance_id)
    transport_sha, handoff_sha = sha256(transport_path.read_bytes()), sha256(handoff_raw)
    consume_journal(journal, auth["operation_id"], transport_sha, handoff_sha)
    deadline = time.monotonic() + MAX_DURATION_SECONDS
    execution_deadline = deadline - 90  # reserve an independent cleanup window
    root = REMOTE_PREFIX + auth["operation_id"]
    receipt = None
    with tempfile.TemporaryDirectory(prefix="fv-p3-nonsql-parent-") as td:
        known = Path(td) / "known_hosts"
        try:
            with Tunnel(args.subscription, instance_name, args.local_port) as tunnel:
                known_host(args.local_port, known)
                session = SSH(ssh_argv(args.local_port, known), tunnel.password, execution_deadline)
                try:
                    session.login(auth["operation_id"])
                    session.send(python_command(REMOTE_BOOTSTRAP, [root, handoff_sha,
                                 auth["operation_id"], auth["instance_id_sha256"]]))
                    send_handoff(session, handoff_raw, auth["operation_id"])
                    line = session.expect(rb"FV_P3_NONSQL_RECEIPT=\{[^\r\n]+\}", 420)
                    receipt = parse_receipt(line, auth, handoff_sha, inspected["stage_sha256"])
                finally:
                    session.close()
        finally:
            # A second, independent session verifies/deletes only this operation's
            # exact 0700 root.  It is cleanup, never an operation replay.
            with Tunnel(args.subscription, instance_name, args.local_port) as tunnel:
                known.unlink(missing_ok=True)
                known_host(args.local_port, known)
                clean = SSH(ssh_argv(args.local_port, known), tunnel.password, deadline)
                try:
                    clean.login(auth["operation_id"])
                    clean.send(python_command(REMOTE_CLEANUP, [root, auth["operation_id"]]))
                    line = clean.expect(rb"FV_P3_NONSQL_CLEANUP=\{[^\r\n]+\}", 60)
                    result = json.loads(line.split(b"=", 1)[1])
                    demand(result == {"operation_id": auth["operation_id"], "status": "PASS"})
                finally:
                    clean.close()
    demand(receipt is not None and time.time() - started <= MAX_DURATION_SECONDS)
    update_journal(journal, "PASS_STATIC_ONLY")
    print("P3_NONSQL_TRANSPORT=PASS_STATIC_ONLY")
    print("INDEPENDENT_CLEANUP=PASS")
    print("TOKEN_SQL_SELECT_DDL_BLOB_MARKER=NOT_RUN")
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="Authorization-locked P3 NON-SQL SSH transport")
    ap.add_argument("--execute", action="store_true")
    ap.add_argument("--binding")
    ap.add_argument("--handoff")
    ap.add_argument("--authorization")
    ap.add_argument("--journal")
    ap.add_argument("--subscription")
    ap.add_argument("--instance-id")
    ap.add_argument("--local-port", type=int, default=51329)
    args = ap.parse_args(argv)
    if not args.execute:
        print("AUTHORIZATION_LOCKED_NONDEPLOY")
        return 0
    try:
        demand(all((args.binding, args.handoff, args.authorization, args.journal,
                    args.subscription, args.instance_id)))
        demand(1024 <= args.local_port <= 65535)
        return execute(args)
    except Exception:
        print("P3_NONSQL_TRANSPORT=HOLD")
        print("TOKEN_SQL_SELECT_DDL_BLOB_MARKER=NOT_RUN")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
