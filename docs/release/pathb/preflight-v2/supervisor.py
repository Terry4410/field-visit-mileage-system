#!/usr/bin/env python3
"""Final candidate; default offline inspection. --execute needs separate Owner approval.
Run only inside the existing Path B app via approved encrypted management transport.
No login/token acquisition, network configuration, privilege or deployment operations.
"""
import argparse,ast,base64,getpass,hashlib,json,os,re,shutil,signal,socket,stat,subprocess,sys,time,uuid,warnings,zipfile
from pathlib import Path
R=Path(__file__).resolve().parent
ROOT=Path('/tmp/fv-pathb-install-d6c2208-once1')
RAM=Path('/dev/shm/fv-pathb-install-d6c2208-once1')
MARKER=Path('/home/.fieldvisit-operator/pathb-p3-install-d6c2208-once1.once')
QUERY=(R/'preflight.sql').read_text()
def sha(b):return hashlib.sha256(b).hexdigest()
def require(ok,code):
    if not ok:raise ValueError(code)
def private_write(p,data):
    fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'wb') as f:f.write(data)
def load_config(expected):
    raw=(R/'config.json').read_bytes();require(sha(raw)==expected,'CONFIG_HASH')
    c=json.loads(raw)
    require(c['server']=='sql-fieldvisit-jpe-pathb-uat.database.windows.net' and c['database']=='db-fieldvisit-pathb-uat','TARGET')
    require(c['adapter_sha256']==sha((R/'token_adapter.py').read_bytes()),'ADAPTER_HASH')
    for name in ['supervisor.py','cleanup.py','transport.py','seal_verifier.py']:
        require(sha((R/name).read_bytes())==c['control_sha256'][name],'CONTROL_HASH')
    for name,digest in c['readback_sha256'].items():
        require(sha((R/name).read_bytes())==digest,'READBACK_HASH')
    require(sha(QUERY.encode())==c['preflight_sha256'],'PREFLIGHT_HASH')
    return c
def package_inputs(package,c):
    raw=Path(package).read_bytes();require(sha(raw)==c['package_sha256'],'ZIP_HASH')
    with zipfile.ZipFile(Path(package)) as z:
        manifest=z.read('standalone-install-manifest.json');require(sha(manifest)==c['manifest_sha256'],'MANIFEST_HASH')
        source=z.read('install.py');require(sha(source)==c['installer_sha256'],'INSTALLER_HASH')
        plan=json.loads(manifest);mode=plan['pathb-uat']
        require(plan['candidateSourceSha']==c['release'],'RELEASE')
        require((mode['server'],mode['database'],mode['schemaProfile'])==(c['server'],c['database'],'PathBNewEmpty'),'MODE')
        require([{'file':x['file'],'sha256':x['sha256']} for x in mode['sequence']]==c['layers'],'ORDER')
        tree=ast.parse(source)
        guards=[n.args[0].value for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name) and n.func.id=='run' and n.args and isinstance(n.args[0],ast.Constant)]
        pro=[n.value.value for n in ast.walk(tree) if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='prologue' for t in n.targets)][0]
        files={x['file']:z.read(x['file']) for x in c['layers']}
        require(all(sha(files[x['file']])==x['sha256'] for x in c['layers']),'LAYER_HASH')
        payloads=[guards[0],*[pro+files[x['file']] for x in c['layers']],guards[1]]
        require([sha(x) for x in payloads]==c['payload_sha256'] and len(payloads)==16,'PAYLOAD_HASH')
        return {'install.py':source,'standalone-install-manifest.json':manifest,**files}
def token_claims(token,c,now):
    claims=json.loads(base64.urlsafe_b64decode(token.split('.')[1]+'==='))
    require(sha(str(claims['oid']).lower().encode())==c['owner_oid_sha256'],'OWNER_TOKEN')
    require(sha(str(claims['tid']).lower().encode())==c['tenant_sha256'],'TENANT_TOKEN')
    require(claims['aud'].rstrip('/')=='https://database.windows.net','AUDIENCE')
    require(float(claims.get('nbf',0))<=now+30 and float(claims['exp'])-now>=c['minimum_token_ttl_seconds'],'TOKEN_TTL')
    return float(claims['exp'])
def ttl_gate(exp,now,sql_remaining,c,before_ddl=False):
    need=(16*120+2*c['child_seconds']+120 if before_ddl else sql_remaining)
    require(sql_remaining>=need,'SQL_BUDGET')
    require(exp-now>=need+c['cleanup_seconds']+c['expiry_buffer_seconds'],'TOKEN_REMAINING')
