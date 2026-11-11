#!/usr/bin/env python3
"""Field Visit Path B non-SQL single-shot tool preparation (OWNER REVIEW).
No Azure API, SQL token, SQL query, dpkg install, app config or deployment.
This script is reviewed but has NOT been executed against App Service.
"""
import hashlib
import os
from pathlib import Path
import platform
import shutil
import stat
import subprocess
import sys
import tempfile
import types
import urllib.request

APP = 'api-fieldvisit-pathb-uat'
RELEASE = 'c2224ab7725f24b491151ec2816525e5eb2ba9e1'
SRC_SHA = '5f2d49e3165c3506176d8c30e465462c23dcd8460ba7544331b54b2cbb4898e0'
SRC_URL = ('https://raw.githubusercontent.com/Terry4410/field-visit-mileage-system/' + RELEASE +
           '/scripts/nondeploy/pathb_g1_tool2_preclaim_fix_20261110/g1_preclaim_tools.py')
CLAIM = Path('/tmp/fv-pathb-tools-review-20261111-once.claim')
# Fixed immutable packages from docs/release/pathb/preflight-v2/tools-binding.json
TOOLS = [
    {'name':'msodbcsql18','url':'https://packages.microsoft.com/debian/12/prod/pool/main/m/msodbcsql18/msodbcsql18_18.6.1.1-1_amd64.deb','sha256':'cd217d3bf471d521dac6886456e6c758c21d4f84f88f7adf48ef672c70e57cb0'},
    {'name':'mssql-tools18','url':'https://packages.microsoft.com/debian/12/prod/pool/main/m/mssql-tools18/mssql-tools18_18.6.1.1-1_amd64.deb','sha256':'c90c16188a533576d0c3b0f4af82cb679edccd369e2626b928998918840c7787'},
    {'name':'libltdl7','url':'https://deb.debian.org/debian/pool/main/libt/libtool/libltdl7_2.4.7-7~deb12u1_amd64.deb','sha256':'8070b550321d4bd1c298f52f75374898d39112b709026d2ea5362ef1277b775a'},
    {'name':'libodbc2','url':'https://deb.debian.org/debian/pool/main/u/unixodbc/libodbc2_2.3.11-2+deb12u1_amd64.deb','sha256':'0af106cc7545f86bfa2d6f4fe644b98470704e73568feb395e07d5d60eeaefe4'},
    {'name':'libodbcinst2','url':'https://deb.debian.org/debian/pool/main/u/unixodbc/libodbcinst2/libodbcinst2_2.3.11-2+deb12u1_amd64.deb','sha256':'416463622a35f7e981577f0c817a3d6148a7eb99225d5f9cf433187be3f91c33'},
]

class Stop(Exception): pass

def check_env():
    if os.environ.get('WEBSITE_SITE_NAME') != APP:
        raise Stop('APP_TARGET_HOLD')
    d = {}
    for line in Path('/etc/os-release').read_text().splitlines():
        if '=' in line and not line.startswith('#'):
            k, v = line.split('=', 1)
            d[k] = v.strip('"')
    if d.get('ID') != 'debian' or d.get('VERSION_ID') != '12' or platform.machine() != 'x86_64':
        raise Stop('OS_ARCH_HOLD')
    if not shutil.which('dpkg-deb') or not shutil.which('ldd'):
        raise Stop('LOCAL_TOOLS_HOLD')
    if shutil.disk_usage('/tmp').free < 268435456:
        raise Stop('SPACE_HOLD')
    # Fails closed if previous one-shot claim exists; no reset or replay.
    if CLAIM.exists() or CLAIM.is_symlink():
        raise Stop('ALREADY_ATTEMPTED_HOLD')

def no_redirect_open(url):
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *a, **k): return None
    return urllib.request.build_opener(NoRedirect()).open(
        urllib.request.Request(url, headers={'Accept-Encoding':'identity'}), timeout=30)

