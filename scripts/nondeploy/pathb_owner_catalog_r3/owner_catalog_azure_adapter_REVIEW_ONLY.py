#!/usr/bin/env python3
"""Authorization-locked Cloud Shell adapter for the Path B Owner catalog readback.

The implementation is complete but performs no Azure action unless --execute is
supplied together with a fresh, exact-hash authorization document and a new
local O_EXCL journal path.  It is deliberately limited to one Storage token,
one SQL token, one encrypted SQL connection, one fixed catalog SELECT, one
conditional Blob create, and at most two official App Service SSH tunnels.
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

from owner_catalog_integration_REVIEW_ONLY import Controller
from owner_catalog_parent_REVIEW_ONLY import (
    APP, CONTAINER, DATABASE, HOST_FP, MARKER, OWNER_SHA, RG, SQL_SERVER,
    STORAGE_ACCOUNT, SUB_SHA, TENANT_SHA, claim_request, commit_claim,
    exact_identity, extract_receipt, token_claims,
)

RELEASE = 'd6c220877b9976b6478d85b5171d4e06293595ae'
CONTRACT = 'FIELDVISIT_PATHB_OWNER_CATALOG_R4_DURABLE_V1'
BINDING_CONTRACT = 'FIELDVISIT_PATHB_OWNER_CATALOG_R4_DURABLE_BINDING_V1'
AUTH_DECISION = 'OWNER_APPROVED_PATHB_OWNER_CATALOG_R4_DURABLE_EXACT_SHA_ONCE'
MAX_DURATION = 600
MAX_CANDIDATE_BYTES = 2 * 1024 * 1024
REMOTE_PREFIX = '/tmp/fv-owner-catalog-r4-'
STORAGE_AUDIENCE = {'https://storage.azure.com/', 'https://storage.azure.com'}
SQL_AUDIENCE = {'https://database.windows.net/', 'https://database.windows.net'}
SAFE_TERMINALS = frozenset((
    'EMPTY_CATALOG_CANDIDATE', 'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED',
    'HOLD_SQL_LOGIN_OR_QUERY', 'HOLD_OPERATION', 'HOLD_PRECHECK',
    'HOLD_RESULT_SHAPE', 'HOLD_SQL_TARGET_OR_METADATA',
    'HOLD_PARTIAL_OR_INCONSISTENT', 'HOLD_RESULT_INCONSISTENT',
    'HOLD_DURABLE_ONCE_GUARD', 'HOLD_RECEIPT_PERSISTENCE',
))


class Hold(Exception):
    pass


def demand(value):
    if not value:
        raise Hold()


def sha256(raw):
    return hashlib.sha256(raw).hexdigest()


def canonical_json(value):
    return (json.dumps(value, sort_keys=True, separators=(',', ':')) + '\n').encode()


def regular(path):
    path = Path(path)
    st = path.lstat()
    demand(stat.S_ISREG(st.st_mode) and not path.is_symlink() and st.st_nlink == 1)


def safe_json(path, limit=131072):
    path = Path(path)
    regular(path)
    demand(path.stat().st_size <= limit)
    value = json.loads(path.read_text(encoding='utf-8'))
    demand(isinstance(value, dict))
    return value


def inspect_candidate(raw, binding):
    """Validate every candidate byte locally before an Azure command is run."""
    demand(isinstance(raw, bytes) and 0 < len(raw) <= MAX_CANDIDATE_BYTES)
    expected = {'manifest.json', 'owner_sql_catalog_child_REVIEW_ONLY.py',
                'pathb_catalog_readonly_REVIEW_ONLY.sql',
                'owner_catalog_durable_receipt_REVIEW_ONLY.py'}
    records = {}
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        items = archive.infolist()
        demand(len(items) == len(expected) and {x.filename for x in items} == expected)
        demand(len({x.filename for x in items}) == len(items))
        for item in items:
            mode = item.external_attr >> 16
            demand(item.file_size <= MAX_CANDIDATE_BYTES)
            demand(not (item.flag_bits & 1) and not stat.S_ISLNK(mode) and
                   not stat.S_ISDIR(mode))
            demand(item.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED))
            records[item.filename] = archive.read(item)
    manifest = json.loads(records['manifest.json'])
    demand(manifest.get('contract') == CONTRACT)
    demand(manifest.get('approved_release') == RELEASE)
    demand(manifest.get('resource_group') == RG and manifest.get('app') == APP)
    demand(manifest.get('sql_server') == SQL_SERVER and manifest.get('database') == DATABASE)
    demand(manifest.get('marker') == MARKER)
    demand(manifest.get('sql_calls_max') == 1 and manifest.get('ddl_dml') == 'FORBIDDEN')
    demand(manifest.get('storage_puts_max') == 1 and manifest.get('automatic_retry') is False)
    files = manifest.get('files')
    demand(isinstance(files, dict) and set(files) == expected - {'manifest.json'})
    demand(binding.get('files') == files)
    for name, expected_sha in files.items():
        demand(re.fullmatch(r'[0-9a-f]{64}', expected_sha or '') is not None)
        demand(sha256(records[name]) == expected_sha)
    demand(binding.get('candidate_sha256') == sha256(raw))
    return manifest


def validate_binding(binding, adapter_path, integration_path, parent_path):
    demand(binding.get('contract') == BINDING_CONTRACT)
    demand(binding.get('approved_release') == RELEASE)
    demand(binding.get('resource_group') == RG and binding.get('app') == APP)
    demand(binding.get('sql_server') == SQL_SERVER and binding.get('database') == DATABASE)
    demand(binding.get('marker') == MARKER)
    demand(binding.get('max_duration_seconds') == MAX_DURATION)
    demand(binding.get('max_ssh_sessions') == 2)
    demand(binding.get('max_storage_tokens') == 1 and binding.get('max_sql_tokens') == 1)
    demand(binding.get('max_storage_puts') == 1 and binding.get('max_sql_calls') == 1)
    demand(binding.get('automatic_retry') is False)
    actual = {
        'owner_catalog_azure_adapter_REVIEW_ONLY.py': sha256(Path(adapter_path).read_bytes()),
        'owner_catalog_integration_REVIEW_ONLY.py': sha256(Path(integration_path).read_bytes()),
        'owner_catalog_parent_REVIEW_ONLY.py': sha256(Path(parent_path).read_bytes()),
    }
    demand(binding.get('control_files') == actual)


def validate_authorization(auth, paths, candidate_raw, binding_raw, instance_id, now):
    demand(auth.get('decision') == AUTH_DECISION)
    demand(auth.get('approved_release') == RELEASE)
    demand(auth.get('resource_group') == RG and auth.get('app') == APP)
    demand(auth.get('sql_server') == SQL_SERVER and auth.get('database') == DATABASE)
    demand(auth.get('marker') == MARKER)
    demand(auth.get('ddl_dml') == 'FORBIDDEN')
    demand(auth.get('max_duration_seconds') == MAX_DURATION)
    demand(auth.get('max_ssh_sessions') == 2)
    demand(auth.get('max_storage_tokens') == 1 and auth.get('max_sql_tokens') == 1)
    demand(auth.get('max_storage_puts') == 1 and auth.get('max_sql_calls') == 1)
    demand(auth.get('automatic_retry') is False)
    demand(re.fullmatch(r'[0-9a-f]{32}', auth.get('operation_id', '')) is not None)
    expires = auth.get('expires_at_epoch')
    demand(isinstance(expires, int) and now < expires <= now + 1800)
    demand(auth.get('candidate_sha256') == sha256(candidate_raw))
    demand(auth.get('binding_sha256') == sha256(binding_raw))
    demand(auth.get('adapter_sha256') == sha256(Path(paths['adapter']).read_bytes()))
    demand(auth.get('instance_id_sha256') == sha256(instance_id.lower().encode()))


def run(argv, timeout=60, json_output=False):
    cp = subprocess.run(argv, capture_output=True, timeout=timeout, check=False)
    demand(cp.returncode == 0)
    if json_output:
        value = json.loads(cp.stdout)
        demand(value is not None)
        return value
    return cp.stdout


class AzureCLI:
    def __init__(self):
        self.storage_tokens = 0
        self.sql_tokens = 0

    def az_json(self, args, timeout=60):
        return run(['az', *args, '-o', 'json', '--only-show-errors'], timeout, True)

    def token(self, resource, kind):
        if kind == 'storage':
            demand(self.storage_tokens == 0)
            self.storage_tokens += 1
        elif kind == 'sql':
            demand(self.sql_tokens == 0)
            self.sql_tokens += 1
        else:
            raise Hold()
        # stdout is held only in this process memory and is never included in a
        # diagnostic, exception, receipt, argv, environment variable, or file.
        cp = subprocess.run(['az', 'account', 'get-access-token', '--resource', resource,
                             '--query', 'accessToken', '-o', 'tsv', '--only-show-errors'],
                            capture_output=True, timeout=60, check=False)
        demand(cp.returncode == 0)
        token = cp.stdout.decode('ascii').strip()
        demand(b'\n' not in cp.stdout.strip() and b'\r' not in cp.stdout.strip())
        return token


def management_preflight(cli, subscription, expected_instance_id):
    account = cli.az_json(['account', 'show'])
    user = cli.az_json(['ad', 'signed-in-user', 'show'])
    demand(exact_identity(account, user))
    demand(sha256(subscription.lower().encode()) == SUB_SHA)
    app = cli.az_json(['webapp', 'show', '--subscription', subscription,
                       '-g', RG, '-n', APP])
    demand(app.get('name') == APP and app.get('state') == 'Running')
    expected_app_id = ('/subscriptions/' + subscription + '/resourceGroups/' + RG +
                       '/providers/Microsoft.Web/sites/' + APP).lower()
    demand(str(app.get('id', '')).lower() == expected_app_id)
    instances = cli.az_json(['webapp', 'list-instances', '--subscription', subscription,
                             '-g', RG, '-n', APP])
    demand(isinstance(instances, list) and len(instances) == 1)
    actual_id = str(instances[0].get('id', ''))
    instance_name = str(instances[0].get('name', ''))
    demand(actual_id.lower() == expected_instance_id.lower())
    demand(re.fullmatch(r'[A-Za-z0-9._-]{8,256}', instance_name) is not None)
    help_text = run(['az', 'webapp', 'create-remote-connection', '-h'], 30)
    demand(all(flag in help_text for flag in (b'--instance', b'--port', b'--timeout')))
    return instance_name


class Tunnel:
    def __init__(self, subscription, instance, port):
        self.subscription, self.instance, self.port = subscription, instance, port
        self.proc = None
        self.password = None

    def __enter__(self):
        argv = ['az', 'webapp', 'create-remote-connection', '--subscription', self.subscription,
                '-g', RG, '-n', APP, '--instance', self.instance, '--port', str(self.port),
                '--timeout', '60']
        self.proc = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     start_new_session=True)
        for stream in (self.proc.stdout, self.proc.stderr):
            os.set_blocking(stream.fileno(), False)
        deadline = time.monotonic() + 60
        combined = b''
        while time.monotonic() < deadline:
            demand(self.proc.poll() is None)
            for stream in select.select([self.proc.stdout, self.proc.stderr], [], [], .2)[0]:
                combined = (combined + (stream.read(8192) or b''))[-32768:]
            match = re.search(rb'username:\s*root\s*,\s*password:\s*([^\s}\r\n]{1,128})',
                              combined, re.I)
            if match:
                self.password = bytearray(match.group(1))
            try:
                with socket.create_connection(('127.0.0.1', self.port), timeout=.2):
                    if self.password:
                        combined = b''
                        return self
            except OSError:
                pass
        raise Hold()

    def __exit__(self, *_):
        if self.password:
            for index in range(len(self.password)):
                self.password[index] = 0
            self.password = None
        if self.proc and self.proc.poll() is None:
            os.killpg(self.proc.pid, signal.SIGTERM)
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(self.proc.pid, signal.SIGKILL)
                self.proc.wait()


def known_host(port, path):
    raw = run(['ssh-keyscan', '-p', str(port), '-t', 'ed25519', '127.0.0.1'], 20)
    demand(bool(raw))
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'wb') as output:
        output.write(raw)
    fingerprint = run(['ssh-keygen', '-lf', str(path)], 20).decode('ascii', 'replace')
    demand(HOST_FP in fingerprint.split())


def ssh_argv(port, known_hosts):
    return ['ssh', '-tt', '-p', str(port), '-m', 'hmac-sha1',
            '-o', 'PreferredAuthentications=password', '-o', 'PubkeyAuthentication=no',
            '-o', 'NumberOfPasswordPrompts=1', '-o', 'ConnectTimeout=15',
            '-o', 'ServerAliveInterval=10', '-o', 'ServerAliveCountMax=2',
            '-o', 'HostKeyAlgorithms=ssh-ed25519', '-o', 'StrictHostKeyChecking=yes',
            '-o', 'UserKnownHostsFile=' + str(known_hosts), 'root@127.0.0.1']


class SSH:
    def __init__(self, argv, password, deadline):
        self.deadline, self.buf = deadline, b''
        self.fd, slave = pty.openpty()
        attributes = termios.tcgetattr(slave)
        attributes[3] &= ~termios.ECHO
        termios.tcsetattr(slave, termios.TCSANOW, attributes)
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
            demand(count > 0)
            offset += count

    def _read(self, seconds):
        end = min(self.deadline, time.monotonic() + seconds)
        while time.monotonic() < end:
            demand(self.password not in self.buf)
            if select.select([self.fd], [], [], .2)[0]:
                chunk = os.read(self.fd, 65536)
                demand(bool(chunk))
                self.buf = (self.buf + chunk)[-1048576:]
                return
            demand(self.proc.poll() is None)
        raise Hold()

    def expect_regex(self, pattern, seconds):
        rx = re.compile(pattern)
        end = min(self.deadline, time.monotonic() + seconds)
        while time.monotonic() < end:
            match = rx.search(self.buf)
            if match:
                found = match.group(0)
                self.buf = self.buf[match.end():]
                return found
            self._read(max(.2, end - time.monotonic()))
        raise Hold()

    def expect_line(self, marker, seconds):
        demand(isinstance(marker, bytes) and b'\n' not in marker and b'\r' not in marker)
        pattern = rb'(?m)(?<![^\r\n])' + re.escape(marker) + rb'\r?$'
        return self.expect_regex(pattern, seconds)

    def login(self, operation_id):
        self.expect_regex(rb'(?i)password:', 30)
        self.send(self.password + b'\n')
        self.expect_regex(rb'(?m)^root@[^\r\n]*#[ ]*', 30)
        marker = ('FV_OWNER_PARENT_ECHO_OFF=' + operation_id).encode()
        self.buf = b''
        self.send(b"stty -echo; printf '" + marker + b"\\n'\n")
        self.expect_line(marker, 10)
        self.password = b'\0' * len(self.password)

    def close(self):
        try:
            self.send(b'\x03\nexit\n')
        except Exception:
            pass
        try:
            self.proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            os.killpg(self.proc.pid, signal.SIGKILL)
            self.proc.wait()
        os.close(self.fd)
        self.buf = b''
        self.password = b''


REMOTE_STAGE = r'''import base64,hashlib,io,json,os,re,stat,sys,zipfile
from pathlib import Path
root=Path(sys.argv[1]);expected=sys.argv[2];op=sys.argv[3];limit=2097152
def need(v):
 if not v:raise RuntimeError()
need(re.fullmatch(r'/tmp/fv-owner-catalog-r4-[0-9a-f]{32}',str(root)) is not None)
need(not root.exists() and not root.is_symlink())
old=os.umask(0o077);root.mkdir(mode=0o700);os.umask(old)
need(root.stat().st_uid==os.geteuid() and stat.S_IMODE(root.stat().st_mode)==0o700)
target=root/'candidate.zip';fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
total=0;digest=hashlib.sha256()
with os.fdopen(fd,'wb') as out:
 header=sys.stdin.buffer.readline(512).decode('ascii').strip().split()
 need(len(header)==3 and header[0]=='FV_CATALOG_PAYLOAD' and header[2]==op)
 declared=int(header[1]);need(0<declared<=limit)
 while True:
  line=sys.stdin.buffer.readline(8192);need(bool(line))
  if line.strip()==b'FV_CATALOG_PAYLOAD_END':break
  need(len(line)<=4097);part=base64.b64decode(line.strip(),validate=True)
  total+=len(part);need(total<=declared);digest.update(part);out.write(part)
need(total==declared and digest.hexdigest()==expected)
with zipfile.ZipFile(target) as z:
 names=z.namelist();need(len(names)==4 and len(set(names))==4)
 need(set(names)=={'manifest.json','owner_sql_catalog_child_REVIEW_ONLY.py','pathb_catalog_readonly_REVIEW_ONLY.sql','owner_catalog_durable_receipt_REVIEW_ONLY.py'})
 manifest=json.loads(z.read('manifest.json'));need(manifest['contract']=='FIELDVISIT_PATHB_OWNER_CATALOG_R4_DURABLE_V1')
 for name in ('manifest.json','owner_sql_catalog_child_REVIEW_ONLY.py','pathb_catalog_readonly_REVIEW_ONLY.sql','owner_catalog_durable_receipt_REVIEW_ONLY.py'):
  raw=z.read(name);p=root/name;fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
  with os.fdopen(fd,'wb') as output:output.write(raw)
 for name,want in manifest['files'].items():need(hashlib.sha256((root/name).read_bytes()).hexdigest()==want)
print('FV_OWNER_STAGE_READY='+op,flush=True)
'''


REMOTE_CLEANUP = r'''import json,os,re,shutil,stat,sys
from pathlib import Path
root=Path(sys.argv[1]);op=sys.argv[2];status='HOLD'
allowed={'candidate.zip','manifest.json','owner_sql_catalog_child_REVIEW_ONLY.py','pathb_catalog_readonly_REVIEW_ONLY.sql','owner_catalog_durable_receipt_REVIEW_ONLY.py','__pycache__'}
try:
 if re.fullmatch(r'/tmp/fv-owner-catalog-r4-[0-9a-f]{32}',str(root)) is None:raise RuntimeError()
 if root.exists():
  if root.is_symlink() or not root.is_dir():raise RuntimeError()
  st=root.stat()
  if st.st_uid!=os.geteuid() or stat.S_IMODE(st.st_mode)!=0o700:raise RuntimeError()
  if not {p.name for p in root.iterdir()}.issubset(allowed):raise RuntimeError()
  for p in root.iterdir():
   if p.name=='__pycache__':
    if p.is_symlink() or not p.is_dir():raise RuntimeError()
    if any(x.is_symlink() or not x.is_file() for x in p.iterdir()):raise RuntimeError()
   elif p.is_symlink() or not p.is_file():raise RuntimeError()
  token=str(root).encode();refs=0
  for proc in Path('/proc').iterdir():
   if not proc.name.isdigit():continue
   try:
    if token in (proc/'cmdline').read_bytes():refs+=1
   except OSError:pass
  if refs>1:raise RuntimeError()
  shutil.rmtree(root)
 status='PASS' if not root.exists() else 'HOLD'
except Exception:pass
print('FV_OWNER_CLEANUP='+json.dumps({'operation_id':op,'status':status},sort_keys=True,separators=(',',':')),flush=True)
'''


def python_command(source, args):
    encoded = base64.b64encode(source.encode()).decode('ascii')
    demand(len(encoded) < 32768)
    demand(all(re.fullmatch(r'[A-Za-z0-9_./:-]+', value) for value in args))
    return ("python3 -c \"import base64;exec(base64.b64decode('" + encoded + "'))\" " +
            ' '.join("'" + value + "'" for value in args) + '\n').encode()


def send_candidate(session, raw, operation_id):
    session.send(f'FV_CATALOG_PAYLOAD {len(raw)} {operation_id}\n'.encode())
    for offset in range(0, len(raw), 3000):
        session.send(base64.b64encode(raw[offset:offset + 3000]) + b'\n')
    session.send(b'FV_CATALOG_PAYLOAD_END\n')


def consume_journal(path, operation_id, adapter_sha, candidate_sha):
    path = Path(path)
    demand(path.is_absolute() and path.parent.is_dir() and not path.exists() and not path.is_symlink())
    payload = canonical_json({'contract': 'FIELDVISIT_OWNER_CATALOG_LOCAL_ONCE_V1',
                              'operation_id': operation_id, 'status': 'ATTEMPTED',
                              'adapter_sha256': adapter_sha,
                              'candidate_sha256': candidate_sha})
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'wb') as output:
        output.write(payload)


class LiveAdapter:
    def __init__(self, args, auth, cli, candidate_raw, manifest, deadline):
        self.args, self.auth, self.cli = args, auth, cli
        self.raw, self.manifest, self.deadline = candidate_raw, manifest, deadline
        self.instance_name = None
        self.tunnel = self.session = self.temp = None
        self.root = REMOTE_PREFIX + auth['operation_id']
        self.storage_token = None
        self.receipt_raw = b''
        self.sessions = 0

    def verify_owner_target(self):
        self.instance_name = management_preflight(self.cli, self.args.subscription,
                                                   self.args.instance_id)
        return True

    def _open(self, cleanup=False):
        demand(self.sessions < 2)
        self.sessions += 1
        self.tunnel = Tunnel(self.args.subscription, self.instance_name, self.args.local_port)
        self.tunnel.__enter__()
        self.temp = tempfile.TemporaryDirectory(prefix='fv-owner-catalog-parent-')
        known = Path(self.temp.name) / 'known_hosts'
        known_host(self.args.local_port, known)
        self.session = SSH(ssh_argv(self.args.local_port, known), self.tunnel.password,
                           self.deadline)
        self.session.login(self.auth['operation_id'])

    def _close(self):
        if self.session:
            self.session.close()
            self.session = None
        if self.tunnel:
            self.tunnel.__exit__(None, None, None)
            self.tunnel = None
        if self.temp:
            self.temp.cleanup()
            self.temp = None

    def stage(self, payload):
        demand(payload == self.raw)
        self._open()
        self.session.send(python_command(REMOTE_STAGE, [self.root, sha256(payload),
                                                        self.auth['operation_id']]))
        send_candidate(self.session, payload, self.auth['operation_id'])
        marker = ('FV_OWNER_STAGE_READY=' + self.auth['operation_id']).encode()
        self.session.expect_line(marker, 120)

    def ready_noecho(self):
        sql_name = 'pathb_catalog_readonly_REVIEW_ONLY.sql'
        child_name = 'owner_sql_catalog_child_REVIEW_ONLY.py'
        command = ("cd '" + self.root + "' && python3 -c \"import importlib.util;"
                   "s=importlib.util.spec_from_file_location('owner_child','" + child_name + "');"
                   "m=importlib.util.module_from_spec(s);s.loader.exec_module(m);"
                   "raise SystemExit(m.candidate_execute('" + sql_name + "','" +
                   self.manifest['files'][sql_name] + "','" +
                   self.auth['operation_id'] + "','" + sha256(self.raw) + "',blob_claim_verified=True))\"\n")
        self.session.buf = b''
        self.session.send(command.encode())
        self.session.expect_line(b'FV_OWNER_ECHO_OFF=PASS', 60)
        self.session.expect_regex(rb'FV_OWNER_TOKEN_READY:$', 30)
        return True

    def claim_blob(self, candidate_sha):
        token = self.cli.token('https://storage.azure.com/', 'storage')
        try:
            token_claims(token, STORAGE_AUDIENCE, min_ttl=900)
            request = claim_request(token, candidate_sha, ttl_seconds=1800)
            return commit_claim(request)
        finally:
            token = None

    def sql_token(self):
        token = self.cli.token('https://database.windows.net/', 'sql')
        token_claims(token, SQL_AUDIENCE, min_ttl=600)
        return token

    def transmit_once(self, token):
        self.session.send(token.encode('ascii') + b'\n')

    def receive_terminal(self):
        first = self.session.expect_regex(rb'OWNER_SQL_CATALOG=[A-Z0-9_]+\r?$', 120)
        lines = [first]
        for marker in (b'INSTALLER=NOT_RUN', b'DDL=NO', b'RETRY=FORBIDDEN'):
            lines.append(self.session.expect_line(marker, 15))
        raw = b'\n'.join(line.rstrip(b'\r') for line in lines) + b'\n'
        result = extract_receipt(raw)
        demand(result in SAFE_TERMINALS)
        return result

    def cleanup_verified_owned(self):
        try:
            self._close()
            demand(self.sessions < 2)
            self._open(cleanup=True)
            self.session.send(python_command(REMOTE_CLEANUP,
                              [self.root, self.auth['operation_id']]))
            line = self.session.expect_regex(rb'FV_OWNER_CLEANUP=\{[^\r\n]+\}', 60)
            value = json.loads(line.split(b'=', 1)[1])
            return value == {'operation_id': self.auth['operation_id'], 'status': 'PASS'}
        except Exception:
            return False
        finally:
            try:
                self._close()
            except Exception:
                pass


def execute(args):
    started = int(time.time())
    root = Path(__file__).resolve().parent
    paths = {'adapter': Path(__file__).resolve(),
             'integration': root / 'owner_catalog_integration_REVIEW_ONLY.py',
             'parent': root / 'owner_catalog_parent_REVIEW_ONLY.py'}
    candidate_path, binding_path, auth_path = map(Path,
        (args.candidate, args.binding, args.authorization))
    for path in (*paths.values(), candidate_path, binding_path, auth_path):
        regular(path)
    binding_raw = binding_path.read_bytes()
    binding = safe_json(binding_path)
    validate_binding(binding, paths['adapter'], paths['integration'], paths['parent'])
    candidate_raw = candidate_path.read_bytes()
    manifest = inspect_candidate(candidate_raw, binding)
    auth = safe_json(auth_path)
    validate_authorization(auth, paths, candidate_raw, binding_raw, args.instance_id, started)
    consume_journal(args.journal, auth['operation_id'], sha256(paths['adapter'].read_bytes()),
                    sha256(candidate_raw))
    cli = AzureCLI()
    deadline = time.monotonic() + MAX_DURATION
    adapter = LiveAdapter(args, auth, cli, candidate_raw, manifest, deadline)
    report = Controller(adapter, sha256(candidate_raw)).run(candidate_raw)
    demand(cli.storage_tokens <= 1 and cli.sql_tokens <= 1 and adapter.sessions <= 2)
    demand(time.monotonic() <= deadline)
    print('OWNER_CATALOG=' + report.receipt)
    print('DURABLE_CLAIM=' + ('CONSUMED' if report.consumed else 'NOT_PROVEN'))
    print('SQL_TOKEN_HANDOFF=' + ('YES' if report.sql_token_sent else 'NO'))
    print('OWNED_CLEANUP=' + report.cleanup)
    print('INSTALLER=NOT_RUN; DDL=NO; DEPLOYMENT=NOT_RUN; RETRY=FORBIDDEN')
    return 0 if (report.receipt in ('EMPTY_CATALOG_CANDIDATE',
                 'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED') and
                 report.cleanup == 'PASS') else 1


def main(argv=None):
    parser = argparse.ArgumentParser(description='Authorization-locked Owner catalog adapter')
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--candidate')
    parser.add_argument('--binding')
    parser.add_argument('--authorization')
    parser.add_argument('--journal')
    parser.add_argument('--subscription')
    parser.add_argument('--instance-id')
    parser.add_argument('--local-port', type=int, default=51341)
    args = parser.parse_args(argv)
    if not args.execute:
        print('OWNER_CATALOG_ADAPTER=AUTHORIZATION_LOCKED_NONDEPLOY')
        print('AZURE=NOT_CONNECTED; TOKEN=NOT_REQUESTED; BLOB=NOT_WRITTEN; SQL=NOT_RUN')
        return 0
    try:
        demand(all((args.candidate, args.binding, args.authorization, args.journal,
                    args.subscription, args.instance_id)))
        demand(1024 <= args.local_port <= 65535)
        return execute(args)
    except Exception:
        print('OWNER_CATALOG=HOLD_OPERATION')
        print('INSTALLER=NOT_RUN; DDL=NO; DEPLOYMENT=NOT_RUN; RETRY=FORBIDDEN')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
