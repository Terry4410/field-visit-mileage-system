#!/usr/bin/env python3
"""Authorization-locked App-side Path B P3 runner.

This file has no default live behavior.  --execute requires an external Owner
authorization record bound to the final distribution SHA and this bundle's
binding index.  Tokens are read without echo from Kudu stdin, never argv/env.
"""
import argparse, base64, getpass, hashlib, json, os, re, shutil, signal
import socket, stat, subprocess, sys, time, uuid, zipfile
from datetime import datetime
from pathlib import Path

HERE=Path(__file__).resolve().parent
ROOT=Path('/tmp/fv-pathb-g1-original-installer-20261110-work')
RAM=Path('/dev/shm/fv-pathb-g1-original-installer-20261110')
SERVER='sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DATABASE='db-fieldvisit-pathb-uat'
EXPECTED_APPROVAL='OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE'
RUNTIME_OWNER=b'FIELDVISIT_PATHB_G1_ORIGINAL_INSTALLER_V1\n'
INSTALLER_GROUP_CLEARED=True

sys.path.insert(0,str(HERE))
from p3_blob_guard import OneTimeInstallBlobGuard, pinned_http_request, REQUIRED_PRE_DDL_GATES, InstallGuardHold
from p3_install_gates import (validate_local_source,validate_original_package,strict_row_22,
                              PAYLOAD_SHA256,MANIFEST_SHA,digest)
from p3_ssh_stage import strict_seal_crossbinding
from p3_ssh_transport_contract import validate_owner_token

class Hold(Exception):pass
class SqlFailure(Hold):
    def __init__(self,record):self.record=record;super().__init__('SQL_CHILD_FAILED')
def require(ok,code):
    if not ok:raise Hold(code)
def private_write(path,data):
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'wb') as f:f.write(data);f.flush();os.fsync(f.fileno())
def load_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))
def verify_authorization(auth,artifact_sha,binding_sha,instance_sha,now=None):
    require(isinstance(auth,dict) and set(auth)=={
        'contract','decision','artifact_sha256','binding_index_sha256',
        'server','database','installation_attempts','instance_sha256',
        'not_before','expires_at','operation_id'},'OWNER_AUTHORIZATION_SHAPE')
    require(auth.get('decision')==EXPECTED_APPROVAL,'OWNER_AUTHORIZATION_MISSING')
    require(auth.get('contract')=='FIELDVISIT_PATHB_G1_INSTALL_V1','OWNER_AUTHORIZATION_CONTRACT')
    require(auth.get('artifact_sha256')==artifact_sha,'OWNER_ARTIFACT_NOT_APPROVED')
    require(auth.get('binding_index_sha256')==binding_sha,'OWNER_BINDING_NOT_APPROVED')
    require(auth.get('server')==SERVER and auth.get('database')==DATABASE,'OWNER_TARGET_NOT_APPROVED')
    require(auth.get('installation_attempts')==1 and
            re.fullmatch(r'[0-9a-f]{32}',str(auth.get('operation_id',''))) is not None,
            'OWNER_ATTEMPT_SCOPE')
    require(auth.get('instance_sha256')==instance_sha and re.fullmatch(r'[a-f0-9]{64}',str(instance_sha)) is not None,'OWNER_INSTANCE_NOT_APPROVED')
    try:
        start=datetime.fromisoformat(auth['not_before'].replace('Z','+00:00')).timestamp()
        end=datetime.fromisoformat(auth['expires_at'].replace('Z','+00:00')).timestamp()
    except Exception as exc:raise Hold('OWNER_AUTHORIZATION_TIME') from exc
    clock=time.time() if now is None else float(now)
    require(auth['not_before'].endswith('Z') and auth['expires_at'].endswith('Z') and start<=clock<=end and 0<end-start<=1800,'OWNER_AUTHORIZATION_TIME')
    return True
def verify_bundle(binding):
    require(binding.get('contract')=='FIELDVISIT_PATHB_G1_ORIGINAL_BINDING_V1','BINDING_CONTRACT')
    for name,expected in binding['files'].items():
        p=HERE/name;require(p.is_file() and not p.is_symlink() and digest(p.read_bytes())==expected,'BINDING_FILE_HASH')
    return True
