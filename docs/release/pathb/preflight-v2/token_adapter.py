#!/usr/bin/env python3
"""NONDEPLOY candidate: strict per-invocation ODBC adapter. Not an approved operator.
Outer supervisor must bind config/source hashes, one-use authorization, token lifetime,
package integrity, total deadline, cleanup and independent verification.
"""
import fcntl, hashlib, json, os, re, signal, stat, subprocess, sys, time
from pathlib import Path

SERVER='sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DB='db-fieldvisit-pathb-uat'
EXPECTED=['-S',SERVER,'-d',DB,'-I','-b','-l','10','-N','-G']
def sha(raw): return hashlib.sha256(raw).hexdigest()
def private_file(path):
    p=Path(path); s=p.lstat()
    if not stat.S_ISREG(s.st_mode) or s.st_uid!=os.geteuid() or stat.S_IMODE(s.st_mode)!=0o600:
        raise ValueError('PRIVATE_FILE_REQUIRED')
    return p
def classify(raw):
    # No raw strings, identity, SQL, token or arbitrary numeric fragments survive.
    states=sorted(set(re.findall(r'\b(?:01000|08001|08S01|28000|HYT00|HYT01|42000)\b',raw)))
    native=sorted(set(re.findall(r'\b(?:18456|40613|4060|47073|40532|10928|10929|56611|56612|56613)\b',raw)))
    category=('SQL_AUTHENTICATION' if '18456' in native else
              'SQL_TIMEOUT' if any(x in states for x in ['HYT00','HYT01']) else
              'SQL_CONNECTION_OR_QUERY')
    return {'sqlstate':states,'native':native,'category':category}
def invoke(config, argv, raw):
    if argv!=EXPECTED: raise ValueError('EXACT_ARGV_REQUIRED')
    if time.time()>=config['deadline']: raise ValueError('DEADLINE_EXPIRED')
    if any(os.environ.get(k) for k in ['SQLCMDUSER','SQLCMDPASSWORD','OSQLUSER','OSQLPASSWORD','SQLCMDINI']):
        raise ValueError('AMBIENT_SQL_AUTH_OR_STARTUP_REJECTED')
    token=private_file(config['token_file'])
    binary=Path(config['binary'])
    if binary.is_symlink() or sha(binary.read_bytes())!=config['binary_sha256']:
        raise ValueError('BINARY_HASH_REQUIRED')
    # State initialized by an external single-use supervisor, never auto-created here.
    state=private_file(config['state_file'])
    with state.open('r+') as f:
        fcntl.flock(f,fcntl.LOCK_EX|fcntl.LOCK_NB)
        data=json.load(f); i=data['next']
        if data.get('terminal') or i>=len(config['payload_sha256']): raise ValueError('REPLAY_OR_TERMINAL')
        if sha(raw)!=config['payload_sha256'][i]: raise ValueError('SQL_INPUT_HASH_REQUIRED')
        def save():
            f.seek(0);json.dump(data,f);f.truncate();f.flush();os.fsync(f.fileno())
        data.update(next=i+1,terminal=True,inflight=i);save() # crash consumes attempt
        args=[str(binary),*argv[:-2],'-Nm','-G','-P',str(token)]
        env={k:v for k,v in os.environ.items() if not k.startswith(('SQLCMD','OSQL','AZURE_'))
             and k not in ['IDENTITY_ENDPOINT','IDENTITY_HEADER','MSI_ENDPOINT','MSI_SECRET']}
        # Dedicated ODBC registration/LD_LIBRARY_PATH are supplied by outer operator.
        limit=min(110,float(config['call_timeout']),config['deadline']-time.time())
        if limit<=0: raise ValueError('DEADLINE_EXPIRED')
        p=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,
                           stderr=subprocess.PIPE,env=env,start_new_session=True)
        record={'index':i,'input_sha256':sha(raw),'child_exit':None,'timeout':False}
        try:
            out,err=p.communicate(raw,timeout=limit)
            record['child_exit']=p.returncode
            # Successful output intentionally not published. Schema fingerprint needs
            # a separately authorized fixed readback, not raw installer output.
            if p.returncode:
                try: record.update(classify((out+err).decode('utf-8','replace')))
                except Exception: record['category']='CLASSIFIER_FAILED_ORIGINAL_EXIT_PRESERVED'
            rc=p.returncode if 0<=p.returncode<=255 else 1
        except subprocess.TimeoutExpired:
            os.killpg(p.pid,signal.SIGKILL);p.communicate()
            record.update(timeout=True,child_exit=p.returncode,category='PROCESS_TIMEOUT')
            rc=124
        data.pop('inflight',None);data['records'].append(record)
        data['terminal']=rc!=0 or data['next']==len(config['payload_sha256']);save()
        return rc
def main():
    try:
        config=json.loads(private_file(os.environ['FV_ADAPTER_CONFIG']).read_text())
        return invoke(config,sys.argv[1:],sys.stdin.buffer.read())
    except Exception:
        # Original installer still sees a nonzero return; no unredacted traceback.
        return 125
if __name__=='__main__': sys.exit(main())
