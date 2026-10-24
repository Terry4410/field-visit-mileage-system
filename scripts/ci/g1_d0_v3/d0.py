"""New one-shot D0 v3. Closed diagnostics; old v2 sources/history untouched."""
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shlex
import signal
import socket
import ssl
import sys
import time
import urllib.error

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'g1_bounded'))
from arm import APP, CONTEXT_SHA256, Reader, Stop, context_valid, exchange_token, paths
from main_channel import scm_url

STAGES = ('PRECONDITION', 'ARM_TOKEN', 'ARM_SITE_GET', 'ARM_INSTANCE_GET',
          'TARGET_SHAPE', 'SCM_TOKEN', 'WSS_CONNECT', 'WSS_101', 'SHELL_READY',
          'MAIN_PROCESS', 'SIX_HASHES', 'REMOTE_EXIT', 'RECEIPT')
ERRORS = ('CONTRACT_REJECTED', 'AUTH_DENIED', 'REDIRECT_DENIED', 'HTTP_FAILURE',
          'TLS_FAILURE', 'TIMEOUT', 'NETWORK_FAILURE', 'PERMISSION_DENIED',
          'FILE_UNAVAILABLE', 'REMOTE_EXIT_NONZERO', 'REMOTE_PROTOCOL', 'UNKNOWN')
DLLS = ('FieldVisit.Api.dll', 'FieldVisit.Application.dll',
        'FieldVisit.Domain.dll', 'FieldVisit.Infrastructure.dll')
FIXED = ('Microsoft.Data.SqlClient.dll', 'FieldVisit.Api.runtimeconfig.json')


class DiagnosticStop(Exception):
    def __init__(self, classification):
        self.classification = classification if classification in ERRORS else 'UNKNOWN'


def classify(error):
    if isinstance(error, DiagnosticStop):
        return error.classification
    if isinstance(error, urllib.error.HTTPError):
        code = error.code
        if type(code) is not int:
            return 'UNKNOWN'
        return ('AUTH_DENIED' if code in (401, 403) else
                'REDIRECT_DENIED' if 300 <= code < 400 else 'HTTP_FAILURE')
    if isinstance(error, ssl.SSLError):
        return 'TLS_FAILURE'
    if isinstance(error, (TimeoutError, socket.timeout)):
        return 'TIMEOUT'
    if isinstance(error, (urllib.error.URLError, ConnectionError)):
        return 'NETWORK_FAILURE'
    if isinstance(error, Stop):
        return 'CONTRACT_REJECTED'
    # Never inspect exception text, repr, provider body, URL or arbitrary attrs.
    return 'UNKNOWN'


class Receipt:
    def __init__(self):
        self.stage = 'PRECONDITION'
        self.last = 'UNKNOWN'
        self.remote_attempted = 'UNKNOWN'
        self.remote_completed = 'UNKNOWN'
        self.arm_attempts = self.arm_completed = 0
        self.wss_attempts = self.wss_101 = 0
        self.wss_101_state = 'NOT_ATTEMPTED'
        self.command_attempts = self.command_sent = 0
        self.payload_attempts = self.payload_sent = 0

    def begin(self, stage):
        if stage not in STAGES:
            raise DiagnosticStop('CONTRACT_REJECTED')
        self.stage = stage

    def complete(self):
        self.last = self.stage

    def packet(self, env, classification=None, observation=None):
        sha = env.get('GITHUB_SHA', '')
        packet = dict(protocol='D0_V3_DIAGNOSTIC', sourceSha=sha if re.fullmatch('[a-f0-9]{40}', sha) else 'UNKNOWN',
                      status='HOLD' if classification else 'PASS', stage=self.stage,
                      lastCompletedStage=self.last, remoteStageAttempted=self.remote_attempted,
                      remoteStageCompleted=self.remote_completed,
                      armGetAttempts=self.arm_attempts, armGetCompleted=self.arm_completed,
                      wssConnectAttempts=self.wss_attempts, wssHttp101=self.wss_101,
                      wssHttp101State=self.wss_101_state,
                      remoteCommandAttempts=self.command_attempts, remoteCommandSent=self.command_sent,
                      sourcePayloadAttempts=self.payload_attempts, sourcePayloadSent=self.payload_sent,
                      sqlOpenAttempts=0, sqlConnections=0, liveG1='NOT_EXECUTED', businessUat='HOLD')
        if classification:
            packet['errorClass'] = classification if classification in ERRORS else 'UNKNOWN'
        else:
            packet['mainObservation'] = observation
        return packet