def token_exp(token,kind,now):
    validate_owner_token(token,kind,now)
    try:return float(json.loads(base64.urlsafe_b64decode(token.split('.')[1]+'==='))['exp'])
    except Exception as e:raise Hold('TOKEN_EXPIRY') from e
def stage_tools(cfg):
    toolroot=ROOT/'tools';toolroot.mkdir(mode=0o700)
    for item in cfg['tools']:
        p=HERE/'debs'/(item['name']+'.deb')
        require(p.is_file() and digest(p.read_bytes())==item['sha256'],'TOOL_PACKAGE_HASH')
        subprocess.run(['dpkg-deb','-x',str(p),str(toolroot)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=30)
    for rel,expected in cfg['tool_files'].items():require(digest((toolroot/rel).read_bytes())==expected,'TOOL_BINARY_HASH')
    driver=toolroot/'opt/microsoft/msodbcsql18/lib64/libmsodbcsql-18.6.so.1.1'
    odbc=ROOT/'odbc';odbc.mkdir(mode=0o700)
    (odbc/'odbcinst.ini').write_text('[ODBC Driver 18 for SQL Server]\nDriver='+str(driver)+'\nUsageCount=1\n',encoding='ascii')
    (odbc/'odbc.ini').write_text('',encoding='ascii')
    binary=toolroot/'opt/mssql-tools18/bin/sqlcmd'
    env={'PATH':'/usr/bin:/bin','HOME':str(ROOT),'LANG':'C.UTF-8','LC_ALL':'C.UTF-8',
         'ODBCSYSINI':str(odbc),'ODBCINI':'odbc.ini','ODBCINSTINI':'odbcinst.ini',
         'LD_LIBRARY_PATH':':'.join([str(toolroot/'opt/microsoft/msodbcsql18/lib64'),str(toolroot/'usr/lib/x86_64-linux-gnu')])}
    return binary,env
def sql_call(binary,env,tokenfile,raw,deadline,index):
    """Guard EACH non-installer SQL client, including preflight/postchecks.

    SQL completion remains UNKNOWN after a timeout or forced termination;
    process-group cleanup is NEVER evidence that SQL Server rolled back DDL.
    """
    global INSTALLER_GROUP_CLEARED
    require(time.monotonic()<deadline,'SQL_DEADLINE')
    args=[str(binary),'-S',SERVER,'-d',DATABASE,'-I','-b','-l','10','-G','-P',str(tokenfile),'-Nm','-h','-1','-W','-w','65535','-s','|']
    INSTALLER_GROUP_CLEARED=False
    p=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,
                       stderr=subprocess.PIPE,env=env,start_new_session=True)
    try:
        try:
            out,err=p.communicate(raw,timeout=min(110,max(1,deadline-time.monotonic())))
        except subprocess.TimeoutExpired as exc:
            raise SqlFailure({'index':index,'category':'PROCESS_TIMEOUT_UNKNOWN_SQL_OUTCOME',
                              'exit':None}) from exc
        if p.returncode:
            from token_adapter import classify
            rec={'index':index,'exit':p.returncode,'input_sha256':digest(raw)}
            try:rec.update(classify((out+err).decode('utf-8','replace')))
            except Exception:rec['category']='CLASSIFIER_FAILED_ORIGINAL_EXIT_PRESERVED'
            raise SqlFailure(rec)
        # A successful direct client exit cannot certify its descendants exited.
        if _group_alive(p.pid):
            raise Hold('SQL_CLIENT_DESCENDANTS_AFTER_EXIT')
        return out
    finally:
        stopped=_stop_installer_group(p.pid,p)
        try:
            if p.poll() is None:p.wait(timeout=2)
        except subprocess.TimeoutExpired:stopped=False
        for stream in (p.stdin,p.stdout,p.stderr):
            if stream is not None and not stream.closed:stream.close()
        if not stopped:
            raise Hold('SQL_CLIENT_PROCESS_GROUP_NOT_CLEARED')
        INSTALLER_GROUP_CLEARED=True

