#!/usr/bin/env python3
"""NONDEPLOY REVIEW CANDIDATE: ONE-SHOT, read-only Path B Catalog.
NOT LIVE AUTHORIZED. Needs separate Owner authorization tied to exact candidate SHA.
DO NOT run outside existing api-fieldvisit-pathb-uat SSH. No SQL DDL/DML/installer.
"""
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request

APP = 'api-fieldvisit-pathb-uat'
SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DB = 'db-fieldvisit-pathb-uat'
PRIVATE_IP = '10.90.2.4'
RELEASE = 'd34a248394c2a583966fd7bf376a7f97827113c0'
CATALOG_URL = 'https://raw.githubusercontent.com/Terry4410/field-visit-mileage-system/' + RELEASE + '/scripts/nondeploy/pathb_owner_catalog_r3/pathb_catalog_readonly_REVIEW_ONLY.sql'
CATALOG_SHA = 'd5a4f5825cd95227661921ed8577882053998f0fb103c49b64658f69ccca39b0'
CATALOG_BYTES = 2567
ROOT = Path('/tmp/fv-pathb-tools-once-zcr6bflq/extracted')
CLAIM = Path('/tmp/fv-pathb-catalog-classified-v3-new-authorization-required.claim')
EXPECTED_BIN_SHA = '4234f2b7db28e68b3f216712ab9fb016229823c111e008dc97749a8ae9190609'
EXPECTED_DRIVER_SHA = 'bfc10e264cf1b065485a472bb8186a57566a0b98cbf6712bf2fad48b6a9f369c'
CODE = re.compile(r'^FV_CAT\|(?:PASS|HOLD)\|(?:PASS|HOLD)\|(?:PASS|HOLD)\|(?:PASS|HOLD)\|\d+\|\d+\|[01]\|[01]\|(?:\d+|NA)\|(?:\d+|NA)\|\d+\|\d+\|\d+$')

class Hold(Exception):
    pass

# Allowlisted, ambiguous -> UNCLASSIFIED. Never print raw client output.
ERROR_PATTERNS = {
    'CLIENT_DRIVER_OR_DSN': (r'\\bIM002\\b', r'\\bIM003\\b', r'can.t open lib', r'data source name not found', r'driver.*could not be loaded'),
    'TLS_OR_CERT': (r'certificate verify failed', r'certificate chain.*not trusted', r'ssl provider', r'tls.*handshake'),
    'NETWORK_OR_TIMEOUT': (r'\\bHYT00\\b', r'\\bHYT01\\b', r'\\b08001\\b', r'\\b08S01\\b', r'login timeout expired', r'tcp provider'),
    'SQL_LOGIN_OR_PERMISSION': (r'\\b28000\\b', r'\\b18456\\b', r'login failed for user', r'\\bpermission was denied\\b', r'\\b229\\b'),
    'SQL_SYNTAX_OR_OBJECT': (r'incorrect syntax near', r'invalid object name', r'\\b208\\b', r'\\b102\\b'),
    'SQLCMD_USAGE': (r'unknown option', r'unrecognized option', r'invalid option', r'^usage: sqlcmd'),
}

def categorize_sqlcmd_failure(stdout, stderr):
    if not isinstance(stdout, bytes) or not isinstance(stderr, bytes):
        return 'UNCLASSIFIED'
    msg = (stdout[:65536] + b'\\n' + stderr[:65536]).decode('utf-8', 'replace')
    found = [category for category, patterns in ERROR_PATTERNS.items()
             if any(re.search(pattern, msg, re.I | re.M) for pattern in patterns)]
    return found[0] if len(found) == 1 else 'UNCLASSIFIED'

def safe_sqlcmd_exitcode(code):
    return str(code) if isinstance(code, int) and not isinstance(code, bool) and 0 <= code <= 255 else 'UNKNOWN'

def digest(x): return hashlib.sha256(x).hexdigest()

def exact(path, sha):
    if path.is_symlink() or not path.is_file() or digest(path.read_bytes()) != sha:
        raise Hold('BIN_INTEGRITY_HOLD')