class Session:
    """One short shell command, then fixed Python stdin bytes in the same WSS."""
    def __init__(self, receipt, clock=time.monotonic):
        self.receipt = receipt
        self.clock = clock
        self.end = clock() + 120
        self.ws = None
        self.buffer = b''
        self.total = 0

    def line(self, seconds):
        end = min(self.end, self.clock() + seconds)
        while b'\n' not in self.buffer:
            if self.clock() >= end:
                raise DiagnosticStop('TIMEOUT')
            self.ws.settimeout(max(.01, end - self.clock()))
            kind, data = self.ws.recv_data()
            if kind not in (1, 2) or not isinstance(data, (str, bytes)):
                raise DiagnosticStop('REMOTE_PROTOCOL')
            if isinstance(data, str):
                data = data.encode()
            self.total += len(data)
            if self.total > 2097152 or len(self.buffer) + len(data) > 1048576:
                raise DiagnosticStop('REMOTE_PROTOCOL')
            self.buffer += data
        line, self.buffer = self.buffer.split(b'\n', 1)
        return line.rstrip(b'\r')

    def record(self, nonce, seconds=45):
        end = min(self.end, self.clock() + seconds)
        while self.clock() < end:
            line = self.line(max(0, end - self.clock()))
            prefix = ('G1_D0_V3_DONE_' + nonce + '_').encode()
            if line.startswith(prefix):
                self.receipt.begin('REMOTE_EXIT')
                raise DiagnosticStop('REMOTE_EXIT_NONZERO' if line != prefix + b'0' else 'REMOTE_PROTOCOL')
            try:
                value = json.loads(line)
            except (ValueError, UnicodeError):
                continue  # PTY banners/echo never escape and remain byte/time bounded.
            if not isinstance(value, dict) or value.get('nonce') != nonce:
                continue
            return value
        raise DiagnosticStop('TIMEOUT')

    def progress(self, nonce, stage, state):
        value = self.record(nonce)
        if (set(value) == {'kind', 'nonce', 'stage', 'errorClass'} and
                value['kind'] == 'D0_V3_STOP' and value['stage'] == stage and
                value['errorClass'] in ('CONTRACT_REJECTED', 'PERMISSION_DENIED',
                                        'FILE_UNAVAILABLE', 'TIMEOUT', 'UNKNOWN')):
            raise DiagnosticStop(value['errorClass'])
        if value != dict(kind='D0_V3_STAGE', nonce=nonce, stage=stage, state=state):
            raise DiagnosticStop('REMOTE_PROTOCOL')
        if state == 'ATTEMPT':
            self.receipt.remote_attempted = stage
        else:
            self.receipt.remote_completed = stage

    def observe(self, site, instances, token, command, payload, nonce, connect):
        d = self.receipt
        url, instance = scm_url(site, instances)
        d.begin('WSS_CONNECT')
        d.wss_attempts = 1  # Before factory call; a thrown handshake consumes it.
        d.wss_101_state = 'UNKNOWN'
        self.ws = connect(url, header={'Authorization': 'Bearer ' + token}, cookie='ARRAffinity=' + instance,
                          timeout=30, redirect_limit=0,
                          sslopt={'cert_reqs': ssl.CERT_REQUIRED, 'check_hostname': True}, enable_multithread=False)
        d.complete()
        d.begin('WSS_101')
        status = self.ws.getstatus()
        if status != 101:
            d.wss_101_state = 'CONFIRMED_NON101'
            raise DiagnosticStop('REDIRECT_DENIED' if type(status) is int and 300 <= status < 400 else
                                 'AUTH_DENIED' if status in (401, 403) else 'HTTP_FAILURE')
        d.wss_101 = 1
        d.wss_101_state = 'CONFIRMED_101'
        d.complete()
        d.begin('SHELL_READY')
        d.command_attempts = 1
        self.ws.send(command.encode(), opcode=2)
        d.command_sent = 1  # send returned, not proof of remote execution.
        end = min(self.end, self.clock() + 30)
        while True:
            line = self.line(max(0, end - self.clock()))
            if line == ('G1_D0_V3_READY_' + nonce).encode():
                break
            if line.startswith(('G1_D0_V3_DONE_' + nonce + '_').encode()):
                raise DiagnosticStop('REMOTE_EXIT_NONZERO')
        d.complete()
        d.begin('MAIN_PROCESS')
        d.payload_attempts = 1
        self.ws.send(payload, opcode=2)  # Python stdin, not another shell command.
        d.payload_sent = 1
        for stage in ('MAIN_PROCESS', 'SIX_HASHES'):
            d.begin(stage)
            self.progress(nonce, stage, 'ATTEMPT')
            self.progress(nonce, stage, 'COMPLETE')
            d.complete()
        candidate = self.record(nonce)
        d.begin('REMOTE_EXIT')
        end = min(self.end, self.clock() + 15)
        while True:
            line = self.line(max(0, end - self.clock()))
            if not line:
                continue
            if line != ('G1_D0_V3_DONE_' + nonce + '_0').encode():
                raise DiagnosticStop('REMOTE_EXIT_NONZERO' if line.startswith(('G1_D0_V3_DONE_' + nonce + '_').encode()) else 'REMOTE_PROTOCOL')
            break
        d.complete()
        d.begin('RECEIPT')
        observation = validate_observation(candidate, nonce)
        d.complete()
        return observation

    def close(self):
        if self.ws is not None:
            try:
                self.ws.close(timeout=1)
            except Exception:
                pass
            self.ws = None