def _group_alive(pgid):
    """Check the dedicated installer process group (including descendants)."""
    try:
        os.killpg(pgid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        # Never classify an inaccessible process group as safely gone.
        return True

def _stop_installer_group(pgid, leader, timeout=5.0):
    """Bounded group stop; NEVER claim an unknown SQL outcome was rolled back."""
    for sig in (signal.SIGTERM, signal.SIGKILL):
        leader.poll()  # reap the group leader if already exited
        if not _group_alive(pgid):
            return True
        try:
            os.killpg(pgid, sig)
        except ProcessLookupError:
            return True
        end=time.monotonic()+(0.5 if sig == signal.SIGTERM else timeout)
        while time.monotonic()<end:
            leader.poll()
            if not _group_alive(pgid):
                return True
            time.sleep(0.05)
    leader.poll()
    return not _group_alive(pgid)

def run_installer_managed(argv, env, timeout):
    """Own the WHOLE installer process group, not merely its Python parent.

    The SQL adapter MUST spawn sqlcmd in this SAME group. If a child detaches,
    group absence is not sufficient: keep LIVE_GO blocked pending live proof.
    """
    global INSTALLER_GROUP_CLEARED
    INSTALLER_GROUP_CLEARED=False
    p=subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                       stderr=subprocess.PIPE, env=env, start_new_session=True)
    pgid=p.pid
    outcome=None
    try:
        try:
            stdout,stderr=p.communicate(timeout=timeout)
        except subprocess.TimeoutExpired as exc:
            outcome='TIMEOUT_UNKNOWN_SQL_OUTCOME'
            raise Hold(outcome) from exc
        # A zero exit does not certify that spawned SQL clients are gone.
        if p.returncode!=0:
            outcome='INSTALLER_FAILED_SQL_OUTCOME_UNKNOWN'
            raise Hold(outcome)
        if _group_alive(pgid):
            outcome='INSTALLER_DESCENDANTS_AFTER_EXIT'
            raise Hold(outcome)
        return subprocess.CompletedProcess(argv,p.returncode,stdout,stderr)
    finally:
        # Even on a successful parent exit, descendants may still run.
        stopped=_stop_installer_group(pgid,p)
        try:
            if p.poll() is None:
                p.wait(timeout=2)
        except subprocess.TimeoutExpired:
            stopped=False
        for stream in (p.stdout,p.stderr):
            if stream is not None and not stream.closed:
                stream.close()
        if not stopped:
            raise Hold('INSTALLER_PROCESS_GROUP_NOT_CLEARED')
        INSTALLER_GROUP_CLEARED=True

def install_package(package):
    dest=ROOT/'package';dest.mkdir(mode=0o700)
    with zipfile.ZipFile(package) as z:
        for info in z.infolist():
            p=Path(info.filename);require(not p.is_absolute() and '..' not in p.parts,'PACKAGE_MEMBER_PATH')
            target=dest/p;target.parent.mkdir(parents=True,exist_ok=True);private_write(target,z.read(info.filename))
    return dest
def parse_postcheck(raw):
    rows=[x.strip().split('|') for x in raw.decode('utf-8','strict').splitlines() if x.strip().startswith('FV_POST|')]
    require(len(rows)==1 and [x.strip() for x in rows[0]]==['FV_POST','69','3','3','0','0','0'],'POSTCHECK_HOLD')
    return True
def parse_fingerprint(raw):
    text=''.join(raw.decode('utf-8','strict').splitlines()).strip();data=json.loads(text)
    require(isinstance(data,list) and len(data)==69,'FINGERPRINT_TABLE_COUNT')
    for table in data:
        require(all(not x['is_disabled'] and not x['is_not_trusted'] for k in ('checks','foreignKeys') for x in (table.get(k) or [])),'FINGERPRINT_CONSTRAINT')
        require(all(not x['is_disabled'] and x.get('definition') is not None for x in (table.get('triggers') or [])),'FINGERPRINT_TRIGGER')
    return digest(text.encode())
def cleanup():
    """Remove only runtime roots created and sealed by this exact runner."""
    for p in (RAM,ROOT):
        if not os.path.lexists(p):continue
        require(p.is_dir() and not p.is_symlink() and p.stat().st_uid==os.geteuid() and
                stat.S_IMODE(p.stat().st_mode)==0o700,'OWNED_CLEANUP_HOLD')
        sentinel=p/'.fv-p3-owned'
        require(sentinel.is_file() and not sentinel.is_symlink() and
                sentinel.stat().st_uid==os.geteuid() and sentinel.read_bytes()==RUNTIME_OWNER,
                'OWNED_CLEANUP_HOLD')
        shutil.rmtree(p)
