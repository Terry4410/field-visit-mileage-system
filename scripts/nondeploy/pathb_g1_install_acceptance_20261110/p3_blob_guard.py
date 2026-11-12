"""Path B P3 SQL INSTALL *marker guard only* — OFFLINE INTEGRATION CANDIDATE.

No main() or CLI; importing this file has no side effects. This does NOT execute
SQL or install anything. The eventual installer supervisor must call consume()
exactly once, immediately BEFORE the first installer SQL client call, after a
fresh full 22-field empty/identity/TLS preflight, dry-run, Owner token TTL and
package/Seal/14-layer integrity checks all pass.

An uncertain conditional PUT consumes the invocation: NEVER retry, delete or
reset markers. Neither this module nor its tests touch real Azure.
"""
import email.utils
import base64
import hashlib
import http.client
import json
import re
import secrets
import ssl
import time
from dataclasses import dataclass

ACCOUNT = 'stfvsc261026a'
CONTAINER = 'schema-evidence'
PRIOR_G1_INSTALL_BLOB = 'operator-once/pathb-g1-original-installer-single-20261110.json'
NEW_BLOB = 'operator-once/pathb-g1-original-installer-single-kudu-20261112.json'
PRIOR_V3_INSTALL_BLOB = 'operator-once/pathb-p3-kudu-install-d6c2208-v3-20261109.json'
PRIOR_INSTALL_BLOB = 'operator-once/pathb-p3-kudu-install-d6c2208-v1-20261106.json'
PRIOR_V2_INSTALL_BLOB = 'operator-once/pathb-p3-kudu-install-d6c2208-v2-20261109.json'
PREDECESSORS = (
    ('operator-once/pathb-select22-manual-v2-20261103.json', None),
    ('operator-once/pathb-select22-review-v3-20261103.json', 'pathb-select22-review-v3-20261103'),
)
SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DATABASE = 'db-fieldvisit-pathb-uat'
RELEASE = 'd6c220877b9976b6478d85b5171d4e06293595ae'
PACKAGE_SHA = '5a0c956d4ef5c88e0ff49d2d606ee9c1cb34e81e7f0c0f85b5aea8c5743e5828'
MANIFEST_SHA = 'e94c76952eebdf55e56ab6fffa9b1dd0dfc38ec1c4ae79cae36dfacb83c1a9c9'
INSTALLER_SHA = 'b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f'
SELECT_SHA = 'e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add'
OWNER_OID_SHA = '2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604'
TENANT_SHA = '976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c'
STORAGE_AUDIENCE = 'https://storage.azure.com'
CONTRACT = 'FIELDVISIT_PATHB_G1_ORIGINAL_INSTALL_BLOB_KUDU_ONCE_V1'
RUN_ID = 'pathb-g1-original-installer-single-kudu-20261112'
MAX_RESPONSE_BYTES = 8192
REQUIRED_PRE_DDL_GATES = (
    'EXACT_ORIGINAL_PACKAGE', 'MANIFEST_SEAL_14_LAYERS', 'PROTECTED_OWNER_KUDU',
    'OWNER_SQL_TOKEN_TTL', 'PRIVATE_DNS_TLS', 'EXACT_SQL_IDENTITY',
    'FRESH_22_FIELD_DATABASE_EMPTY', 'INSTALLER_DRY_RUN', 'SQL_BUDGET',
)


class InstallGuardHold(Exception):
    """Deliberately machine-safe category only; never include raw server errors."""
    def __init__(self, code):
        if not re.fullmatch(r'[A-Z][A-Z0-9_]{2,80}', code):
            code = 'GUARD_HOLD_REDACTED'
        self.code = code
        super().__init__(code)


def require(ok, code):
    if not ok:
        raise InstallGuardHold(code)


@dataclass(frozen=True)
class Response:
    status: int
    etag: str = ''
    body: bytes = b''
    error_code: str = ''