def validate_observation(value, nonce):
    expected = {'kind', 'nonce', 'runtime', 'fixedFiles', 'processStable', 'sqlConnections', 'configuration'}
    if (not isinstance(value, dict) or set(value) != expected or value['kind'] != 'D0_V3_OBSERVATION' or
            value['nonce'] != nonce or value['processStable'] is not True or
            type(value['sqlConnections']) is not int or value['sqlConnections'] != 0 or value['configuration'] != 'NOT_READ'):
        raise DiagnosticStop('CONTRACT_REJECTED')
    runtime, fixed = value['runtime'], value['fixedFiles']
    if (not isinstance(runtime, dict) or set(runtime) != {'assemblies', 'apiCompositeSha256'} or
            not isinstance(runtime['assemblies'], dict) or set(runtime['assemblies']) != set(DLLS) or
            not isinstance(fixed, dict) or set(fixed) != set(FIXED)):
        raise DiagnosticStop('CONTRACT_REJECTED')
    if any(not isinstance(h, str) or not re.fullmatch('[a-f0-9]{64}', h)
           for h in [*runtime['assemblies'].values(), *fixed.values()]):
        raise DiagnosticStop('CONTRACT_REJECTED')
    composite = hashlib.sha256(''.join(n + ':' + runtime['assemblies'][n] + '\n' for n in DLLS).encode()).hexdigest()
    if runtime['apiCompositeSha256'] != composite:
        raise DiagnosticStop('CONTRACT_REJECTED')
    # Fresh allowlist projection; no raw remote/provider object is printed.
    return dict(runtime=dict(assemblies={n: runtime['assemblies'][n] for n in DLLS}, apiCompositeSha256=composite),
                fixedFiles={n: fixed[n] for n in FIXED}, processStable=True, configuration='NOT_READ', sqlConnections=0)