def verified_pre_ddl_gates(evidence, *, sql_expires, storage_expires, deadline, now_wall, now_mono):
    """Evidence-linked, fail-closed gate ledger; never synthesize all-PASS.

    Source flags must be set only by completed real validations in run(). The
    remaining runtime budget is measured immediately before marker creation.
    """
    required_time=16*120+2*110+60  # original 16 + 2 readbacks + safety buffer
    observed={
        'EXACT_ORIGINAL_PACKAGE': evidence.get('package_sha_verified') is True,
        'MANIFEST_SEAL_14_LAYERS': evidence.get('seal_and_layers_verified') is True,
        'PROTECTED_OWNER_SSH': evidence.get('protected_ssh_stage_verified') is True,
        'OWNER_SQL_TOKEN_TTL': (evidence.get('owner_sql_token_verified') is True and
                                 sql_expires-now_wall>=required_time+300 and
                                 storage_expires-now_wall>=300),
        'PRIVATE_DNS_TLS': (evidence.get('private_dns_verified') is True and
                            evidence.get('fresh_sql_tls_verified') is True),
        'EXACT_SQL_IDENTITY': evidence.get('fresh_sql_identity_verified') is True,
        'FRESH_22_FIELD_DATABASE_EMPTY': evidence.get('fresh_empty_verified') is True,
        'INSTALLER_DRY_RUN': evidence.get('installer_dry_run_verified') is True,
        'SQL_BUDGET': (evidence.get('sql_call_count')==1 and
                       deadline-now_mono>=required_time),
    }
    require(set(observed)==set(REQUIRED_PRE_DDL_GATES),'GATE_CONTRACT_MISMATCH')
    require(all(observed.values()),'PRE_DDL_EVIDENCE_MISSING')
    return {name:'PASS' for name,passed in observed.items() if passed}