def load_verified_tools():
    try:
        with no_redirect_open(SRC_URL) as res:
            if res.status != 200: raise Stop('SOURCE_HTTP_HOLD')
            raw = res.read(20_001)
        if len(raw)>20_000 or hashlib.sha256(raw).hexdigest() != SRC_SHA:
            raise Stop('SOURCE_SHA_HOLD')
        mod = types.ModuleType('pinned_g1_preclaim_tools')
        exec(compile(raw, '<verified_tools>', 'exec'), mod.__dict__)
        return mod
    except Stop: raise
    except Exception: raise Stop('SOURCE_TRANSPORT_HOLD')

def run():
    check_env()
    fd = os.open(CLAIM, os.O_CREAT | os.O_EXCL | os.O_WRONLY | getattr(os, 'O_NOFOLLOW', 0), 0o600)
    with os.fdopen(fd,'w') as f: f.write('ATTEMPTED; NO RETRY\n')
    root = Path(tempfile.mkdtemp(prefix='fv-pathb-tools-once-', dir='/tmp'))
    os.chmod(root, 0o700)
    cache = root / 'cache'; cache.mkdir(mode=0o700)
    ext = root / 'extracted'; ext.mkdir(mode=0o700)
    print('ATTEMPT=CONSUMED', flush=True)
    tools = load_verified_tools()
    # The original approved implementation validates SHA individually, no auto-retry.
    try:
        receipt = tools.acquire_all(TOOLS, cache)
    except Exception:
        raise Stop('PACKAGE_ACQUISITION_HOLD')
    if sorted(receipt) != sorted(t['name'] for t in TOOLS):
        raise Stop('PACKAGE_SET_HOLD')
    for i, item in enumerate(TOOLS, 1):
        if tools.verify_private_tool(i, cache/(item['name']+'.deb'), item['sha256']) <= 0:
            raise Stop('PACKAGE_SHA_HOLD')
    print('PACKAGE_SHA=5/5_PASS', flush=True)
    # Only here, after all five hashes pass, extract; never dpkg -i, never maintainer scripts.
    for item in TOOLS:
        p = subprocess.run(['dpkg-deb','-x',str(cache/(item['name']+'.deb')),str(ext)],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=90)
        if p.returncode:
            raise Stop('EXTRACT_HOLD')
    cmd = ext/'opt/mssql-tools18/bin/sqlcmd'
    if not cmd.is_file() or not os.access(cmd,os.X_OK):
        raise Stop('SQLCMD_BINARY_HOLD')
    libs = ext/'usr/lib/x86_64-linux-gnu'
    env = os.environ.copy()
    env['LD_LIBRARY_PATH'] = str(libs)+':'+str(ext/'opt/microsoft/msodbcsql18/lib64')
    p = subprocess.run(['ldd',str(cmd)],capture_output=True,timeout=15,env=env)
    if p.returncode or b'not found' in p.stdout or b'not found' in p.stderr:
        raise Stop('LINKER_HOLD')
    print('SQLCMD_LDD=PASS', flush=True)
    p = subprocess.run([str(cmd),'-?'],capture_output=True,timeout=15,env=env)
    if p.returncode or not (p.stdout or p.stderr):
        raise Stop('SQLCMD_HELP_HOLD')
    print('SQLCMD_HELP=PASS', flush=True)
    print('TOOLS_PREP=PASS_NONSQL_ONLY', flush=True)
    print('PRIVATE_ROOT='+str(root), flush=True)
    return 0

if __name__ == '__main__':
    try: sys.exit(run())
    except Stop as e:
        print('TOOLS_PREP=HOLD', flush=True)
        print('STOP_CLASS='+str(e), flush=True)
        print('AUTO_RETRY=FORBIDDEN', flush=True)
        sys.exit(1)
    except Exception:
        print('TOOLS_PREP=HOLD', flush=True)
        print('STOP_CLASS=UNEXPECTED_HOLD', flush=True)
        print('AUTO_RETRY=FORBIDDEN', flush=True)
        sys.exit(1)