def command_payload(source, nonce):
    if not 0 < len(source) <= 65536 or not re.fullmatch('[a-f0-9]{64}', nonce):
        raise Stop()
    code = source.decode('utf-8') + '\nraise SystemExit(run(' + repr(nonce) + '))\n'
    payload = code.encode()
    # Source arrives only after stty/READY. A long first canonical input line
    # would be truncated before stty can run. No new remote compression module.
    loader = 'import sys;exec(compile(sys.stdin.buffer.read(' + str(len(payload)) + '),"<reviewed-d0-v3>","exec"))'
    command = "PS1= PS2=; unset HISTFILE; stty -echo -icanon && printf '\\nG1_D0_V3_READY_" + nonce + "\\n' && /usr/bin/python3 -I -B -c " + shlex.quote(loader)
    command += " 2>/dev/null; result=$?; printf '\\nG1_D0_V3_DONE_" + nonce + "_%s\\n' \"$result\"; exit \"$result\"\n"
    if len(command.encode()) > 1024 or command.count('\n') != 1:
        raise Stop()
    return command, payload


def run(connect=None):
    d, session, reader = Receipt(), None, None
    e = os.environ
    try:
        if (not context_valid(e) or not re.fullmatch('[a-f0-9]{40}', e.get('GITHUB_SHA', '')) or
                e.get('G1_MODE') != 'D0_V3_DIAGNOSTIC' or e.get('ACCEPT_COST') != 'false' or
                e.get('G1_D0_V3_GUARD') != 'PASS' or sys.argv[1:]):
            raise Stop()
        ctx = [e.get(k, '').lower() for k in ('AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_CLIENT_ID')]
        if hashlib.sha256('\n'.join(ctx).encode()).hexdigest() != CONTEXT_SHA256:
            raise Stop()
        if connect is None:
            import websocket
            if websocket.__version__ != '1.8.0':
                raise Stop()
            connect = websocket.create_connection
        source = Path(__file__).with_name('remote.py').read_bytes()
        if not 0 < len(source) <= 65536:
            raise Stop()
        nonce = secrets.token_hex(32)
        command, payload = command_payload(source, nonce)
        site_path = paths(ctx[1])['api']
        instance_path = site_path.split('?')[0] + '/instances?api-version=2024-04-01'
        plan = {'api': site_path, 'instances': instance_path}
        d.complete()
        d.begin('ARM_TOKEN')
        token = exchange_token('https://management.azure.com/.default')
        d.complete()
        reader = Reader(plan, token, collections=(instance_path,))
        pages = {}
        for key, stage in (('api', 'ARM_SITE_GET'), ('instances', 'ARM_INSTANCE_GET')):
            d.begin(stage)
            try:
                pages[key] = reader.get(plan[key])
            finally:
                d.arm_attempts = reader.calls
            d.arm_completed += 1
            d.complete()
        d.begin('TARGET_SHAPE')
        p = pages['api'].get('properties', {})
        if pages['api'].get('name') != APP or p.get('reserved') is not True or p.get('state') != 'Running':
            raise Stop()
        scm_url(pages['api'], pages['instances'])
        d.complete()
        d.begin('SCM_TOKEN')
        scm_token = exchange_token('https://appservice.azure.com/.default')
        d.complete()
        session = Session(d)
        observation = session.observe(pages['api'], pages['instances'], scm_token, command, payload, nonce, connect)
        packet = d.packet(e, observation=observation)
        outcome = 0
    except Exception as error:
        packet = d.packet(e, classification=classify(error))
        outcome = 1
    finally:
        if session is not None:
            session.close()
    print(json.dumps(packet, sort_keys=True))
    return outcome


def supervisor_timeout(signum, frame):
    raise TimeoutError()


if __name__ == '__main__':
    signal.signal(signal.SIGTERM, supervisor_timeout)
    sys.exit(run())
