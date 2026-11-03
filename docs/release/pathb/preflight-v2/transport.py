#!/usr/bin/env python3
"""Cloud Shell parent. Default NONDEPLOY; execute requires NEW exact Owner approval.
Official az webapp exec only. Tokens captured privately, never in argv/log.
"""
import argparse,base64,hashlib,io,json,os,pty,re,select,signal,subprocess,sys,termios,textwrap,time,zipfile
from pathlib import Path
R=Path(__file__).resolve().parent
REMOTE='/tmp/fv-pathb-control-d6c2208-once1'
def sha(b):return hashlib.sha256(b).hexdigest()
def az(args):
    p=subprocess.run(['az',*args,'-o','json','--only-show-errors'],capture_output=True,timeout=45)
    if p.returncode:raise ValueError('AZURE_READ_OR_TOKEN_HOLD')
    return json.loads(p.stdout)
class Session:
    def __init__(self,deadline):
        self.deadline=deadline;self.buf=b'';self.secrets=[]
        self.fd,slave=pty.openpty();attrs=termios.tcgetattr(slave);attrs[3]&=~termios.ECHO;termios.tcsetattr(slave,termios.TCSANOW,attrs)
        self.p=subprocess.Popen(['az','webapp','exec','-g','rg-fieldvisit-uat','-n','api-fieldvisit-pathb-uat'],stdin=slave,stdout=slave,stderr=slave,start_new_session=True);os.close(slave)
    def send(self,b):
        offset=0
        while offset<len(b):
            if time.monotonic()>=self.deadline:raise ValueError('MANAGEMENT_SEND_DEADLINE')
            if select.select([], [self.fd], [], .2)[1]:offset+=os.write(self.fd,b[offset:offset+2048])
    def expect(self,pattern,seconds=60):
        end=min(self.deadline,time.monotonic()+seconds);rx=re.compile(pattern)
        while time.monotonic()<end:
            if any(s in self.buf for s in self.secrets):raise ValueError('SECRET_ECHO_HOLD')
            m=rx.search(self.buf)
            if m:
                out=m.group(0);self.buf=self.buf[m.end():];return out
            if select.select([self.fd],[],[],min(.2,max(0,end-time.monotonic())))[0]:
                b=os.read(self.fd,65536)
                if not b:raise ValueError('MANAGEMENT_CHANNEL_EOF')
                self.buf+=b
                if len(self.buf)>2000000:raise ValueError('CHANNEL_OUTPUT_BOUND')
            elif self.p.poll() is not None:raise ValueError('MANAGEMENT_CHANNEL_CLOSED')
        raise ValueError('MANAGEMENT_CHANNEL_TIMEOUT')
    def close(self):
        try:self.send(b'\x03\nexit\n')
        except (OSError,ValueError):pass
        try:self.p.wait(timeout=3)
        except subprocess.TimeoutExpired:os.killpg(self.p.pid,signal.SIGKILL);self.p.wait()
        os.close(self.fd);self.buf=b'';self.secrets=[]
def management(c):
    a=az(['account','show'])
    if sha(a['id'].encode())!=c['subscription_sha256'] or sha(a['tenantId'].encode())!=c['tenant_sha256']:raise ValueError('ACCOUNT_MISMATCH')
    s=az(['sql','server','show','-g','rg-fieldvisit-uat','-n','sql-fieldvisit-jpe-pathb-uat'])
    admins=az(['sql','server','ad-admin','list','-g','rg-fieldvisit-uat','-s','sql-fieldvisit-jpe-pathb-uat'])
    rules=az(['sql','server','firewall-rule','list','-g','rg-fieldvisit-uat','-s','sql-fieldvisit-jpe-pathb-uat'])
    app=az(['webapp','show','-g','rg-fieldvisit-uat','-n',c['app']])
    if not app.get('virtualNetworkSubnetId') or app.get('state')!='Running':raise ValueError('EXISTING_APP_PRIVATE_ROUTE_REQUIRED')
    f={'server':s['fullyQualifiedDomainName'],'database':c['database'],'app':app['name'],'pna':s['publicNetworkAccess'],'firewall_count':len(rules),'minimum_tls':s['minimalTlsVersion'],'owner_admin_match':any(sha(str(x.get('sid','')).lower().encode())==c['owner_oid_sha256'] for x in admins),'private_sql_ip':'10.90.2.4','observed_at':time.time()}
    if f['server']!=c['server'] or f['pna']!='Disabled' or f['firewall_count']!=0 or f['minimum_tls']!='1.2' or not f['owner_admin_match']:raise ValueError('MANAGEMENT_PRIVATE_OWNER_HOLD')
    return f