def validate_owner_storage_token(token, *, now=None):
    """Validate claims of a token delivered by the separately authenticated
    Owner Azure CLI -> pinned SSH channel. This is NOT JWT signature validation:
    caller must also prove original trusted token provenance and SSH binding.
    """
    require(isinstance(token, str) and len(token) > 50, 'STORAGE_TOKEN_NOT_VALIDATED')
    try:
        parts = token.split('.')
        require(len(parts) == 3 and all(parts), 'STORAGE_TOKEN_FORMAT')
        payload = base64.urlsafe_b64decode(parts[1] + '=' * (-len(parts[1]) % 4))
        require(len(payload) <= 8192, 'STORAGE_TOKEN_SIZE')
        claims = json.loads(payload.decode('utf-8'))
        require(isinstance(claims, dict), 'STORAGE_TOKEN_FORMAT')
        oid = str(claims['oid']).lower()
        tid = str(claims['tid']).lower()
        aud = str(claims['aud']).rstrip('/')
        nbf = float(claims.get('nbf', 0))
        exp = float(claims['exp'])
    except (KeyError, ValueError, TypeError, UnicodeError) as exc:
        raise InstallGuardHold('STORAGE_TOKEN_FORMAT') from exc
    except Exception as exc:
        raise InstallGuardHold('STORAGE_TOKEN_FORMAT') from exc
    now = time.time() if now is None else now
    return _validate_claims(oid, tid, aud, nbf, exp, now, OWNER_OID_SHA, TENANT_SHA)


def _validate_claims(oid, tid, aud, nbf, exp, now, owner_sha, tenant_sha):
    # Internal pure function: tests may use synthetic hashes, while production
    # validate_owner_storage_token always passes the pinned constants above.
    require(hashlib.sha256(oid.encode()).hexdigest() == owner_sha, 'STORAGE_TOKEN_OWNER')
    require(hashlib.sha256(tid.encode()).hexdigest() == tenant_sha, 'STORAGE_TOKEN_TENANT')
    require(aud == STORAGE_AUDIENCE, 'STORAGE_TOKEN_AUDIENCE')
    require(nbf <= now + 30 and exp - now >= 3000, 'STORAGE_TOKEN_TIME')
    return True


