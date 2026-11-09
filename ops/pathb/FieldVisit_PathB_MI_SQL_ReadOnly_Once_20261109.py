#!/usr/bin/env python3
"""One authorized MI Azure SQL metadata SELECT; never rerun. App SSH only.
No installer, marker, schema writes, SQL permission modifications or secrets in receipt.
"""
import hashlib
import json
import os
import pathlib
import signal
import stat
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request

BASE=pathlib.Path('/tmp/fv-pathb-p3-kudu-install-d6c2208-v3-work/tools')
SQLCMD=BASE/'opt/mssql-tools18/bin/sqlcmd'
DRIVER=BASE/'opt/microsoft/msodbcsql18/lib64/libmsodbcsql-18.6.so.1.1'
EXPECTED={SQLCMD:'4234f2b7db28e68b3f216712ab9fb016229823c111e008dc97749a8ae9190609',DRIVER:'bfc10e264cf1b065485a472bb8186a57566a0b98cbf6712bf2fad48b6a9f369c'}
CLAIM=pathlib.Path('/home/LogFiles/fv-pathb-mi-sql-readonly-once-20261109.claim')
RECEIPT=pathlib.Path('/home/LogFiles/fv-pathb-mi-sql-readonly-once-20261109.receipt')
SERVER='sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DATABASE='db-fieldvisit-pathb-uat'
SQL=b"SET NOCOUNT ON; SELECT 'FV_TABLES|' + CONVERT(varchar(12), COUNT(*)) FROM sys.tables WHERE is_ms_shipped = 0;\nGO\n"

def emit(state):
    # Best-effort receipt; no secrets, no source exceptions
    output='MI_SQL_READONLY='+state+'\nINSTALLER=NOT_RUN\nDDL=NO\nRETRY=FORBIDDEN\n'
    print(output,flush=True)
    try:
        fd=os.open(str(RECEIPT),os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(fd,'w') as f: f.write(output)
    except OSError: pass

def digest(p):
    with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()

# Refuse if any receipt/claim exists. Write claim BEFORE MI token or SQL. No replay.
if CLAIM.exists() or RECEIPT.exists():
    print('MI_SQL_READONLY=HOLD_ALREADY_ATTEMPTED\nRETRY=FORBIDDEN',flush=True)
    sys.exit(1)
try:
    for p,expected in EXPECTED.items():
        st=p.lstat()
        if not stat.S_ISREG(st.st_mode) or digest(p)!=expected:
            raise RuntimeError('TOOL_HOLD')
    if not (os.environ.get('IDENTITY_ENDPOINT') and os.environ.get('IDENTITY_HEADER')):
        raise RuntimeError('MI_HOLD')
    endpoint=os.environ['IDENTITY_ENDPOINT']
    parsed=urllib.parse.urlsplit(endpoint)
    # App Service managed identity endpoint expected loopback; reject any external host.
    if parsed.scheme!='http' or parsed.hostname not in ('127.0.0.1','localhost','::1') or parsed.username or parsed.password:
        raise RuntimeError('MI_ENDPOINT_HOLD')
    # Atomic exclusive claim, never remove, before token request.
    fd=os.open(str(CLAIM),os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
    with os.fdopen(fd,'w') as f:f.write('CONSUMED: single MI token request and one SQL metadata SELECT max\n')
except Exception:
    emit('HOLD_PRECHECK')
    sys.exit(1)

private=None
try:
    q=urllib.parse.parse_qsl(parsed.query,keep_blank_values=True)
    q=[(k,v) for k,v in q if k not in ('api-version','resource')]
    q.extend((('api-version','2019-08-01'),('resource','https://database.windows.net/')))
    url=urllib.parse.urlunsplit((parsed.scheme,parsed.netloc,parsed.path,urllib.parse.urlencode(q),''))
    req=urllib.request.Request(url,headers={'X-IDENTITY-HEADER':os.environ['IDENTITY_HEADER']})
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req,timeout=12) as resp:
        token=json.load(resp).get('access_token')
    if not isinstance(token,str) or not (100<=len(token)<=15000) or '\n' in token:
        raise RuntimeError('TOKEN_HOLD')
    private=pathlib.Path(tempfile.mkdtemp(prefix='fv-mi-readonly-',dir='/dev/shm'))
    os.chmod(private,0o700)
    tokenfile=private/'token.utf16le'
    fd=os.open(str(tokenfile),os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'wb') as f:f.write(token.encode('utf-16le'))
    del token
    odbc=private/'odbc';odbc.mkdir(mode=0o700)
    (odbc/'odbcinst.ini').write_text('[ODBC Driver 18 for SQL Server]\nDriver='+str(DRIVER)+'\nUsageCount=1\n',encoding='ascii')
    (odbc/'odbc.ini').write_text('',encoding='ascii')
    env={'PATH':'/usr/bin:/bin','HOME':str(private),'LANG':'C.UTF-8','LC_ALL':'C.UTF-8',
         'ODBCSYSINI':str(odbc),'ODBCINI':'odbc.ini','ODBCINSTINI':'odbcinst.ini',
         'LD_LIBRARY_PATH':':'.join((str(BASE/'opt/microsoft/msodbcsql18/lib64'),str(BASE/'usr/lib/x86_64-linux-gnu')))}
    args=[str(SQLCMD),'-S',SERVER,'-d',DATABASE,'-I','-b','-l','10','-N','-G','-P',str(tokenfile),'-Nm','-h','-1','-W','-w','65535','-s','|']
    proc=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env,start_new_session=True)
    try:
        stdout,stderr=proc.communicate(SQL,timeout=35)
    except subprocess.TimeoutExpired:
        os.killpg(proc.pid,signal.SIGKILL);proc.communicate()
        raise RuntimeError('SQL_TIMEOUT')
    # Never print stdout/stderr; strict one-line acceptance.
    lines=[x.strip() for x in stdout.decode('utf-8','replace').splitlines() if x.strip()]
    if proc.returncode==0 and len(lines)==1 and lines[0].startswith('FV_TABLES|') and lines[0][10:].isdigit():
        val=int(lines[0][10:])
        if 0<=val<=100000:
            emit('PASS_TABLES_'+str(val))
        else:emit('HOLD_SQL_RESULT')
    else:emit('HOLD_SQL_AUTH_OR_QUERY')
except Exception:
    emit('HOLD_MI_OR_SQL')
finally:
    if private:
        for item in ('token.utf16le','odbc/odbcinst.ini','odbc/odbc.ini'):
            p=private/item
            try:p.unlink(missing_ok=True)
            except OSError:pass
        try:(private/'odbc').rmdir()
        except OSError:pass
        try:private.rmdir()
        except OSError:pass