def transfer_payload(package,packages,c):
    memory=io.BytesIO()
    with zipfile.ZipFile(memory,'w',zipfile.ZIP_DEFLATED) as t:
        for n in ['supervisor.py','cleanup.py','transport.py','token_adapter.py','seal_verifier.py','preflight.sql','config.json','postcheck.sql','schema-fingerprint.sql']:t.write(R/n,n)
        t.write(package,'original.zip')
        for x in c['tools']['packages']:t.write(Path(packages)/(x['name']+'.deb'),'debs/'+x['name']+'.deb')
    return base64.b64encode(memory.getvalue()).decode()
def bootstrap_source(payload):
    # Short physical lines avoid terminal canonical-line truncation; no shell interpolation.
    digest=sha(base64.b64decode(payload))
    return "import base64,io,zipfile,hashlib\nfrom pathlib import Path\np=Path('"+REMOTE+"');p.mkdir(mode=0o700)\nprint('FV_TRANSFER_CLAIMED',flush=True)\nb='''"+'\n'.join(textwrap.wrap(payload,76))+"'''\nd=base64.b64decode(b);assert hashlib.sha256(d).hexdigest()=='"+digest+"'\nwith zipfile.ZipFile(io.BytesIO(d)) as t:\n for n in t.namelist():\n  q=Path(n);assert not q.is_absolute() and '..' not in q.parts\n  f=p/q;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(t.read(n))\nprint('FV_TRANSFER_READY',flush=True)\n"
def independent_cleanup(deadline):
    s=Session(deadline)
    try:
        s.expect(rb'(?m)^root@[^\r\n]*#[ ]*',45)
        source=(R/'cleanup.py').read_text()
        proof="d={'__name__':'independent_cleanup'}\nexec("+repr(source)+",d)\nd['cleanup']()\nraise SystemExit(int(d['ROOT'].exists() or d['ROOT'].is_symlink() or d['RAM'].exists() or d['RAM'].is_symlink() or bool(d['owned_processes']())))\n"
        encoded='\n'.join(textwrap.wrap(base64.b64encode(proof.encode()).decode(),76))
        command="python3 - <<'FVCLEAN'\nimport base64\nb='''"+encoded+"'''\nexec(base64.b64decode(b))\nFVCLEAN\necho FV_CLEANUP_RC=$?\n"
        s.send(command.encode())
        line=s.expect(rb'(?m)^FV_CLEANUP_RC=[0-9]+',45)
        if line!=b'FV_CLEANUP_RC=0':raise ValueError('INDEPENDENT_CLEANUP_HOLD')
    finally:s.close()
def final_private(c):
    s=az(['sql','server','show','-g','rg-fieldvisit-uat','-n','sql-fieldvisit-jpe-pathb-uat'])
    f=az(['sql','server','firewall-rule','list','-g','rg-fieldvisit-uat','-s','sql-fieldvisit-jpe-pathb-uat'])
    if s['fullyQualifiedDomainName']!=c['server'] or s['publicNetworkAccess']!='Disabled' or len(f)!=0:raise ValueError('FINAL_PRIVATE_ONLY_HOLD')
