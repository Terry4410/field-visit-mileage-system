"""Staged main-container adapter. NO command-line or workflow Live entry point.

The caller must independently validate the exact-source Owner approval and
content-addressed release plan BEFORE constructing this adapter. Its transport
is a new privileged operation, not part of the historical five-GET authorization.
All incoming PTY text stays private; only strict nonce-bound records escape.
"""
import base64
import hashlib
import json
import re
import shlex
import ssl
import time
from urllib.parse import urlsplit

from main_probe import DLLS, Stop, digest


def scm_url(site, instances):
    if site.get('name')!='api-fieldvisit-uat' or site.get('properties',{}).get('state')!='Running':raise Stop()
    if instances.get('nextLink') or len(instances.get('value',[]))!=1:raise Stop()
    instance=instances['value'][0].get('name','')
    if not re.fullmatch('[a-zA-Z0-9]{16,128}',instance):raise Stop()
    hosts={h.get('name') for h in site.get('properties',{}).get('hostNameSslStates',[])
           if h.get('hostType')=='Repository'}
    if len(hosts)!=1:raise Stop()
    host=hosts.pop()
    if not isinstance(host,str) or not re.fullmatch(r'api-fieldvisit-uat(?:-[a-z0-9]+)?\.scm\.(?:[a-z0-9-]+\.)?azurewebsites\.net',host):raise Stop()
    return 'wss://'+host+'/exec/shell?shell=/bin/sh',instance


def file_manifest(manifest):
    if not isinstance(manifest,dict) or not 1<=len(manifest)<=80:raise Stop()
    for path,sha in manifest.items():
        if (not re.fullmatch(r'/[A-Za-z0-9_./-]+',path) or '//' in path or
            any(p in ('.','..') for p in path.split('/')[1:]) or
            not isinstance(sha,str) or not re.fullmatch('[a-f0-9]{64}',sha)):raise Stop()
    if '/index.html' not in manifest:raise Stop()
    return manifest


def pre_sql(observation,plan,read_frontend,recovery,now):
    """Real measurements checked against a separately approved immutable plan.

    read_frontend(path) must enforce the approved exact HTTPS origin, GET-only,
    TLS, no redirect/retry/cookies/auth, <=80 requests, <=8MiB/file and <=120s.
    Recovery is management-plane metadata, NOT a claim of successful restore.
    """
    if set(plan)!={'nonce','apiAssemblies','apiCompositeSha256','sourcePolicySha256',
                   'frontendFiles','frontendOrigin','rollbackBundleSha256','restoreUtc',
                   'schemaSha256','rollbackAvailableUntil','frontendOffFile'}:raise Stop()
    for key in ('sourcePolicySha256','rollbackBundleSha256','schemaSha256'):
        if not isinstance(plan[key],str) or not re.fullmatch('[a-f0-9]{64}',plan[key]):raise Stop()
    if not re.fullmatch('[a-f0-9]{64}',plan['nonce']):raise Stop()
    origin=urlsplit(plan['frontendOrigin'])
    if (origin.scheme!='https' or not origin.hostname or origin.username or origin.password or
        origin.port not in (None,443) or origin.path or origin.query or origin.fragment):raise Stop()
    expected=plan['apiAssemblies']
    if set(expected)!=set(DLLS) or any(not re.fullmatch('[a-f0-9]{64}',h) for h in expected.values()):raise Stop()
    composite=digest(''.join(n+':'+expected[n]+'\n' for n in DLLS).encode())
    if (plan['apiCompositeSha256']!=composite or observation.get('kind')!='G1_OBSERVATION' or
        observation.get('nonce')!=plan['nonce'] or observation.get('sqlConnections')!=0 or
        observation.get('configuration')!='STARTUP_BOUND' or observation.get('flags')!='MATCH' or
        observation.get('runtime')!={'assemblies':expected,'apiCompositeSha256':composite} or
        observation.get('authentication') not in ('API_SQL_LOGIN','API_MANAGED_IDENTITY')):raise Stop()
    if (type(plan['restoreUtc']) is not int or type(plan['rollbackAvailableUntil']) is not int or
        set(recovery)!={'earliestUtc','observedUtc','retentionDays','continuous'} or
        any(type(recovery[k]) is not int for k in ('earliestUtc','observedUtc','retentionDays')) or
        recovery['continuous'] is not True or not 1<=recovery['retentionDays']<=35 or
        not 0<=now-recovery['observedUtc']<=120 or
        not max(recovery['earliestUtc'],now-86400*recovery['retentionDays'])<=plan['restoreUtc']<=now-3600 or
        plan['rollbackAvailableUntil']<=now+86400):raise Stop()
    manifest=file_manifest(plan['frontendFiles'])
    off=plan['frontendOffFile']
    if off not in manifest:raise Stop()
    # The independently reviewed manifest must bind a known source contract
    # whose immutable file asserts safe OFF; arbitrary runtime JSON is not proof.
    measured={}
    for path,expected_hash in manifest.items():
        content=read_frontend(path)
        if not isinstance(content,bytes) or len(content)>8388608:raise Stop()
        measured[path]=digest(content)
        if measured[path]!=expected_hash:raise Stop()
    return {'nonce':plan['nonce'],'runtime':'MATCH','flags':'MATCH','recovery':'MATCH',
            'frontendManifestSha256':digest(json.dumps(measured,sort_keys=True,separators=(',',':')).encode())}


