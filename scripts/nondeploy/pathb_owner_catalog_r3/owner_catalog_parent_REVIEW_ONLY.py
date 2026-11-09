#!/usr/bin/env python3
"""Path B Owner Catalog SSH/Blob orchestration candidate -- deliberately NONEXECUTABLE.
Only offline helpers and contracts are callable. No Azure calls from main().
"""
import base64
import hashlib
import ipaddress
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

RG = 'rg-fieldvisit-uat'
APP = 'api-fieldvisit-pathb-uat'
SQL_SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DATABASE = 'db-fieldvisit-pathb-uat'
STORAGE_ACCOUNT = 'stfvsc261026a'
CONTAINER = 'schema-evidence'
# Unique new marker, not any consumed v2/v3/MI marker.
MARKER = 'operator-once/pathb-owner-catalog-once-r4-durable-20261109.json'
HOST_FP = 'SHA256:qlMlSfV+nfx3Y5BdqQC+4WY1VFChCdRZMjqE+T/NgOI'
OWNER_SHA = '2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604'
TENANT_SHA = '976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c'
SUB_SHA = '7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47'
PROMPT = b'FV_OWNER_TOKEN_READY:'
ALLOWED_RECEIPTS = frozenset(('EMPTY_CATALOG_CANDIDATE',
     'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED', 'HOLD_SQL_LOGIN_OR_QUERY',
     'HOLD_OPERATION', 'HOLD_PRECHECK', 'HOLD_RESULT_SHAPE',
     'HOLD_SQL_TARGET_OR_METADATA', 'HOLD_PARTIAL_OR_INCONSISTENT',
     'HOLD_RESULT_INCONSISTENT', 'HOLD_DURABLE_ONCE_GUARD'))

class Hold(Exception):
    pass

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def exact_identity(account, signed_user):
    return (sha(str(account.get('id','')).lower().encode()) == SUB_SHA
      and sha(str(account.get('tenantId','')).lower().encode()) == TENANT_SHA
      and account.get('user',{}).get('type','').lower() == 'user'
      and sha(str(signed_user.get('id','')).lower().encode()) == OWNER_SHA)

def token_claims(access_token, audience, min_ttl=120):
    """Shape check only, not JWT signature validation; real CLI authenticated issuer required."""
    if not isinstance(access_token,str) or not 100 <= len(access_token) <= 4096:
        raise Hold('TOKEN_SHAPE')
    parts=access_token.split('.')
    if len(parts)!=3:
        raise Hold('TOKEN_SHAPE')
    try:
        body=json.loads(base64.urlsafe_b64decode(parts[1]+'='*(-len(parts[1])%4)))
        if sha(str(body.get('oid','')).lower().encode()) != OWNER_SHA:
            raise Hold('OWNER_MISMATCH')
        if sha(str(body.get('tid','')).lower().encode()) != TENANT_SHA:
            raise Hold('TENANT_MISMATCH')
        if body.get('aud') not in audience:
            raise Hold('AUDIENCE_MISMATCH')
        if not isinstance(body.get('nbf'),int) or body['nbf'] > time.time() + 30:
            raise Hold('TOKEN_NBF')
        if not isinstance(body.get('exp'),int) or body['exp'] - time.time() < min_ttl:
            raise Hold('TOKEN_TTL')
        return True
    except (ValueError, UnicodeError, KeyError, TypeError) as exc:
        raise Hold('TOKEN_CLAIMS') from None