def retrieve_catalog():
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *a, **kw): return None
    try:
        request = urllib.request.Request(CATALOG_URL, headers={'Accept-Encoding':'identity'})
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=15) as resp:
            if resp.status != 200: raise Hold('SOURCE_HTTP_HOLD')
            body = resp.read(CATALOG_BYTES + 1)
        if len(body) != CATALOG_BYTES or digest(body) != CATALOG_SHA:
            raise Hold('SOURCE_SHA_HOLD')
        return body
    except Hold: raise
    except Exception: raise Hold('SOURCE_HTTP_HOLD')

def validate():
    if os.environ.get('WEBSITE_SITE_NAME') != APP: raise Hold('APP_HOLD')
    if CLAIM.exists() or CLAIM.is_symlink(): raise Hold('ALREADY_ATTEMPTED')
    if not os.environ.get('IDENTITY_ENDPOINT') or not os.environ.get('IDENTITY_HEADER'):
        raise Hold('IDENTITY_INTERFACE_HOLD')
    parsed = urllib.parse.urlsplit(os.environ['IDENTITY_ENDPOINT'])
    if parsed.scheme != 'http' or not parsed.hostname or not (ipaddress.ip_address(parsed.hostname).is_loopback or ipaddress.ip_address(parsed.hostname).is_link_local):
        raise Hold('IDENTITY_ENDPOINT_HOLD')
    if parsed.username or parsed.password or parsed.fragment or parsed.query: raise Hold('IDENTITY_ENDPOINT_HOLD')
    cmd = ROOT/'opt/mssql-tools18/bin/sqlcmd'
    driver = ROOT/'opt/microsoft/msodbcsql18/lib64/libmsodbcsql-18.6.so.1.1'
    exact(cmd, EXPECTED_BIN_SHA)
    exact(driver, EXPECTED_DRIVER_SHA)
    if not (ROOT/'usr/lib/x86_64-linux-gnu/libodbc.so.2').exists(): raise Hold('LIBRARY_HOLD')
    try:
        ips = {a[4][0] for a in socket.getaddrinfo(SERVER, 1433, type=socket.SOCK_STREAM)}
    except Exception: raise Hold('PRIVATE_DNS_HOLD')
    if ips != {PRIVATE_IP}: raise Hold('PRIVATE_DNS_HOLD')
    if any(os.environ.get(k) for k in ['SQLCMDUSER','SQLCMDPASSWORD','SQLCMDINI','OSQLUSER','OSQLPASSWORD']):
        raise Hold('AMBIENT_AUTH_HOLD')
    return cmd,driver,parsed

def request_one_token(parsed):
    q = urllib.parse.urlencode({'resource':'https://database.windows.net/','api-version':'2019-08-01'})
    target = urllib.parse.urlunsplit((parsed.scheme,parsed.netloc, parsed.path, q,''))
    req = urllib.request.Request(target, headers={'X-IDENTITY-HEADER':os.environ['IDENTITY_HEADER'], 'Accept-Encoding':'identity'})
    try:
        with urllib.request.build_opener(urllib.request.ProxyHandler({}), type('NoRedirect', (urllib.request.HTTPRedirectHandler,), {'redirect_request': lambda self,*args,**kwargs: None})()).open(req,timeout=12) as resp:
            if resp.status != 200: raise Hold('TOKEN_RESPONSE_HOLD')
            payload = resp.read(16385)
        if len(payload)>16384: raise Hold('TOKEN_RESPONSE_HOLD')
        data=json.loads(payload)
        token=data.get('access_token')
        if not isinstance(token,str) or not re.fullmatch(r'[A-Za-z0-9_\-\.]+',token) or len(token)<100:
            raise Hold('TOKEN_FORMAT_HOLD')
        exp=data.get('expires_on')
        if exp is not None:
            try:
                if float(exp)<time.time()+120: raise Hold('TOKEN_LIFETIME_HOLD')
            except (TypeError,ValueError): raise Hold('TOKEN_LIFETIME_HOLD')
        return token
    except Hold: raise
    except Exception: raise Hold('TOKEN_REQUEST_HOLD')