class MainSession:
    """One WSS connection, no retry, <=480s including pre-SQL evidence work."""
    def __init__(self,site,instances,token,source,probe,probe_sha,nonce,driver_hashes,runtimeconfig_sha,connect,clock=time.monotonic):
        if digest(probe)!=probe_sha or len(probe)>1048576 or len(source)>65536:raise Stop()
        self.open(site,instances,token,nonce,connect,clock)
        try:
            code=source.decode('utf-8')+'\nraise SystemExit(main('+repr(probe_sha)+'))\n'
            self.send('exec /usr/bin/python3 -I -B -c '+shlex.quote(code)+'\n')
            self.send(json.dumps({'nonce':nonce,'probeBase64':base64.b64encode(probe).decode(),
                                 'driverHashes':driver_hashes,'runtimeconfigSha256':runtimeconfig_sha})+'\n')
        except Exception:
            self.close();raise Stop() from None

    def open(self,site,instances,token,nonce,connect,clock):
        self.clock=clock;self.end=clock()+480;self.total=0;self.buffer=b'';self.ws=None
        self.nonce=nonce;self.go_sent=False;self.finished=False
        url,instance=scm_url(site,instances)
        if not re.fullmatch('[a-f0-9]{64}',nonce):raise Stop()
        # Factory is websocket.create_connection from pinned websocket-client.
        # Exceptions/HTTP bodies are never printed; no CLI discovery or Basic auth.
        try:
            self.ws=connect(url,header={'Authorization':'Bearer '+token},cookie='ARRAffinity='+instance,
                timeout=30,redirect_limit=0,sslopt={'cert_reqs':ssl.CERT_REQUIRED,'check_hostname':True},
                enable_multithread=False)
            # websocket-client1.8 may return a 3xx handshake with redirect_limit0.
            # Deny it before sending ANY shell command; never follow Location.
            if self.ws.getstatus()!=101:raise Stop()
            self.send("PS1= PS2=; unset HISTFILE; stty -echo -icanon && printf '\\nG1_READY_%s\\n' "+nonce+" || exit 1\n")
            until=self.clock()+30
            while self.line(max(0,until-self.clock()))!=('G1_READY_'+nonce).encode():pass
        except Exception:
            self.close();raise Stop() from None

    def send(self,value):
        if self.ws is None or self.clock()>=self.end:raise Stop()
        # Raw shell input uses binary frames; terminal control JSON uses text.
        self.ws.send(value.encode(),opcode=2)

    def line(self,seconds):
        until=min(self.end,self.clock()+seconds)
        while b'\n' not in self.buffer:
            if self.clock()>=until:raise Stop()
            self.ws.settimeout(max(.01,until-self.clock()))
            kind,data=self.ws.recv_data()
            if kind not in (1,2):raise Stop()
            if isinstance(data,str):data=data.encode()
            self.total+=len(data)
            if self.total>2097152 or len(self.buffer)+len(data)>1048576:raise Stop()
            self.buffer+=data
        line,self.buffer=self.buffer.split(b'\n',1)
        return line.rstrip(b'\r')

    def record(self,kind,seconds):
        until=min(self.end,self.clock()+seconds)
        while self.clock()<until:
            line=self.line(until-self.clock())
            try:value=json.loads(line)
            except (ValueError,UnicodeError):continue
            if not isinstance(value,dict) or value.get('nonce')!=self.nonce:continue
            if value.get('kind')!=kind:raise Stop()
            # Do not forward unreviewed fields, even if nonce is valid.
            shapes={'G1_OBSERVATION':{'kind','nonce','runtime','flags','authentication','sqlConnections','configuration'},
                    'G1_RESULT':{'kind','nonce','processStable','apiPrincipal','schema','principalSidSha256','actualSchemaSha256','sqlConnections'},
                    'G1_DISCOVERY':{'kind','nonce','runtime','fixedFiles','processStable','sqlConnections','configuration'}}
            keys=shapes[kind]
            if set(value)!=keys:raise Stop()
            return value
        raise Stop()

    def observe(self):return self.record('G1_OBSERVATION',45)

    def sql_once(self,observation,plan,read_frontend,recovery,now):
        if self.go_sent or self.finished:raise Stop()
        receipt=pre_sql(observation,plan,read_frontend,recovery,now)
        if receipt['nonce']!=self.nonce:raise Stop()
        self.go_sent=True  # Send failures still consume this session; no retry.
        self.send('G1_SQL_GO '+self.nonce+'\n')
        result=self.record('G1_RESULT',240);self.finished=True
        if (result['processStable'] is not True or result['sqlConnections']!=1 or
            result['apiPrincipal']!='MATCH' or result['schema']!='MATCH' or
            result['actualSchemaSha256']!=plan['schemaSha256'] or
            not isinstance(result['principalSidSha256'],str) or
            not re.fullmatch('[a-f0-9]{64}',result['principalSidSha256'])):raise Stop()
        return dict(liveG1='PASS',sqlConnections=1,**receipt,principalSidSha256=result['principalSidSha256'])

    def close(self):
        if self.ws is not None:
            try:self.ws.close(timeout=1)
            except Exception:pass
            self.ws=None