def claim_request(storage_bearer, authorized_sha, ttl_seconds=1800):
    """Build conditional PUT. Must be used only by a freshly authorized runner.

    Azure Blob Storage If-None-Match:* is atomic on service, unlike CIFS claim.
    Never log headers (bearer). On any ambiguous response the attempt is consumed.
    """
    if not re.fullmatch(r'[0-9a-f]{64}',authorized_sha):
        raise Hold('APPROVED_SHA')
    if not 1 <= ttl_seconds <= 1800:
        raise Hold('TTL')
    if not isinstance(storage_bearer,str) or len(storage_bearer)<100:
        raise Hold('STORAGE_TOKEN')
    uri=f'https://{STORAGE_ACCOUNT}.blob.core.windows.net/{CONTAINER}/{MARKER}'
    body=json.dumps({'contract':'FV_OWNER_CATALOG_R3_ONCE',
       'candidate_sha256':authorized_sha,'ttl_seconds':ttl_seconds},
       sort_keys=True,separators=(',',':')).encode()
    return urllib.request.Request(uri,method='PUT',data=body,headers={
       'Authorization':'Bearer '+storage_bearer,'x-ms-version':'2023-11-03',
       'x-ms-blob-type':'BlockBlob','If-None-Match':'*',
       'Content-Type':'application/json','Content-Length':str(len(body))})

def commit_claim(req, opener=urllib.request.urlopen):
    """Run exactly once; distinguish 201 from every other/unknown response.
    Caller MUST NOT retry on timeout, 412 or any exception.
    """
    try:
        with opener(req,timeout=12) as response:
            if response.status != 201:
                raise Hold('CLAIM_NOT_CREATED')
        return True
    except Exception:
        raise Hold('CLAIM_UNKNOWN_OR_CONSUMED') from None

def extract_receipt(raw):
    """Only accept whole receipt lines, no partial outputs or auth strings."""
    if not isinstance(raw,bytes) or len(raw)>32768:
        raise Hold('OUTPUT_BOUND')
    text=raw.decode('utf-8','replace')
    lines=[s.strip() for s in text.splitlines() if s.startswith('OWNER_SQL_CATALOG=')]
    if len(lines)!=1:
        raise Hold('RECEIPT_COUNT')
    val=lines[0].split('=',1)[1]
    if val not in ALLOWED_RECEIPTS:
        raise Hold('RECEIPT_INVALID')
    if 'INSTALLER=NOT_RUN' not in text or 'DDL=NO' not in text or 'RETRY=FORBIDDEN' not in text:
        raise Hold('RECEIPT_FIELDS')
    return val

def trusted_ssh_argv(local_port, known_hosts_file):
    if not isinstance(local_port,int) or not 1024 <= local_port <= 65535:
        raise Hold('PORT')
    if not pathlib.Path(known_hosts_file).is_file():
        raise Hold('HOST_PIN')
    return ['ssh','-tt','-p',str(local_port),'-m','hmac-sha1',
     '-o','PreferredAuthentications=password','-o','PubkeyAuthentication=no',
     '-o','NumberOfPasswordPrompts=1','-o','ConnectTimeout=15',
     '-o','HostKeyAlgorithms=ssh-ed25519','-o','StrictHostKeyChecking=yes',
     '-o','UserKnownHostsFile='+str(known_hosts_file),'root@127.0.0.1']

def verify_ready_boundary(transcript):
    """Ready marker must be single, exact, terminal, and after observed echo-off ACK.
    The transport must send token only after this and never read/log token value.
    """
    if not isinstance(transcript,bytes) or len(transcript)>65536:
        raise Hold('TRANSCRIPT_BOUND')
    normalized=transcript.replace(b'\r\n',b'\n')
    if normalized.count(b'FV_OWNER_ECHO_OFF=PASS\n')!=1:
        raise Hold('NO_ECHO_ACK')
    if normalized.count(PROMPT)!=1 or not normalized.endswith(PROMPT):
        raise Hold('READY_HANDSHAKE')
    return True

def main():
    # No '--execute' option by design. Deployment requires separate independently
    # reviewed source, exact SHA approval and explicit one-shot owner authority.
    print('OWNER_CATALOG_PARENT=HOLD_REVIEW_ONLY_NOT_EXECUTABLE')
    print('TOKEN=NOT_REQUESTED; SQL=NOT_RUN; BLOB=NOT_WRITTEN')
    return 1

if __name__ == '__main__':
    sys.exit(main())
