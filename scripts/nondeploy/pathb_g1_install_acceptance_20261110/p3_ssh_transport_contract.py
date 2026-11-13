"""P3 SSH transport contract, importable but *no* CLI or live dispatch.

Adapts previously demonstrated official App Service SSH tunnel.  Credentials
and token transport are expressly excluded from default/offline execution.
The future live parent must bind this module's exact SHA and require separate
Owner DDL authorization.  No shell subprocess runs on import.
"""
import base64
import hashlib
import json
from pathlib import Path
import re

RG='rg-fieldvisit-uat'
APP='api-fieldvisit-pathb-uat'
HOST_FP='SHA256:FKM/np9YzcuZaOsPqy3TL4gcae0szuZ3Z0dEG06KyZk'
OWNER_SHA='2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604'
TENANT_SHA='976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c'
SUB_SHA='7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47'
REMOTE_STAGE='/tmp/fv-pathb-g1-original-installer-20261110-stage'
LEGACY_STAGES=('/tmp/fv-pathb-p3-install-once2-stage-20261103',
               '/tmp/fv-pathb-install-d6c2208-once1',
               '/tmp/fv-pathb-select22-blobguard-review-v3-stagefix1-20261103',
               '/tmp/fv-pathb-select22-blobguard-review-v3-20261103')
ALLOWED_AUDIENCES={'sql':'https://database.windows.net','storage':'https://storage.azure.com'}

class Hold(Exception): pass

def digest(raw):return hashlib.sha256(raw).hexdigest()

def check_owner_account(record):
    if (not isinstance(record,dict) or digest(str(record.get('id','')).lower().encode())!=SUB_SHA
            or digest(str(record.get('tenantId','')).lower().encode())!=TENANT_SHA
            or str(record.get('user',{}).get('type','')).lower()!='user'):
        raise Hold('OWNER_CLOUD_ACCOUNT_HOLD')
    return True

def ssh_args(port, known_hosts, remote_command):
    """Only a *preapproved literal* remote command can ever be selected.
    The live stage/execution action is intentionally NOT supplied here.
    """
    if not isinstance(port,int) or port<1024 or port>65535:raise Hold('SSH_PORT')
    if not isinstance(known_hosts,Path) or not known_hosts.is_absolute():raise Hold('SSH_KEY_FILE')
    if remote_command != 'FV_P3_STAGE_READ_ONLY':raise Hold('SSH_REMOTE_NOT_RELEASED')
    return ['ssh','-T','-p',str(port),'-m','hmac-sha1',
            '-o','PreferredAuthentications=password','-o','PubkeyAuthentication=no',
            '-o','NumberOfPasswordPrompts=1','-o','ConnectTimeout=15',
            '-o','ServerAliveInterval=10','-o','ServerAliveCountMax=2',
            '-o','HostKeyAlgorithms=ssh-ed25519','-o','StrictHostKeyChecking=yes',
            '-o','UserKnownHostsFile='+str(known_hosts),'root@127.0.0.1',remote_command]

def ssh_interactive_args(port, known_hosts):
    """Pinned interactive App Service SSH over the official Azure tunnel.

    No remote command is accepted.  The parent drives one fixed protocol over
    stdin after matching the shell prompt; callers cannot inject a command.
    """
    if not isinstance(port,int) or port<1024 or port>65535:raise Hold('SSH_PORT')
    if not isinstance(known_hosts,Path) or not known_hosts.is_absolute():raise Hold('SSH_KEY_FILE')
    return ['ssh','-tt','-p',str(port),'-m','hmac-sha1',
            '-o','PreferredAuthentications=password','-o','PubkeyAuthentication=no',
            '-o','NumberOfPasswordPrompts=1','-o','ConnectTimeout=15',
            '-o','ServerAliveInterval=10','-o','ServerAliveCountMax=2',
            '-o','HostKeyAlgorithms=ssh-ed25519','-o','StrictHostKeyChecking=yes',
            '-o','UserKnownHostsFile='+str(known_hosts),'root@127.0.0.1']

def pinned_host_fingerprint(output):
    # Output of ssh-keygen -lf against the single scanned ssh-ed25519 key.
    text=output.decode('ascii','replace') if isinstance(output,bytes) else str(output)
    if HOST_FP not in text.split():raise Hold('SSH_HOST_KEY_MISMATCH')
    return True

def validate_owner_token(jwt,kind,now):
    if kind not in ALLOWED_AUDIENCES:raise Hold('TOKEN_KIND')
    if not isinstance(jwt,str) or not jwt.isascii() or len(jwt)>16384:raise Hold('TOKEN_SHAPE')
    try:
        segments=jwt.split('.')
        if len(segments)!=3 or not all(segments):raise ValueError()
        payload=base64.urlsafe_b64decode(segments[1]+'='*(-len(segments[1])%4))
        if len(payload)>8192:raise ValueError()
        claims=json.loads(payload)
        if not isinstance(claims,dict):raise ValueError()
        oid=str(claims['oid']).lower();tid=str(claims['tid']).lower()
        nbf=float(claims.get('nbf',0));exp=float(claims['exp'])
        aud=str(claims['aud']).rstrip('/')
    except Exception as ex:
        raise Hold('TOKEN_CLAIMS') from ex
    if (digest(oid.encode())!=OWNER_SHA or digest(tid.encode())!=TENANT_SHA
            or aud!=ALLOWED_AUDIENCES[kind] or nbf>now+30 or exp-now<3000):
        raise Hold('TOKEN_OWNER_AUDIENCE_OR_TTL')
    return True

def validate_staging_root(root):
    if str(root)!=REMOTE_STAGE or any(str(root)==legacy for legacy in LEGACY_STAGES):
        raise Hold('REUSED_REMOTE_STAGE_OR_UNKNOWN_ROOT')
    return True

def incoming_frame(sql_token,storage_token):
    """Constructs stdin-only frame; must never be logged, saved or argv.
    This merely builds bytes; delivery requires separately reviewed live runner.
    """
    if not isinstance(sql_token,str) or not isinstance(storage_token,str):raise Hold('TOKEN_FRAME')
    if not re.fullmatch(r'[A-Za-z0-9._-]{50,16384}',sql_token):raise Hold('TOKEN_FRAME')
    if not re.fullmatch(r'[A-Za-z0-9._-]{50,16384}',storage_token):raise Hold('TOKEN_FRAME')
    return (json.dumps({'sql_token':sql_token,'storage_token':storage_token},separators=(',',':'))+'\n').encode('ascii')

if __name__=='__main__':
    raise SystemExit('NONDEPLOY: NO LIVE P3 SSH/TOKEN/DDL ENTRYPOINT')