class DiscoverySession(MainSession):
    """D0 has no probe binary, selected credentials, identity token or SQL path."""
    def __init__(self,site,instances,token,source,nonce,connect,clock=time.monotonic):
        if len(source)>65536:raise Stop()
        self.open(site,instances,token,nonce,connect,clock)
        self.end=min(self.end,self.clock()+90)
        try:
            code=source.decode('utf-8')+'\nraise SystemExit(discover_main('+repr(nonce)+'))\n'
            # One physical input line: multiline quoted source creates PS2
            # prompts that can prefix the only JSON record on a real PTY.
            loader='import base64;exec(compile(base64.b64decode('+repr(base64.b64encode(code.encode()).decode())+'),"<reviewed-g1-d0>","exec"))'
            command="printf '\\n'; /usr/bin/python3 -I -B -c "+shlex.quote(loader)
            command+=' 2>/dev/null; result=$?; printf \'\\nG1_DONE_'+nonce+'_%s\\n\' "$result"; exit "$result"\n'
            self.send(command)
        except Exception:
            self.close();raise Stop() from None

    def observe(self):
        value=self.record('G1_DISCOVERY',45)
        if value['sqlConnections']!=0 or value['configuration']!='NOT_READ' or value['processStable'] is not True:raise Stop()
        runtime=value['runtime'];files=value['fixedFiles']
        if (not isinstance(runtime,dict) or set(runtime)!={'assemblies','apiCompositeSha256'} or
            not isinstance(runtime['assemblies'],dict) or set(runtime['assemblies'])!=set(DLLS) or
            not isinstance(files,dict) or set(files)!={'Microsoft.Data.SqlClient.dll','FieldVisit.Api.runtimeconfig.json'}):raise Stop()
        if any(not isinstance(h,str) or not re.fullmatch('[a-f0-9]{64}',h)
               for h in list(files.values())+list(runtime['assemblies'].values())):raise Stop()
        composite=digest(''.join(n+':'+runtime['assemblies'][n]+'\n' for n in DLLS).encode())
        if runtime['apiCompositeSha256']!=composite:raise Stop()
        # A valid JSON line alone is insufficient. Require the source-bound
        # process to finish with exit0 in the same session, never poll/reconnect.
        until=min(self.end,self.clock()+15)
        while True:
            done=self.line(max(0,until-self.clock()))
            if not done:continue
            if done!=('G1_DONE_'+self.nonce+'_0').encode():raise Stop()
            break
        return value

    def sql_once(self,*args,**kwargs):raise Stop()