class OneTimeInstallBlobGuard:
    """Explicitly injected request() for unit tests. No hidden retry or delete.

    request(method, blob_name, headers, body=b'') -> Response
    For production, the request function is pinned_http_request and must use a
    previously separately validated Owner Storage-audience access token.
    """
    def __init__(self, token, *, request, owner_oid_sha=OWNER_OID_SHA):
        validate_owner_storage_token(token)
        require(owner_oid_sha == OWNER_OID_SHA, 'OWNER_BINDING')
        self._token = token
        self._request = request
        self.creation_attempted = False
        self.consumed = False
        self.predecessors_verified = False
        self.new_key_available = False
        self._prepared = None

    def headers(self, put=False, size=None):
        h = {
            'Authorization': 'Bearer ' + self._token,
            'x-ms-version': '2023-11-03',
            'x-ms-date': email.utils.formatdate(usegmt=True),
        }
        if put:
            h.update({
                'If-None-Match': '*',
                'x-ms-blob-type': 'BlockBlob',
                'Content-Type': 'application/json',
                'Content-Length': str(size),
            })
        return h

    def _call(self, method, blob, headers, body=b'', *, code='BLOB_UNAVAILABLE'):
        try:
            r = self._request(method, blob, headers, body)
        except Exception as ex:
            raise InstallGuardHold(code) from ex
        require(isinstance(r, Response), 'BLOB_RESPONSE_INVALID')
        require(isinstance(r.body, bytes) and len(r.body) <= MAX_RESPONSE_BYTES,
                'BLOB_RESPONSE_SIZE')
        return r

    def check_predecessors(self):
        require(not self.creation_attempted, 'GUARD_ALREADY_ATTEMPTED')
        for blob, attempt in PREDECESSORS:
            r = self._call('GET', blob, self.headers(), code='PREDECESSOR_UNAVAILABLE')
            require(r.status == 200 and bool(r.etag) and 0 < len(r.body) <= 4096,
                    'PREDECESSOR_MISSING_OR_UNKNOWN')
            try:
                obj = json.loads(r.body.decode('utf-8'))
            except (UnicodeError, json.JSONDecodeError) as ex:
                raise InstallGuardHold('PREDECESSOR_MALFORMED') from ex
            require(isinstance(obj, dict), 'PREDECESSOR_MALFORMED')
            # Both predecessor SELECT markers were consumed before their SQL call.
            expected = {
                'contract': 'FIELDVISIT_PATHB_SELECT22_BLOB_ONCE_V1',
                'status': 'CONSUMED_BEFORE_SQL',
                'run_id': 'pathb-select22-manual-v2-20261103',
                'release': RELEASE,
                'server': SERVER,
                'database': DATABASE,
                'owner_oid_sha256': OWNER_OID_SHA,
                'preflight_sha256': SELECT_SHA,
            }
            require(all(obj.get(k) == v for k, v in expected.items()),
                    'PREDECESSOR_BINDING_MISMATCH')
            if attempt is not None:
                require(obj.get('attempt_id') == attempt, 'PREDECESSOR_ATTEMPT_MISMATCH')
            require(type(obj.get('started_at_utc')) is int and
                    isinstance(obj.get('invocation_nonce'), str) and
                    re.fullmatch(r'[a-f0-9]{32}', obj['invocation_nonce']) is not None,
                    'PREDECESSOR_METADATA_INVALID')
        self.predecessors_verified = True

    def check_available(self):
        require(self.predecessors_verified and not self.creation_attempted,
                'PREDECESSOR_NOT_VERIFIED')
        previous = self._call('HEAD', PRIOR_INSTALL_BLOB, self.headers(), code='PRIOR_INSTALL_MARKER_UNKNOWN')
        require(previous.status == 404 and previous.error_code == 'BlobNotFound', 'PRIOR_INSTALL_MARKER_PRESENT_OR_UNKNOWN')
        previous_v2 = self._call('HEAD', PRIOR_V2_INSTALL_BLOB, self.headers(), code='PRIOR_V2_MARKER_UNKNOWN')
        require(previous_v2.status == 404 and previous_v2.error_code == 'BlobNotFound', 'PRIOR_V2_MARKER_PRESENT_OR_UNKNOWN')
        previous_v3 = self._call('HEAD', PRIOR_V3_INSTALL_BLOB, self.headers(), code='PRIOR_V3_MARKER_UNKNOWN')
        require(previous_v3.status == 404 and previous_v3.error_code == 'BlobNotFound', 'PRIOR_V3_MARKER_PRESENT_OR_UNKNOWN')
        prior_g1 = self._call('HEAD', PRIOR_G1_INSTALL_BLOB, self.headers(), code='PRIOR_G1_MARKER_UNKNOWN')
        require(prior_g1.status == 404 and prior_g1.error_code == 'BlobNotFound',
                'PRIOR_G1_MARKER_PRESENT_OR_UNKNOWN')
        r = self._call('HEAD', NEW_BLOB, self.headers(), code='INSTALL_MARKER_HEAD_UNKNOWN')
        if r.status == 404 and r.error_code == 'BlobNotFound':
            self.new_key_available = True
            return
        if r.status == 200:
            raise InstallGuardHold('INSTALL_REPLAY_BLOCKED')
        raise InstallGuardHold('INSTALL_MARKER_HEAD_DENIED_OR_UNKNOWN')

    def prepare(self, verified_gates, *, operation_id, artifact_sha256, instance_sha256):
        """Call once immediately before first installer child (after fresh SELECT).

        Gates must be emitted by independently reviewed, source-bound supervisor,
        not supplied from user input or an untrusted text file.
        """
        require(isinstance(verified_gates, dict) and
                set(verified_gates) == set(REQUIRED_PRE_DDL_GATES) and
                all(verified_gates[k] == 'PASS' for k in REQUIRED_PRE_DDL_GATES),
                'INSTALL_PRE_DDL_GATES_HOLD')
        require(self.predecessors_verified and self.new_key_available and
                not self.creation_attempted and not self.consumed,
                'INSTALL_MARKER_NOT_AVAILABLE')
        require(re.fullmatch(r'[0-9a-f]{32}',str(operation_id)) is not None and
                re.fullmatch(r'[0-9a-f]{64}',str(artifact_sha256)) is not None and
                re.fullmatch(r'[0-9a-f]{64}',str(instance_sha256)) is not None,
                'EXACT_G1_OPERATION_BINDING')
        marker = {
            'contract': CONTRACT,
            'operation_id': operation_id,
            'artifact_sha256': artifact_sha256,
            'instance_sha256': instance_sha256,
            'status': 'CONSUMED_BEFORE_INSTALLER_SQL',
            'run_id': RUN_ID,
            'release': RELEASE,
            'server': SERVER,
            'database': DATABASE,
            'owner_oid_sha256': OWNER_OID_SHA,
            'original_package_sha256': PACKAGE_SHA,
            'manifest_sha256': MANIFEST_SHA,
            'installer_sha256': INSTALLER_SHA,
            'preflight_sha256': SELECT_SHA,
            'created_at_utc': int(time.time()),
            'nonce': secrets.token_hex(16),
        }
        body = (json.dumps(marker, sort_keys=True, separators=(',', ':')) + '\n').encode()
        require(len(body) < 4096, 'INSTALL_MARKER_TOO_LARGE')
        self._prepared = body
        return hashlib.sha256(body).hexdigest()

    def consume(self, verified_gates):
        """One external atomic PUT once, only after all local preparation."""
        require(self._prepared is not None and isinstance(verified_gates, dict) and
                set(verified_gates) == set(REQUIRED_PRE_DDL_GATES) and
                all(verified_gates[k] == 'PASS' for k in REQUIRED_PRE_DDL_GATES) and
                self.predecessors_verified and self.new_key_available and
                not self.creation_attempted and not self.consumed,
                'INSTALL_NOT_PREPARED_OR_PREVIOUSLY_CONSUMED')
        body = self._prepared
        # Exactly one PUT with ambiguous response treated as consumed.
        self.creation_attempted = True
        r = self._call('PUT', NEW_BLOB, self.headers(put=True, size=len(body)), body,
                       code='INSTALL_PUT_UNKNOWN_STOP')
        require(r.status == 201 and bool(r.etag), 'INSTALL_CREATE_NOT_CONFIRMED_STOP')
        self.consumed = True
        readback = self._call('GET', NEW_BLOB, self.headers(),
                              code='INSTALL_READBACK_UNKNOWN_STOP')
        require(readback.status == 200 and readback.etag == r.etag and readback.body == body,
                'INSTALL_READBACK_MISMATCH_STOP')
        return hashlib.sha256(body).hexdigest()


def pinned_http_request(storage_token):
    """Return no-redirect, no-retry HTTPS request function for existing private Blob.

    Outer installer MUST validate the Owner Storage token OID/tenant/audience,
    network allowlist, one-use scope and expiry independently before calling.
    """
    validate_owner_storage_token(storage_token)
    def send(method, blob_name, headers, body=b''):
        require(method in ('HEAD', 'GET', 'PUT') and
                blob_name in (NEW_BLOB, PRIOR_G1_INSTALL_BLOB, PRIOR_INSTALL_BLOB, PRIOR_V2_INSTALL_BLOB, PRIOR_V3_INSTALL_BLOB, *(x[0] for x in PREDECESSORS)),
                'BLOB_METHOD_OR_NAME_DENIED')
        c = http.client.HTTPSConnection(ACCOUNT + '.blob.core.windows.net',
                                       timeout=18, context=ssl.create_default_context())
        try:
            c.request(method, '/' + CONTAINER + '/' + blob_name,
                      body=body, headers=headers)
            resp = c.getresponse()
            return Response(resp.status, resp.getheader('ETag', ''),
                            resp.read(MAX_RESPONSE_BYTES + 1),
                            resp.getheader('x-ms-error-code', ''))
        finally:
            c.close()
    return send