def preflight_row(out,c):
    rows=[x.strip().split('|') for x in out.decode('utf-8','strict').splitlines() if x.strip().startswith('FV_SQL|')]
    require(len(c['empty_metadata_fields'])==11 and
            len(rows)==1 and len(rows[0])==11+len(c['empty_metadata_fields']),
            'PREFLIGHT_SHAPE');v=[x.strip() for x in rows[0]]
    sid=bytes.fromhex(v[4]);require(len(sid)==16,'SID_SHAPE')
    ids=[str(uuid.UUID(bytes=sid)),str(uuid.UUID(bytes_le=sid))]
    require(any(sha(x.encode())==c['owner_oid_sha256'] for x in ids),'OWNER_SID')
    require(v[1]==c['server'] and v[2]==c['database'] and v[3]=='5','SQL_TARGET')
    require(v[5:8]==['1','1','0'],'VISIBILITY_EMPTY')
    require(v[8].upper()=='TRUE' and v[10].upper()=='TCP','TLS_TCP')
    require(all(re.fullmatch(r'0|[1-9][0-9]*',x) for x in v[11:]),'EMPTY_METADATA_UNKNOWN')
    require(all(x=='0' for x in v[11:]),'PARTIAL_SCHEMA_OR_MARKER')
    # v[9] intentionally NOT a release predicate per explicit Owner decision.
    return {'owner_sid':'PASS','token_identity':'PASS','tls':'PASS','target':'PASS',
            'visibility':'PASS','empty_db':'PASS','markers':'PASS','partial_schema':'PASS'}
def kill_owned():
    subprocess.run([sys.executable,str(R/'cleanup.py'),'--kill-only'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=20)
def child(args,env,deadline,raw=None,cap=110):
    require(time.monotonic()<deadline,'DEADLINE')
    p=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env,start_new_session=True)
    try:
        out,err=p.communicate(raw,timeout=min(cap,deadline-time.monotonic()))
        if p.returncode:
            # Adapter captures granular SQL errors; direct preflight retains safe codes.
            from token_adapter import classify
            record={'child_exit':p.returncode}
            try:record.update(classify((out+err).decode('utf-8','replace')))
            except Exception:record['category']='UNKNOWN_ORIGINAL_EXIT_PRESERVED'
            raise ChildFailure(record)
        return out
    except subprocess.TimeoutExpired:
        kill_owned()
        try:os.killpg(p.pid,signal.SIGKILL)
        except ProcessLookupError:pass
        p.communicate();raise ChildFailure({'child_exit':p.returncode,'category':'PROCESS_TIMEOUT'})
class ChildFailure(Exception):
    def __init__(self,record):self.record=record
def catalog_result(raw):
    text=raw.decode('utf-8','strict').replace('\r\n','').replace('\n','').strip()
    data=json.loads(text);require(isinstance(data,list) and len(data)==69,'CATALOG_TABLE_COUNT')
    triggers=0
    for table in data:
        for i in table.get('indexes') or []:
            require(not i['has_filter'] or i['filter_definition'] is not None,'INDEX_METADATA')
        for k in ['checks','foreignKeys']:
            require(all(not x['is_disabled'] and not x['is_not_trusted'] for x in table.get(k) or []),'CATALOG_CONSTRAINTS')
        for tr in table.get('triggers') or []:
            require(tr['definition'] is not None and not tr['is_disabled'],'TRIGGER_DEFINITION');triggers+=1
    return {'schema_sha256':sha(text.encode('utf-8')),'tables':69,'enabled_visible_triggers':triggers,'kind':'OBSERVED_POSTINSTALL_BASELINE_NOT_PREEXISTING_REFERENCE'}
def stage_tools(c,packages):
    for x in c['tools']['packages']:
        p=Path(packages)/(x['name']+'.deb');require(sha(p.read_bytes())==x['sha256'],'TOOL_PACKAGE_HASH')
        subprocess.run(['dpkg-deb','-x',str(p),str(ROOT/'tools')],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=30)
    for rel,digest in c['tools']['files'].items():require(sha((ROOT/'tools'/rel).read_bytes())==digest,'TOOL_BINARY_HASH')
    lib=list((ROOT/'tools/opt/microsoft/msodbcsql18/lib64').glob('libmsodbcsql-*.so.*'));require(len(lib)==1,'DRIVER')
    conf=ROOT/'odbc';conf.mkdir(mode=0o700)
    (conf/'odbcinst.ini').write_text('[ODBC Driver 18 for SQL Server]\nDriver='+str(lib[0])+'\nUsageCount=1\n')
    (conf/'odbc.ini').write_text('')
    env={'PATH':'/usr/bin:/bin','HOME':str(ROOT),'LANG':'C.UTF-8','LC_ALL':'C.UTF-8'}
    env.update(LD_LIBRARY_PATH=str(ROOT/'tools/usr/lib/x86_64-linux-gnu'),ODBCSYSINI=str(conf),ODBCINSTINI='odbcinst.ini',ODBCINI=str(conf/'odbc.ini'),ACCEPT_EULA='Y')
    binary=ROOT/'tools/opt/mssql-tools18/bin/sqlcmd'
    for p in [binary,lib[0]]:
        r=subprocess.run(['ldd',str(p)],capture_output=True,env=env,timeout=15);require(r.returncode==0 and b'not found' not in r.stdout+r.stderr,'TOOL_DEPENDENCIES')
    version=child([str(binary),'-?'],env,time.monotonic()+20,cap=15)
    require(b'18.6' in version,'TOOL_VERSION')
    return binary,env