def issue_claim():
    try:
        fd=os.open(CLAIM,os.O_WRONLY|os.O_CREAT|os.O_EXCL|getattr(os,'O_NOFOLLOW',0),0o600)
        with os.fdopen(fd,'w') as file:
            file.write('CATALOG_ONE_SHOT_CONSUMED; DO_NOT_RETRY\n')
            file.flush(); os.fsync(file.fileno())
        print('CATALOG_ATTEMPT=CONSUMED',flush=True)
    except Exception: raise Hold('CLAIM_HOLD')

def run():
    cmd,driver,parsed=validate()
    body=retrieve_catalog()
    issue_claim()
    work=Path(tempfile.mkdtemp(prefix='fv-cat-one-',dir='/tmp'))
    os.chmod(work,0o700)
    tok=work/'access-token.utf16le'; sql=work/'catalog.sql'; ini=work/'odbcinst.ini'
    try:
        token=request_one_token(parsed)
        fd=os.open(tok,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(fd,'wb') as file: file.write(token.encode('utf-16le'));file.flush();os.fsync(file.fileno())
        del token
        sql.write_bytes(body);os.chmod(sql,0o600)
        ini.write_text('[ODBC Driver 18 for SQL Server]\nDescription=Microsoft SQL Server ODBC 18\nDriver='+str(driver)+'\nUsageCount=1\n')
        os.chmod(ini,0o600)
        lib=ROOT/'usr/lib/x86_64-linux-gnu'
        env={k:v for k,v in os.environ.items() if not k.startswith(('SQLCMD','OSQL','AZURE_')) and k not in ('IDENTITY_HEADER','IDENTITY_ENDPOINT','MSI_SECRET','MSI_ENDPOINT')}
        env.update({'ODBCSYSINI':str(work),'ODBCINSTINI':'odbcinst.ini','LD_LIBRARY_PATH':str(lib)+':'+str(driver.parent)})
        argv=[str(cmd),'-S',SERVER,'-d',DB,'-G','-P',str(tok),'-Nm','-b','-l','30','-t','60','-h','-1','-W','-w','4096','-i',str(sql)]
        print('SQLCMD_DISPATCH=ONE',flush=True)
        try:
            p=subprocess.run(argv,capture_output=True,timeout=95,env=env,cwd=str(work))
        except subprocess.TimeoutExpired: raise Hold('SQL_TIMEOUT_UNKNOWN')
        if p.returncode!=0:
            # No raw stdout/stderr/identity/SQL error printed or stored.
            print('SQLCMD_EXIT=' + safe_sqlcmd_exitcode(p.returncode), flush=True)
            print('SQLCMD_FAILURE_CLASS=' + categorize_sqlcmd_failure(p.stdout,p.stderr), flush=True)
            raise Hold('SQLCMD_NONZERO_EXIT_HOLD')
        lines=p.stdout.decode('utf-8','replace').splitlines()
        rows=[s.strip() for s in lines if s.strip().startswith('FV_CAT|')]
        if len(rows)!=1 or not CODE.fullmatch(rows[0]): raise Hold('CATALOG_RECEIPT_UNKNOWN')
        print('CATALOG_RECEIPT='+rows[0],flush=True)
        print('SQL_CALL_COUNT=1_ATTEMPTED',flush=True)
        print('CATALOG_QUERY=COMPLETE',flush=True)
        return 0
    finally:
        for p in (tok,sql,ini):
            try: p.unlink(missing_ok=True)
            except Exception: pass
        try: work.rmdir()
        except Exception: pass

def main():
    try: return run()
    except Hold as err:
        print('CATALOG_QUERY=HOLD',flush=True)
        print('STOP_CLASS='+str(err),flush=True)
        print('SQL_CALL_COUNT=0_OR_1_UNKNOWN',flush=True)
        print('AUTO_RETRY=FORBIDDEN',flush=True)
        return 1
    except Exception:
        print('CATALOG_QUERY=HOLD',flush=True)
        print('STOP_CLASS=UNEXPECTED_HOLD',flush=True)
        print('SQL_CALL_COUNT=0_OR_1_UNKNOWN',flush=True)
        print('AUTO_RETRY=FORBIDDEN',flush=True)
        return 1
if __name__=='__main__':sys.exit(main())