def execute(package,seal,packages,config_sha,approval,blob_name):
    from supervisor import load_config,package_inputs,token_claims
    from seal_verifier import verify
    c=load_config(config_sha)
    if approval!=config_sha:raise ValueError('OWNER_APPROVAL_REQUIRED')
    verify(package,seal,c,blob_name)
    package_inputs(package,c)
    for x in c['tools']['packages']:
        if sha((Path(packages)/(x['name']+'.deb')).read_bytes())!=x['sha256']:raise ValueError('PACKAGE_HASH')
    management(c)
    once=Path('/home/terry/.fieldvisit-operator');once.mkdir(mode=0o700,exist_ok=True)
    with (once/(c['run_id']+'.parent-once')).open('x') as f:f.write(config_sha)
    end=time.monotonic()+c['total_seconds'];s=None;token=None;staged=False;result=None
    try:
        s=Session(end-c['cleanup_seconds']);s.expect(rb'(?m)^root@[^\r\n]*#[ ]*',45)
        s.send(b"stty -echo; printf '\\nFV_ECHO_OFF\\n'\n");s.expect(rb'(?m)^FV_ECHO_OFF\r?\n',15)
        payload=transfer_payload(package,packages,c)
        s.send(("python3 - <<'FVBOOT'\n"+bootstrap_source(payload)+"FVBOOT\n").encode())
        s.expect(rb'(?m)^FV_TRANSFER_CLAIMED\r?\n',180);staged=True
        s.expect(rb'(?m)^FV_TRANSFER_READY\r?\n',180)
        command='python3 '+REMOTE+'/supervisor.py --package '+REMOTE+'/original.zip --packages '+REMOTE+'/debs --config-sha '+config_sha+' --execute --owner-approval '+approval+'\n'
        s.send(command.encode());s.expect(rb'MANAGEMENT_REFRESH_REQUIRED\r?\n',300)
        s.send(json.dumps(management(c)).encode()+b'\n')
        s.expect(rb'DUMMY_NO_ECHO:',30);dummy=b'FV-DUMMY-NOT-A-CREDENTIAL';s.secrets.append(dummy);s.send(dummy+b'\n')
        ack=s.expect(rb'DUMMY_SHA256=[0-9a-f]{64}',30)
        if ack!=('DUMMY_SHA256='+sha(dummy)).encode():raise ValueError('DUMMY_TRANSPORT_HOLD')
        s.expect(rb'OWNER_SQL_TOKEN:',30)
        # The single FUTURE token acquisition. No login/device-code/refresh retry.
        t=az(['account','get-access-token','--resource','https://database.windows.net/']);token=t['accessToken'];t=None
        token_claims(token,c,time.time())
        if len(token.encode())>4000:raise ValueError('TOKEN_EXCEEDS_PTY_CANONICAL_BOUND')
        s.secrets.append(token.encode());s.send(token.encode()+b'\n')
        line=s.expect(rb'FV_FINAL_RESULT=\{[^\r\n]+\}',c['sql_seconds']+60)
        token=None;result=json.loads(line.split(b'=',1)[1]);print(json.dumps(result))
    finally:
        token=None
        if s:s.close()
        if staged:
            independent_cleanup(end)
            clean=Session(end)
            try:
                clean.expect(rb'(?m)^root@[^\r\n]*#[ ]*',45)
                clean.send(("python3 -c \"import shutil;from pathlib import Path;p=Path('"+REMOTE+"');shutil.rmtree(p);print('FV_CONTROL_COPY_ABSENT='+str(not p.exists()))\"\n").encode())
                clean.expect(rb'(?m)^FV_CONTROL_COPY_ABSENT=True',45)
            finally:clean.close()
            final_private(c);print('INDEPENDENT_CLEANUP=PASS;FINAL_PRIVATE_ONLY=PASS')
    return 0 if result and result['status'].endswith('_PASS') and result['independent_local_cleanup_exit']==0 else 1
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--package',required=True);p.add_argument('--seal');p.add_argument('--blob-object-name');p.add_argument('--packages',required=True);p.add_argument('--config-sha',required=True);p.add_argument('--execute',action='store_true');p.add_argument('--owner-approval');a=p.parse_args()
    if not a.execute:print('TRANSPORT_NONDEPLOY_NO_AZURE_NO_TOKEN');sys.exit(0)
    try:sys.exit(execute(a.package,a.seal,a.packages,a.config_sha,a.owner_approval,a.blob_object_name))
    except Exception:print('TRANSPORT=HOLD_DETAILS_SUPPRESSED');sys.exit(1)