def run(args):
    started=time.monotonic();evidence={};result={'status':'HOLD','sql_call_count':0,'layer_progress':0,'marker':'NOT_CONSUMED'}
    sql_token=storage_token=None
    try:
        cfg=load_json(HERE/'executor_config.json');binding_raw=(HERE/'binding-index.json').read_bytes();binding=json.loads(binding_raw)
        facts=load_json(HERE/'live-facts.json')
        auth=load_json(args.authorization)
        verify_bundle(binding);verify_authorization(auth,args.artifact_sha256,digest(binding_raw),facts.get('instance_sha256'))
        require(not os.path.lexists(ROOT) and not os.path.lexists(RAM),'STALE_RUNTIME_STATE')
        ROOT.mkdir(mode=0o700);RAM.mkdir(mode=0o700)
        private_write(ROOT/'.fv-p3-owned',RUNTIME_OWNER);private_write(RAM/'.fv-p3-owned',RUNTIME_OWNER)
        require(facts=={'server':SERVER,'database':DATABASE,'app':'api-fieldvisit-pathb-uat','pna':'Disabled','firewall_count':0,'owner_admin_match':True,'private_sql_ip':'10.90.2.4','ssh_transport':'OFFICIAL_AZ_WEBAPP_SSH_PINNED_V1','instance_sha256':facts.get('instance_sha256'),'observed_at':facts.get('observed_at')},'MANAGEMENT_FACTS')
        require(re.fullmatch(r'[a-f0-9]{64}',str(facts['instance_sha256'])) is not None,'MANAGEMENT_FACTS')
        require(0<=time.time()-float(facts['observed_at'])<=180,'MANAGEMENT_FACTS_STALE')
        ips={x[4][0] for x in socket.getaddrinfo(SERVER,1433,type=socket.SOCK_STREAM)};require(ips=={facts['private_sql_ip']},'PRIVATE_DNS');evidence.update(private_dns_verified=True,protected_ssh_stage_verified=(facts['ssh_transport']=='OFFICIAL_AZ_WEBAPP_SSH_PINNED_V1'))
        validate_local_source(HERE)
        package=(HERE/'original.zip').read_bytes();seal=(HERE/'original.seal.json').read_bytes()
        require(digest(package)==cfg['package_sha256'] and digest(seal)==cfg['seal_sha256'],'ORIGINAL_BLOB_HASH')
        require(validate_original_package(package)=='ORIGINAL_14_LAYERS_PASS','ORIGINAL_PACKAGE')
        strict_seal_crossbinding(package,seal)
        evidence.update(package_sha_verified=True,seal_and_layers_verified=True)
        package_root=install_package(HERE/'original.zip')
        dry=subprocess.run([sys.executable,str(package_root/'install.py'),'--root',str(package_root),'--target','pathb-uat'],capture_output=True,timeout=30)
        require(dry.returncode==0 and b'HASH_VERIFIED_DRY_RUN_NO_CONNECTION' in dry.stdout,'INSTALLER_DRY_RUN');evidence['installer_dry_run_verified']=True
        binary,env=stage_tools(cfg)
        print('\nFV_P3_TOKENS_READY',flush=True)
        sql_token=getpass.getpass('OWNER_SQL_TOKEN:');storage_token=getpass.getpass('OWNER_STORAGE_TOKEN:')
        now=time.time();sql_exp=token_exp(sql_token,'sql',now);storage_exp=token_exp(storage_token,'storage',now)
        require(min(sql_exp,storage_exp)-now>=cfg['minimum_token_ttl_seconds'],'TOKEN_TTL');evidence['owner_sql_token_verified']=True
        tokenfile=RAM/'sql-token';private_write(tokenfile,sql_token.encode('utf-16le'));sql_token=None
        deadline=min(started+cfg['total_deadline_seconds']-cfg['cleanup_reserve_seconds'],time.monotonic()+cfg['sql_deadline_seconds'])
        pre=(HERE/'preflight.sql').read_bytes();require(digest(pre)==cfg['preflight_sha256'],'PREFLIGHT_HASH')
        out=sql_call(binary,env,tokenfile,pre,deadline,1);result['sql_call_count']=1
        require(strict_row_22(out)=='DATABASE_EMPTY_PASS','EMPTY_DATABASE')
        # strict_row_22 verifies exact SQL identity, encrypted TCP, all metadata
        # and empty database; these flags exist only AFTER that actual SQL output.
        evidence.update(fresh_sql_tls_verified=True,fresh_sql_identity_verified=True,
                        fresh_empty_verified=True,sql_call_count=result['sql_call_count'])
        gates=verified_pre_ddl_gates(evidence,sql_expires=sql_exp,
                storage_expires=storage_exp,deadline=deadline,
                now_wall=time.time(),now_mono=time.monotonic())
        guard=OneTimeInstallBlobGuard(storage_token,request=pinned_http_request(storage_token));guard.check_predecessors();guard.check_available()
        # Prepare and SHA-bind the immutable claim before any DDL marker PUT.
        claim_sha=guard.prepare(gates,operation_id=auth['operation_id'],
                artifact_sha256=args.artifact_sha256,instance_sha256=facts['instance_sha256'])
        # All App-side files are fully initialized BEFORE consuming the Blob.
        state=ROOT/'adapter-state.json'
        private_write(state,json.dumps({'next':0,'terminal':False,'records':[]}).encode())
        storage_token_file=RAM/'storage-token'
        private_write(storage_token_file,storage_token.encode('ascii'))
        adapter_cfg={'deadline':time.time()+max(0.0,deadline-time.monotonic()),
                     'token_file':str(tokenfile),'binary':str(binary),
                     'binary_sha256':digest(binary.read_bytes()),
                     'state_file':str(state),'payload_sha256':list(PAYLOAD_SHA256),
                     'call_timeout':110, 'g1_operation_id':auth['operation_id'],
                     'g1_claim_sha256':claim_sha,
                     'g1_storage_token_file':str(storage_token_file),
                     'g1_live_approval':EXPECTED_APPROVAL}
        acfg=ROOT/'adapter-config.json'
        private_write(acfg,json.dumps(adapter_cfg,separators=(',',':')).encode())
        wrapper=ROOT/'bin';wrapper.mkdir(mode=0o700)
        shutil.copy2(HERE/'token_adapter_g1_REVIEW_ONLY.py',wrapper/'sqlcmd')
        os.chmod(wrapper/'sqlcmd',0o700)
        runenv=dict(env,PATH=str(wrapper)+':/usr/bin:/bin',
                    FV_ADAPTER_CONFIG=str(acfg),
                    FIELDVISIT_APPROVED_INSTALL_MANIFEST=MANIFEST_SHA,
                    PYTHONPATH=str(HERE))
        # Final budget check occurs *before* the single irreversible PUT.
        require(min(sql_exp,storage_exp)-time.time()>=2600,'PRE_DDL_TOKEN_TTL')
        require(deadline-time.monotonic()>=16*120+2*110+60,'PRE_DDL_SQL_BUDGET')
        require(guard.consume(gates)==claim_sha,'BLOB_CLAIM_SHA_MISMATCH')
        result['marker']='CONSUMED'
        storage_token=None
        # The next step after consumption is the original frozen installer.
        proc=run_installer_managed([sys.executable,str(package_root/'install.py'),'--root',str(package_root),'--target','pathb-uat','--execute'],runenv,max(1,deadline-time.monotonic()))
        journal=load_json(state);result['sql_call_count']=1+int(journal.get('next',0));result['layer_progress']=max(0,min(14,int(journal.get('next',0))-1));result['journal']=journal.get('records',[])
        require(proc.returncode==0 and journal.get('next')==16 and len(journal.get('records',[]))==16,'INSTALLER_PARTIAL_OR_FAILED')
        post=sql_call(binary,env,tokenfile,(HERE/'postcheck.sql').read_bytes(),deadline,18);result['sql_call_count']=18;parse_postcheck(post)
        fp=sql_call(binary,env,tokenfile,(HERE/'schema-fingerprint.sql').read_bytes(),deadline,19);result['sql_call_count']=19
        fingerprint_sha=parse_fingerprint(fp)
        # External SQL acceptance receipt after all 16 DDL calls + 2 readbacks.
        # If this durable receipt is uncertain, never report PASS or replay.
        from g1_blob_call_journal_REVIEW_ONLY import construct_postcheck, persist_exact
        from p3_g1_blob_transport import build_request_from_private_storage_token
        final_record=construct_postcheck(auth['operation_id'],claim_sha,fingerprint_sha)
        persist_exact(final_record,build_request_from_private_storage_token(storage_token_file))
        result.update(status='PASS',layer_progress=14,user_tables=69,markers=3,
                      schema_fingerprint_sha256=fingerprint_sha)
    except SqlFailure as e:result['failure']=e.record
    except Exception as e:
        code=str(e);result['failure']={'category':code if re.fullmatch('[A-Z0-9_]{3,100}',code) else 'CONTROL_FAILURE_DETAILS_SUPPRESSED'}
    finally:
        sql_token=storage_token=None
        if not INSTALLER_GROUP_CLEARED:
            # Preserve all runtime evidence/token files in their guarded directories;
            # NEVER remove files while a SQL client might still be running.
            result['cleanup']='HOLD_PROCESS_GROUP_NOT_CLEARED'
        else:
            try:cleanup();result['cleanup']='PASS' if not os.path.lexists(ROOT) and not os.path.lexists(RAM) else 'HOLD'
            except Exception:result['cleanup']='HOLD'
        result['elapsed_seconds']=round(time.monotonic()-started,2)
        print('\nFV_P3_FINAL_RESULT='+json.dumps(result,separators=(',',':')),flush=True)
    return 0 if result.get('status')=='PASS' and result.get('cleanup')=='PASS' else 1
def main():
    p=argparse.ArgumentParser();p.add_argument('--execute',action='store_true');p.add_argument('--authorization');p.add_argument('--artifact-sha256');a=p.parse_args()
    if not a.execute:print('AUTHORIZATION_LOCKED_NONDEPLOY');return 0
    require(bool(a.authorization) and re.fullmatch('[a-f0-9]{64}',a.artifact_sha256 or '') is not None,'OWNER_AUTHORIZATION_REQUIRED')
    return run(a)
if __name__=='__main__':
    try:raise SystemExit(main())
    except Exception:print('P3_RUNNER=HOLD_DETAILS_SUPPRESSED');raise SystemExit(1)
