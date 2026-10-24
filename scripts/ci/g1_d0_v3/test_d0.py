"""Synthetic only: no sockets, Azure, SQL, original API or SDK execution."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import pty
import select
import ssl
import subprocess
import sys
import termios
import unittest
from unittest.mock import Mock, patch
import urllib.error

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('diagnostic_v3', HERE / 'd0.py')
d0 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(d0)
remote_spec = importlib.util.spec_from_file_location('remote_v3', HERE / 'remote.py')
remote = importlib.util.module_from_spec(remote_spec)
remote_spec.loader.exec_module(remote)
NONCE = 'a' * 64
SECRET = 'SYNTHETIC_PASSWORD_TOKEN_MFA_IDENTITY_NOT_REAL'
BAD_TEXT = 'https://private.invalid/' + SECRET + '\nAuthorization: Bearer ' + SECRET
CTX = ['synthetic-tenant', 'synthetic-subscription', 'synthetic-client']
ENV = dict(GITHUB_ACTIONS='true', GITHUB_REPOSITORY='Terry4410/field-visit-mileage-system',
           GITHUB_REF='refs/heads/post-uat/v1.8.0', GITHUB_EVENT_NAME='workflow_dispatch',
           GITHUB_RUN_ATTEMPT='1', GITHUB_ACTOR='Terry4410', GITHUB_TRIGGERING_ACTOR='Terry4410',
           GITHUB_SHA='b' * 40, APPROVED_SHA='b' * 40, G1_MODE='D0_V3_DIAGNOSTIC', ACCEPT_COST='false',
           G1_D0_V3_GUARD='PASS', AZURE_TENANT_ID=CTX[0], AZURE_SUBSCRIPTION_ID=CTX[1], AZURE_CLIENT_ID=CTX[2])
SITE_PATH = d0.paths(CTX[1])['api']
INSTANCE_PATH = SITE_PATH.split('?')[0] + '/instances?api-version=2024-04-01'
SITE = dict(id=SITE_PATH.split('?')[0], name='api-fieldvisit-uat', properties=dict(
    reserved=True, state='Running', hostNameSslStates=[dict(hostType='Repository', name='api-fieldvisit-uat.scm.azurewebsites.net')]))
INSTANCES = dict(value=[dict(id=INSTANCE_PATH.split('?')[0] + '/' + 'c' * 32, name='c' * 32)])


def observation():
    hashes = {name: hashlib.sha256(name.encode()).hexdigest() for name in d0.DLLS}
    composite = hashlib.sha256(''.join(n + ':' + hashes[n] + '\n' for n in d0.DLLS).encode()).hexdigest()
    return dict(kind='D0_V3_OBSERVATION', nonce=NONCE, runtime=dict(assemblies=hashes, apiCompositeSha256=composite),
                fixedFiles={n: hashlib.sha256(n.encode()).hexdigest() for n in d0.FIXED},
                processStable=True, sqlConnections=0, configuration='NOT_READ')


def frames(candidate=None):
    result = ['G1_D0_V3_READY_' + NONCE]
    for stage in ('MAIN_PROCESS', 'SIX_HASHES'):
        for state in ('ATTEMPT', 'COMPLETE'):
            result.append(json.dumps(dict(kind='D0_V3_STAGE', nonce=NONCE, stage=stage, state=state)))
    result.extend([json.dumps(candidate or observation()), 'G1_D0_V3_DONE_' + NONCE + '_0'])
    return result


class Response:
    status = 200
    def __init__(self, request, value):
        self.request, self.raw = request, json.dumps(value).encode()
    def geturl(self):
        return self.request.full_url
    def read(self, size):
        return self.raw[:size]
    def __enter__(self):
        return self
    def __exit__(self, *args):
        pass


class Opener:
    def __init__(self, pages, fail=0, error=None):
        self.pages, self.fail, self.error, self.calls = pages, fail, error, []
    def open(self, request, timeout):
        self.calls.append(request)
        if self.fail == len(self.calls):
            raise self.error
        return Response(request, self.pages[len(self.calls) - 1])


class WS:
    def __init__(self, lines=None, status=101, send_error=None, receive_error=None, fragment=False):
        self.status, self.send_error, self.receive_error = status, send_error, receive_error
        raw = ('\n'.join(frames() if lines is None else lines) + '\n').encode()
        self.data = [raw[i:i + 11] for i in range(0, len(raw), 11)] if fragment else [raw]
        self.sent, self.closed = [], False
    def getstatus(self):
        return self.status
    def send(self, data, opcode):
        self.sent.append((data, opcode))
        if self.send_error:
            raise self.send_error
    def settimeout(self, value):
        pass
    def recv_data(self):
        if self.receive_error:
            raise self.receive_error
        if not self.data:
            raise TimeoutError(BAD_TEXT)
        return 2, self.data.pop(0)
    def close(self, timeout):
        self.closed = True


class DiagnosticTests(unittest.TestCase):
    def setUp(self):
        # A regression that accidentally reaches a real transport fails offline.
        self.stack = contextlib.ExitStack()
        self.stack.enter_context(patch('socket.socket.connect', side_effect=AssertionError('OFFLINE_NETWORK_FORBIDDEN')))
        self.stack.enter_context(patch('urllib.request.OpenerDirector.open', side_effect=AssertionError('OFFLINE_NETWORK_FORBIDDEN')))
        self.addCleanup(self.stack.close)

    def execute(self, *, fail_get=0, error=None, pages=None, token_fail=0, ws=None, connect_error=None, env=None):
        opener = Opener(pages or [SITE, INSTANCES], fail_get, error)
        ws = ws or WS()
        connect = Mock(side_effect=connect_error, return_value=ws)
        scopes = []
        def token(scope):
            scopes.append(scope)
            if len(scopes) == token_fail:
                raise error
            return SECRET
        real_reader = d0.Reader
        def reader(plan, token, **kwargs):
            return real_reader(plan, token, opener=opener, **kwargs)
        out, err = io.StringIO(), io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.dict(os.environ, ENV if env is None else env, clear=True))
            stack.enter_context(patch.object(sys, 'argv', ['d0.py']))
            stack.enter_context(patch.object(d0, 'CONTEXT_SHA256', hashlib.sha256('\n'.join(CTX).encode()).hexdigest()))
            stack.enter_context(patch.object(d0, 'exchange_token', token))
            stack.enter_context(patch.object(d0, 'Reader', reader))
            stack.enter_context(patch.object(d0.secrets, 'token_hex', return_value=NONCE))
            stack.enter_context(contextlib.redirect_stdout(out))
            stack.enter_context(contextlib.redirect_stderr(err))
            status = d0.run(connect)
        text = out.getvalue()
        self.assertEqual(err.getvalue(), '')
        self.assertNotIn(SECRET, text)
        self.assertNotIn('https://', text)
        for item in CTX:
            self.assertNotIn(item, text)
        receipt = json.loads(text)
        self.assertEqual(receipt['sqlOpenAttempts'], 0)
        self.assertEqual(receipt['sqlConnections'], 0)
        self.assertEqual(receipt['armGetAttempts'], len(opener.calls))
        self.assertLessEqual(receipt['armGetAttempts'], 2)
        self.assertLessEqual(receipt['wssConnectAttempts'], 1)
        self.assertEqual(receipt['wssConnectAttempts'], connect.call_count)
        self.assertIn(receipt['stage'], d0.STAGES)
        self.assertIn(receipt['lastCompletedStage'], (*d0.STAGES, 'UNKNOWN'))
        if status:
            self.assertNotIn('mainObservation', receipt)
            self.assertIn(receipt['errorClass'], d0.ERRORS)
        if connect.called and not connect_error:
            self.assertTrue(ws.closed)
        return status, receipt, ws, connect, scopes

    def assert_hold(self, result, stage, attempts, completed, wss=0, upgrade=0, commands=0, sent=0, error=None):
        status, r, *_ = result
        self.assertEqual(status, 1)
        self.assertEqual(r['status'], 'HOLD')
        self.assertEqual(r['stage'], stage)
        self.assertEqual((r['armGetAttempts'], r['armGetCompleted']), (attempts, completed))
        self.assertEqual((r['wssConnectAttempts'], r['wssHttp101'], r['remoteCommandAttempts'], r['remoteCommandSent']),
                         (wss, upgrade, commands, sent))
        if error:
            self.assertEqual(r['errorClass'], error)

    def test_precondition_rejects_before_any_transport(self):
        for key, value in [('G1_D0_V3_GUARD', ''), ('G1_MODE', 'FULL_G1'), ('ACCEPT_COST', 'true'),
                           ('GITHUB_REF', 'refs/heads/main'), ('GITHUB_RUN_ATTEMPT', '2'),
                           ('GITHUB_SHA', SECRET), ('AZURE_CLIENT_ID', 'other')]:
            with self.subTest(key=key):
                self.assert_hold(self.execute(env=dict(ENV, **{key: value})), 'PRECONDITION', 0, 0)

    def test_arm_token_denied_and_unknown_never_expose_error(self):
        for error, classification in [(urllib.error.HTTPError(BAD_TEXT, 403, BAD_TEXT, {}, io.BytesIO(BAD_TEXT.encode())), 'AUTH_DENIED'),
                                      (RuntimeError(BAD_TEXT), 'UNKNOWN'), (ssl.SSLError(BAD_TEXT), 'TLS_FAILURE')]:
            with self.subTest(classification=classification):
                self.assert_hold(self.execute(token_fail=1, error=error), 'ARM_TOKEN', 0, 0, error=classification)

    def test_site_get_attempt_consumed_on_failure(self):
        self.assert_hold(self.execute(fail_get=1, error=TimeoutError(BAD_TEXT)), 'ARM_SITE_GET', 1, 0, error='TIMEOUT')

    def test_instance_get_attempt_consumed_on_failure(self):
        self.assert_hold(self.execute(fail_get=2, error=urllib.error.URLError(BAD_TEXT)), 'ARM_INSTANCE_GET', 2, 1, error='NETWORK_FAILURE')

    def test_arm_redirect_and_malformed_collection_stop_without_followup(self):
        self.assert_hold(self.execute(fail_get=1, error=urllib.error.HTTPError(BAD_TEXT, 302, BAD_TEXT, {}, None)),
                         'ARM_SITE_GET', 1, 0, error='REDIRECT_DENIED')
        for page in [dict(INSTANCES, nextLink=BAD_TEXT), {'value': []}, {'value': [*INSTANCES['value'], *INSTANCES['value']]}]:
            with self.subTest(shape=list(page)):
                self.assert_hold(self.execute(pages=[SITE, page]), 'ARM_INSTANCE_GET', 2, 1)

    def test_target_shape_rejects_nonrunning_linux_or_foreign_scm(self):
        for change in [dict(state='Stopped'), dict(reserved=False), dict(hostNameSslStates=[dict(hostType='Repository', name='other.scm.azurewebsites.net')])]:
            p = copy.deepcopy(SITE)
            p['properties'].update(change)
            self.assert_hold(self.execute(pages=[p, INSTANCES]), 'TARGET_SHAPE', 2, 2)

    def test_scm_token_denied_is_separate_from_arm(self):
        self.assert_hold(self.execute(token_fail=2, error=urllib.error.HTTPError(BAD_TEXT, 401, BAD_TEXT, {}, None)),
                         'SCM_TOKEN', 2, 2, error='AUTH_DENIED')

    def test_wss_connect_failure_consumes_only_attempt(self):
        for error in (TimeoutError(BAD_TEXT), ConnectionError(BAD_TEXT), RuntimeError(BAD_TEXT)):
            result = self.execute(connect_error=error)
            self.assert_hold(result, 'WSS_CONNECT', 2, 2, wss=1)
            self.assertEqual(result[1]['wssHttp101State'], 'UNKNOWN')

    def test_wss_non101_and_redirect_send_no_command(self):
        for status, classification in [(302, 'REDIRECT_DENIED'), (403, 'AUTH_DENIED'), (200, 'HTTP_FAILURE')]:
            result = self.execute(ws=WS(status=status))
            self.assert_hold(result, 'WSS_101', 2, 2, wss=1, error=classification)
            self.assertEqual(result[2].sent, [])
            self.assertEqual(result[1]['wssHttp101State'], 'CONFIRMED_NON101')

    def test_send_failure_and_shell_timeout_counts_are_distinct(self):
        self.assert_hold(self.execute(ws=WS(send_error=ConnectionError(BAD_TEXT))), 'SHELL_READY', 2, 2, 1, 1, 1, 0)
        self.assert_hold(self.execute(ws=WS(lines=[], receive_error=TimeoutError(BAD_TEXT))), 'SHELL_READY', 2, 2, 1, 1, 1, 1, 'TIMEOUT')

    def test_python_absent_has_no_invented_remote_stage(self):
        result = self.execute(ws=WS(lines=frames()[:1] + ['G1_D0_V3_DONE_' + NONCE + '_127']))
        self.assert_hold(result, 'REMOTE_EXIT', 2, 2, 1, 1, 1, 1, 'REMOTE_EXIT_NONZERO')
        self.assertEqual(result[1]['remoteStageAttempted'], 'UNKNOWN')
        self.assertEqual(result[1]['remoteStageCompleted'], 'UNKNOWN')

    def test_remote_process_ambiguity_and_missing_hash_file_have_precise_stage(self):
        for stage, index, classification in [('MAIN_PROCESS', 2, 'CONTRACT_REJECTED'), ('SIX_HASHES', 4, 'FILE_UNAVAILABLE')]:
            lines = frames()[:index] + [json.dumps(dict(kind='D0_V3_STOP', nonce=NONCE, stage=stage, errorClass=classification))]
            result = self.execute(ws=WS(lines=lines))
            self.assert_hold(result, stage, 2, 2, 1, 1, 1, 1, classification)
            self.assertEqual(result[1]['remoteStageAttempted'], stage)

    def test_remote_unallowlisted_error_or_json_never_escapes(self):
        for value in [dict(kind='D0_V3_STOP', nonce=NONCE, stage='MAIN_PROCESS', errorClass=BAD_TEXT),
                      dict(kind='D0_V3_STAGE', nonce=NONCE, stage='MAIN_PROCESS', state='ATTEMPT', credentials=BAD_TEXT)]:
            lines = frames()[:1] + [json.dumps(value)]
            self.assert_hold(self.execute(ws=WS(lines=lines)), 'MAIN_PROCESS', 2, 2, 1, 1, 1, 1, 'REMOTE_PROTOCOL')

    def test_malformed_pty_json_times_out_without_exposing_it(self):
        self.assert_hold(self.execute(ws=WS(lines=frames()[:1] + ['{"nonce":' + BAD_TEXT])), 'MAIN_PROCESS', 2, 2, 1, 1, 1, 1, 'TIMEOUT')

    def test_remote_exit_nonzero_and_timeout_cannot_pass(self):
        self.assert_hold(self.execute(ws=WS(lines=frames()[:-1] + ['G1_D0_V3_DONE_' + NONCE + '_1'])),
                         'REMOTE_EXIT', 2, 2, 1, 1, 1, 1, 'REMOTE_EXIT_NONZERO')
        self.assert_hold(self.execute(ws=WS(lines=frames()[:-1])), 'REMOTE_EXIT', 2, 2, 1, 1, 1, 1, 'TIMEOUT')

    def test_receipt_rejects_missing_hashes_and_extra_secrets_after_exit0(self):
        variants = []
        v = observation(); del v['fixedFiles'][d0.FIXED[0]]; variants.append(v)
        v = observation(); v['credentials'] = BAD_TEXT; variants.append(v)
        v = observation(); v['runtime']['assemblies'][d0.DLLS[0]] = BAD_TEXT; variants.append(v)
        v = observation(); v['runtime']['apiCompositeSha256'] = '0' * 64; variants.append(v)
        v = observation(); v['sqlConnections'] = False; variants.append(v)
        for value in variants:
            result = self.execute(ws=WS(lines=frames(value)))
            self.assert_hold(result, 'RECEIPT', 2, 2, 1, 1, 1, 1, 'CONTRACT_REJECTED')
            self.assertEqual(result[1]['lastCompletedStage'], 'REMOTE_EXIT')

    def test_success_requires_all_stages_hashes_same_session_exit_and_tls(self):
        status, result, ws, connect, scopes = self.execute(ws=WS(fragment=True))
        self.assertEqual((status, result['status'], result['stage'], result['lastCompletedStage']), (0, 'PASS', 'RECEIPT', 'RECEIPT'))
        self.assertEqual((result['armGetAttempts'], result['armGetCompleted'], result['wssConnectAttempts'], result['wssHttp101'], result['remoteCommandAttempts'], result['remoteCommandSent']), (2, 2, 1, 1, 1, 1))
        self.assertEqual(result['remoteStageCompleted'], 'SIX_HASHES')
        self.assertEqual(set(result['mainObservation']['runtime']['assemblies']) | set(result['mainObservation']['fixedFiles']), set(d0.DLLS + d0.FIXED))
        self.assertEqual(scopes, ['https://management.azure.com/.default', 'https://appservice.azure.com/.default'])
        self.assertEqual(len(ws.sent), 2)
        command, opcode = ws.sent[0]
        self.assertEqual(opcode, 2)
        self.assertEqual(command.count(b'\n'), 1)
        self.assertIn(b'/usr/bin/python3 -I -B -c', command)
        self.assertLessEqual(len(command), 1024)
        self.assertIn(b'sys.stdin.buffer.read(', command)
        self.assertNotIn(SECRET.encode(), command)
        self.assertEqual((result['sourcePayloadAttempts'], result['sourcePayloadSent']), (1, 1))
        self.assertTrue(ws.sent[1][0].startswith((HERE / 'remote.py').read_bytes()))
        options = connect.call_args.kwargs
        self.assertEqual(options['redirect_limit'], 0)
        self.assertEqual(options['sslopt'], dict(cert_reqs=ssl.CERT_REQUIRED, check_hostname=True))

    def test_supervisor_signal_raises_closed_timeout(self):
        with self.assertRaises(TimeoutError):
            d0.supervisor_timeout(15, None)

    def test_byte_budget_and_unexpected_wire_opcode_fail_closed(self):
        ws = WS()
        ws.data = [(SECRET * 30000).encode()]
        self.assert_hold(self.execute(ws=ws), 'SHELL_READY', 2, 2, 1, 1, 1, 1, 'REMOTE_PROTOCOL')
        ws = WS()
        ws.recv_data = Mock(return_value=(9, SECRET.encode()))
        self.assert_hold(self.execute(ws=ws), 'SHELL_READY', 2, 2, 1, 1, 1, 1, 'REMOTE_PROTOCOL')

    def test_malformed_provider_exception_uses_unknown_without_stringification(self):
        class HostileError(Exception):
            def __str__(self):
                raise AssertionError('EXCEPTION_MUST_NOT_BE_STRINGIFIED')
        self.assert_hold(self.execute(token_fail=1, error=HostileError()), 'ARM_TOKEN', 0, 0, error='UNKNOWN')
        self.assertEqual(d0.classify(urllib.error.HTTPError(BAD_TEXT, SECRET, BAD_TEXT, {}, None)), 'UNKNOWN')


class RemoteTests(unittest.TestCase):
    def test_remote_negative_process_and_hash_boundaries_redact(self):
        for stage, target, error in [('MAIN_PROCESS', 'find_api', remote.ContractStop(BAD_TEXT)),
                                     ('MAIN_PROCESS', 'find_api', RuntimeError(BAD_TEXT)),
                                     ('SIX_HASHES', 'runtime_snapshot', FileNotFoundError(BAD_TEXT)),
                                     ('SIX_HASHES', 'runtime_snapshot', PermissionError(BAD_TEXT))]:
            with self.subTest(stage=stage, error=type(error).__name__):
                out = io.StringIO()
                with contextlib.ExitStack() as stack:
                    stack.enter_context(contextlib.redirect_stdout(out))
                    stack.enter_context(patch.object(remote, 'find_api', return_value=7))
                    stack.enter_context(patch.object(remote, 'process_start', return_value=8))
                    stack.enter_context(patch.object(remote, target, side_effect=error))
                    self.assertEqual(remote.run(NONCE), 1)
                self.assertNotIn(SECRET, out.getvalue())
                values = [json.loads(v) for v in out.getvalue().splitlines()]
                self.assertEqual(values[-1]['kind'], 'D0_V3_STOP')
                self.assertEqual(values[-1]['stage'], stage)
                self.assertIn(values[-1]['errorClass'], d0.ERRORS)

    def test_remote_success_uses_fixed_files_and_detects_changed_process(self):
        for starts, expected in [([8, 8], 0), ([8, 9], 1)]:
            out = io.StringIO()
            with contextlib.ExitStack() as stack:
                stack.enter_context(contextlib.redirect_stdout(out))
                stack.enter_context(patch.object(remote, 'find_api', return_value=7))
                stack.enter_context(patch.object(remote, 'process_start', side_effect=starts))
                runtime = observation()['runtime']
                stack.enter_context(patch.object(remote, 'runtime_snapshot', return_value=runtime))
                file_hash = stack.enter_context(patch.object(remote, 'file_hash', return_value='d' * 64))
                self.assertEqual(remote.run(NONCE), expected)
            self.assertEqual([str(c.args[0]) for c in file_hash.call_args_list], ['/home/site/wwwroot/' + n for n in d0.FIXED])
            self.assertNotIn(SECRET, out.getvalue())
            self.assertEqual(json.loads(out.getvalue().splitlines()[-1])['kind'], 'D0_V3_OBSERVATION' if not expected else 'D0_V3_STOP')


class NativePtyTests(unittest.TestCase):
    def test_one_short_command_then_large_fixed_stdin_payload_on_canonical_pty(self):
        # Local /bin/sh + synthetic Python only. No Azure, sockets or original API.
        source = ('fixture_padding=' + repr('x' * 16000) + '\nLINES=' + repr(frames()[1:-1]) +
                  '\ndef run(nonce):\n for line in LINES: print(line,flush=True)\n return 0\n').encode()
        command, payload = d0.command_payload(source, NONCE)
        self.assertGreater(len(payload), 4096)
        self.assertLessEqual(len(command.encode()), 1024)
        master, slave = pty.openpty()
        self.assertTrue(termios.tcgetattr(slave)[3] & termios.ICANON)
        process = subprocess.Popen(['/bin/sh'], stdin=slave, stdout=slave, stderr=slave,
                                   env={'PATH': '/usr/bin:/bin'}, start_new_session=True)
        os.close(slave)
        class PtyWS:
            sent = 0
            def getstatus(self):
                return 101
            def send(self, data, opcode):
                self.sent += 1
                self.assert_binary = opcode == 2
                view = memoryview(data)
                while view:
                    view = view[os.write(master, view):]
            def settimeout(self, seconds):
                self.timeout = min(5, seconds)
            def recv_data(self):
                ready, _, _ = select.select([master], [], [], self.timeout)
                if not ready:
                    raise TimeoutError()
                return 2, os.read(master, 65536)
            def close(self, timeout):
                pass
        ws, receipt = PtyWS(), d0.Receipt()
        session = d0.Session(receipt)
        try:
            value = session.observe(SITE, INSTANCES, 'synthetic-token', command, payload, NONCE, lambda *a, **k: ws)
            self.assertTrue(value['processStable'])
            self.assertEqual(receipt.last, 'RECEIPT')
            self.assertEqual((receipt.command_attempts, receipt.command_sent, receipt.payload_attempts, receipt.payload_sent), (1, 1, 1, 1))
            self.assertEqual(ws.sent, 2)
            self.assertTrue(ws.assert_binary)
            self.assertEqual(process.wait(timeout=2), 0)
        finally:
            session.close()
            os.close(master)
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=2)


if __name__ == '__main__':
    unittest.main()