def run(package,packages,management,config_sha,approval):
    c=load_config(config_sha);require(approval==config_sha,'EXACT_OWNER_APPROVAL_REQUIRED')
    # A management observation is supplied through the approved encrypted channel,
    # never fabricated by this process, and must be refreshed just before token handoff.
    inputs=package_inputs(package,c)
    MARKER.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
    require(not MARKER.parent.is_symlink(),'MARKER_PARENT')
    private_write(MARKER,json.dumps({'config_sha256':config_sha,'started':time.time()}).encode())
    result={'status':'HOLD','ddl_started':False,'sql_preflight_calls':0,'installer_invocations':0}
    started=time.monotonic();total_end=started+c['total_seconds'];watch=None;token=None;created=False
    try:
        require(not ROOT.exists() and not RAM.exists(),'STALE_TEMP_MATERIAL')
        ROOT.mkdir(mode=0o700);created=True;RAM.mkdir(mode=0o700)
        warnings.filterwarnings('error',category=getpass.GetPassWarning)
        for name,raw in inputs.items():
            p=ROOT/'package'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw)
        binary,env=stage_tools(c,packages)
        watch=subprocess.Popen([sys.executable,str(R/'cleanup.py'),'--watch',str(os.getpid()),str(time.monotonic()+min(c['sql_seconds']+c['cleanup_seconds'],total_end-time.monotonic()))],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
        # Parent updates this approved observation immediately before READY.
        print('MANAGEMENT_REFRESH_REQUIRED',flush=True)
        facts=json.loads(input()) # fresh parent GET observation over encrypted channel
        require(facts['server']==c['server'] and facts['database']==c['database'] and facts['app']==c['app'],'MANAGEMENT_TARGET')
        require(facts['pna']=='Disabled' and facts['firewall_count']==0 and facts['minimum_tls']=='1.2' and facts['owner_admin_match'] is True,'MANAGEMENT_PRIVATE_OWNER')
        require(0<=time.time()-facts['observed_at']<=60,'MANAGEMENT_FRESHNESS')
        ips={x[4][0] for x in socket.getaddrinfo(c['server'],1433,type=socket.SOCK_STREAM)}
        require(ips=={facts['private_sql_ip']},'PRIVATE_DNS')
        dummy=getpass.getpass('DUMMY_NO_ECHO:');print('DUMMY_SHA256='+sha(dummy.encode()),flush=True);dummy=None
        print('OWNER_TOKEN_READY',flush=True)
        token=getpass.getpass('OWNER_SQL_TOKEN:');exp=token_claims(token,c,time.time())
        sql_end=min(time.monotonic()+c['sql_seconds'],total_end-c['cleanup_seconds'])
        ttl_gate(exp,time.time(),sql_end-time.monotonic(),c)
        tokenfile=RAM/'owner-sql-token';private_write(tokenfile,token.encode('utf-16le'));token=None
        qargs=[str(binary),'-S',c['server'],'-d',c['database'],'-G','-P',str(tokenfile),'-I','-b','-l','10','-Nm','-h','-1','-W','-w','65535','-s','|','-Q',QUERY]
        result['sql_preflight_calls']=1
        result['preflight']=preflight_row(child(qargs,env,sql_end),c)
        child([sys.executable,str(ROOT/'package/install.py'),'--root',str(ROOT/'package'),'--target','pathb-uat'],env,sql_end,cap=30)
        ttl_gate(exp,time.time(),sql_end-time.monotonic(),c,before_ddl=True)
        state=ROOT/'state.json';private_write(state,b'{"next":0,"terminal":false,"records":[]}')
        cfg={'token_file':str(tokenfile),'binary':str(binary),'binary_sha256':sha(binary.read_bytes()),'state_file':str(state),'deadline':time.time()+sql_end-time.monotonic(),'call_timeout':c['child_seconds'],'payload_sha256':c['payload_sha256']}
        adaptercfg=ROOT/'adapter-config.json';private_write(adaptercfg,json.dumps(cfg).encode())
        bindir=ROOT/'bin';bindir.mkdir(mode=0o700);shim=bindir/'sqlcmd'
        # Fixed shim selects the approved adapter; original installer remains unchanged.
        shim.write_text('#!'+sys.executable+'\nimport runpy\nrunpy.run_path('+repr(str(R/'token_adapter.py'))+',run_name="__main__")\n');shim.chmod(0o700)
        env.update(PATH=str(bindir)+':/usr/bin:/bin',FV_ADAPTER_CONFIG=str(adaptercfg),FIELDVISIT_APPROVED_INSTALL_MANIFEST=c['manifest_sha256'],PYTHONUTF8='1',PYTHONIOENCODING='utf-8')
        result.update(ddl_started=True,installer_invocations=1)
        out=child([sys.executable,str(ROOT/'package/install.py'),'--root',str(ROOT/'package'),'--target','pathb-uat','--execute'],env,sql_end,cap=sql_end-time.monotonic())
        data=json.loads(state.read_text());result['calls']=data['records']
        require(data['next']==16 and data['terminal'] and len(data['records'])==16 and all(x['child_exit']==0 for x in data['records']),'SIXTEEN_CALL_COMPLETION')
        require(b'INSTALLER=FINAL_FOURTEEN_LAYER_SCOPE_PASS' in out,'INSTALLER_FINAL')
        readbase=[str(binary),'-S',c['server'],'-d',c['database'],'-G','-P',str(tokenfile),'-I','-b','-l','10','-Nm','-h','-1']
        result['postcheck_calls']=1
        post=child(readbase+['-W','-s','|','-Q',(R/'postcheck.sql').read_text()],env,sql_end)
        rows=[x.strip().split('|') for x in post.decode().splitlines() if x.strip().startswith('FV_POST|')]
        require(len(rows)==1 and [x.strip() for x in rows[0]]==['FV_POST','69','3','3','0','0','0'],'POSTCHECK')
        result['postcheck_calls']=2
        catalog=child(readbase+['-y','0','-w','65535','-Q',(R/'schema-fingerprint.sql').read_text()],env,sql_end)
        result['schema']=catalog_result(catalog)
        result['status']='INSTALLER_69_TABLE_3_MARKER_CONSTRAINT_TRIGGER_FINGERPRINT_PASS'
    except ChildFailure as e:
        result['failure']=e.record
    except Exception as e:
        safe=str(e);result['failure']={'category':safe if re.fullmatch('[A-Z_]+',safe) else 'CONTROL_FAILURE_DETAILS_SUPPRESSED'}
    finally:
        token=None
        if (ROOT/'state.json').exists():
            try:
                journal=json.loads((ROOT/'state.json').read_text());result['calls']=journal.get('records',[])
                result['call_state']={k:journal[k] for k in ['next','terminal','inflight'] if k in journal}
            except Exception:result['call_journal']='UNKNOWN'
        if created:
            kill_owned()
            subprocess.run([sys.executable,str(R/'cleanup.py')],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=c['cleanup_seconds'])
            result['independent_local_cleanup_exit']=subprocess.run([sys.executable,str(R/'cleanup.py'),'--verify'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=20).returncode
        else:result['independent_local_cleanup_exit']=125
        if watch:
            watch.terminate();watch.wait(timeout=5)
        result['elapsed_seconds']=round(time.monotonic()-started,2)
        print('FV_FINAL_RESULT='+json.dumps(result),flush=True)
    return 0 if result['status'].endswith('_PASS') and result['independent_local_cleanup_exit']==0 else 1
def main():
    p=argparse.ArgumentParser();p.add_argument('--package',required=True);p.add_argument('--packages');p.add_argument('--management');p.add_argument('--config-sha',required=True);p.add_argument('--execute',action='store_true');p.add_argument('--owner-approval');a=p.parse_args()
    if not a.execute:
        c=load_config(a.config_sha);package_inputs(a.package,c);print('OFFLINE_BINDING_PASS_NO_CONNECTION');return 0
    # Catch connection loss / interruption as failure with finally cleanup.
    for s in [signal.SIGTERM,signal.SIGHUP,signal.SIGINT]:signal.signal(s,lambda *_:(_ for _ in ()).throw(ValueError('INTERRUPTED')))
    return run(a.package,a.packages,a.management,a.config_sha,a.owner_approval)
if __name__=='__main__':
    try:sys.exit(main())
    except Exception:print('SUPERVISOR=HOLD_DETAILS_SUPPRESSED');sys.exit(1)
