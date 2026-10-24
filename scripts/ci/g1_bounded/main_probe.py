"""Reviewed main-container observer. No filesystem writes; credentials stay local.

Not invoked by the existing GET-only workflow. Requires separate exact approval
for /exec/shell, selected startup configuration use, and the one API-identity SQL
session. Only nonce-bound JSON observations are emitted; never raw process text.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import select
import subprocess
import sys
import time

ROOT=Path('/home/site/wwwroot')
DLLS=('FieldVisit.Api.dll','FieldVisit.Application.dll','FieldVisit.Domain.dll','FieldVisit.Infrastructure.dll')
KEYS={'ConnectionStrings__DefaultConnection','ConnectionStrings:DefaultConnection',
      'SQLAZURECONNSTR_DefaultConnection','SQLCONNSTR_DefaultConnection',
      'OptionC__Candidate__Enabled','OptionC:Candidate:Enabled','OPTIONC_EXECUTION_MODE',
      'FIELDVISIT_UAT_ACTIVATION','WEBSITE_SITE_NAME','WEBSITE_RESOURCE_GROUP',
      'ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ASPNETCORE_CONTENTROOT','DOTNET_CONTENTROOT',
      'IDENTITY_ENDPOINT','IDENTITY_HEADER'}
CANONICAL={k.lower():k for k in KEYS}

# .NET8's documented native-host ABI loads bytes into Default load context.
# dotnet exec rejects /proc FD application paths; do not create a .dll symlink
# or a temporary uploaded file to evade that. Host only this reviewed helper.
HOST_BOOTSTRAP=r'''
import ctypes as C,glob,json,os,re,sys
stage='HOST_LIBRARY'
try:
    versions=[]
    for path in glob.glob('/usr/share/dotnet/host/fxr/8.0.*/libhostfxr.so'):
        version=path.split('/')[-2]
        if re.fullmatch(r'8\.0\.\d+',version):versions.append((tuple(map(int,version.split('.'))),path))
    if not versions:raise RuntimeError()
    host=C.CDLL(max(versions)[1])
    host.hostfxr_initialize_for_runtime_config.argtypes=[C.c_char_p,C.c_void_p,C.POINTER(C.c_void_p)]
    host.hostfxr_initialize_for_runtime_config.restype=C.c_int
    host.hostfxr_get_runtime_delegate.argtypes=[C.c_void_p,C.c_int,C.POINTER(C.c_void_p)]
    host.hostfxr_get_runtime_delegate.restype=C.c_int
    host.hostfxr_close.argtypes=[C.c_void_p];host.hostfxr_close.restype=C.c_int
    stage='HOST_INITIALIZE';handle=C.c_void_p()
    if host.hostfxr_initialize_for_runtime_config(os.fsencode(sys.argv[1]),None,C.byref(handle))!=0:raise RuntimeError()
    try:
        # hdt_load_assembly_bytes=8, hdt_get_function_pointer=6 (.NET8 header).
        loadptr=C.c_void_p();getptr=C.c_void_p();stage='HOST_DELEGATES'
        if host.hostfxr_get_runtime_delegate(handle,8,C.byref(loadptr))!=0:raise RuntimeError()
        if host.hostfxr_get_runtime_delegate(handle,6,C.byref(getptr))!=0:raise RuntimeError()
        load=C.CFUNCTYPE(C.c_int,C.c_void_p,C.c_size_t,C.c_void_p,C.c_size_t,C.c_void_p,C.c_void_p)(loadptr.value)
        get=C.CFUNCTYPE(C.c_int,C.c_char_p,C.c_char_p,C.c_void_p,C.c_void_p,C.c_void_p,C.POINTER(C.c_void_p))(getptr.value)
        stage='HOST_LOAD_BYTES'
        with open(sys.argv[2],'rb') as stream:assembly=stream.read(1048577)
        if not 0<len(assembly)<=1048576:raise RuntimeError()
        buffer=C.create_string_buffer(assembly)
        if load(buffer,len(assembly),None,0,None,None)!=0:raise RuntimeError()
        stage='HOST_ENTRY';entry=C.c_void_p();method=sys.argv[3]
        if method not in ('G1Run','G1SelfTest'):raise RuntimeError()
        if get(b'Program, G1Probe',method.encode(),C.c_void_p(-1),None,None,C.byref(entry))!=0:raise RuntimeError()
    finally:host.hostfxr_close(handle)
    result=C.CFUNCTYPE(C.c_int)(entry.value)()
    sys.exit(result)
except Exception:
    print(json.dumps(dict(kind='HOST_STOP',stage=stage,sqlConnections=0)),flush=True)
    sys.exit(1)
'''


class Stop(Exception):
    pass


def digest(value):
    return hashlib.sha256(value).hexdigest()


def selected_environment(stream):
    # Nonselected values are discarded byte-by-byte, never accumulated/exported.
    found={};key=bytearray();value=bytearray();selected=False;in_value=False;total=0
    while True:
        item=stream.read(1)
        if not item:
            if key or value or in_value:raise Stop()
            return found
        total+=1
        if total>1048576:raise Stop()
        if item==b'\0':
            if selected:
                name=CANONICAL[key.decode('ascii').lower()];text=value.decode('utf-8')
                if name in found:raise Stop()
                found[name]=text
            key.clear();value.clear();selected=False;in_value=False
        elif not in_value:
            if item==b'=':
                in_value=True
                raw_key=key.decode('ascii','ignore').lower()
                selected=raw_key in CANONICAL
                # DefaultBuilder also reads prefixed host configuration. Refuse
                # ambiguous application-key overrides rather than infer precedence.
                if any(raw_key.startswith(p) and raw_key[len(p):] in CANONICAL
                       for p in ('aspnetcore_','dotnet_')):raise Stop()
            elif len(key)<256:key.extend(item)
            else:raise Stop()
        elif selected:
            if len(value)>65536:raise Stop()
            value.extend(item)


def process_start(pid):
    stat=Path(f'/proc/{pid}/stat').read_text()
    fields=stat[stat.rfind(')')+2:].split()
    ticks=int(fields[19])
    boot=int(next(s.split()[1] for s in Path('/proc/stat').read_text().splitlines() if s.startswith('btime ')))
    return ticks,boot+ticks/os.sysconf('SC_CLK_TCK')


def find_api():
    candidates=[]
    for p in Path('/proc').iterdir():
        if not p.name.isdecimal():continue
        try:
            if (p/'comm').read_text().strip() not in ('dotnet','FieldVisit.Api'):continue
            args=(p/'cmdline').read_bytes().split(b'\0')
            expected=str(ROOT/'FieldVisit.Api.dll').encode()
            if expected in args and len([a for a in args if a])==2:
                candidates.append(int(p.name))
        except (FileNotFoundError,PermissionError,ProcessLookupError):
            continue
    if len(candidates)!=1:raise Stop()
    pid=candidates[0]
    if Path(f'/proc/{pid}/cwd').resolve()!=ROOT:raise Stop()
    return pid


def runtime_snapshot(pid,root=ROOT):
    maps=Path(f'/proc/{pid}/maps').read_text().splitlines()
    hashes={}
    for name in DLLS:
        path=root/name
        if path.is_symlink() or not path.is_file():raise Stop()
        stat=path.stat()
        matching=[s.split(maxsplit=5) for s in maps if len(s.split(maxsplit=5))==6 and s.split(maxsplit=5)[5]==str(path)]
        if not matching or any(int(s[4])!=stat.st_ino for s in matching):raise Stop()
        content=path.read_bytes()
        if len(content)>67108864:raise Stop()
        after=path.stat()
        if (stat.st_ino,stat.st_mtime_ns,stat.st_size)!=(after.st_ino,after.st_mtime_ns,after.st_size):raise Stop()
        hashes[name]=digest(content)
    composite=digest(''.join(name+':'+hashes[name]+'\n' for name in DLLS).encode())
    return {'apiCompositeSha256':composite,'assemblies':hashes}


def project_json(obj):
    """Only the exact configuration keys read by the reviewed default builder."""
    if not isinstance(obj,dict):raise Stop()
    result={}
    for section,key,name in [('ConnectionStrings','DefaultConnection','connectionString'),
                             ('OptionC','Candidate','candidate')]:
        top=next((v for k,v in obj.items() if k.lower()==section.lower()),{})
        if not isinstance(top,dict):raise Stop()
        value=next((v for k,v in top.items() if k.lower()==key.lower()),None)
        if name=='candidate' and isinstance(value,dict):
            value=next((v for k,v in value.items() if k.lower()=='enabled'),None)
        if value is not None:
            if name=='connectionString' and not isinstance(value,str):raise Stop()
            if name=='candidate' and type(value) is not bool:raise Stop()
            result[name]=value
    return result


def effective_configuration(pid,environment,start_utc):
    if environment.get('WEBSITE_SITE_NAME')!='api-fieldvisit-uat' or environment.get('WEBSITE_RESOURCE_GROUP')!='rg-fieldvisit-uat':raise Stop()
    names=[environment[k] for k in ('DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT') if environment.get(k)]
    if len(set(names))>1:raise Stop()
    name=names[0] if names else 'Production'
    if name=='Development' or not name.isalnum() or len(name)>30:raise Stop()
    if any(environment.get(k) not in (None,'',str(ROOT)) for k in ('DOTNET_CONTENTROOT','ASPNETCORE_CONTENTROOT')):raise Stop()
    config={'candidate':False};sources=[]
    for file in (ROOT/'appsettings.json',ROOT/('appsettings.'+name+'.json')):
        if not file.exists():continue
        if file.is_symlink() or file.stat().st_size>262144 or file.stat().st_mtime>start_utc:raise Stop()
        before=file.stat()
        raw=file.read_bytes()
        def unique(pairs):
            d={}
            for k,v in pairs:
                if k.lower() in {x.lower() for x in d}:raise Stop()
                d[k]=v
            return d
        config.update(project_json(json.loads(raw,object_pairs_hook=unique)))
        after=file.stat()
        if (before.st_ino,before.st_mtime_ns,before.st_size)!=(after.st_ino,after.st_mtime_ns,after.st_size):raise Stop()
        sources.append((file,digest(raw)))
    connections=[environment[k] for k in ('ConnectionStrings__DefaultConnection','ConnectionStrings:DefaultConnection',
                 'SQLAZURECONNSTR_DefaultConnection','SQLCONNSTR_DefaultConnection') if k in environment]
    if len(set(connections))>1:raise Stop()
    if connections:config['connectionString']=connections[0]
    flags=[environment[k] for k in ('OptionC__Candidate__Enabled','OptionC:Candidate:Enabled') if k in environment]
    if len(set(v.lower() for v in flags))>1:raise Stop()
    if flags:
        if flags[0].lower() not in ('true','false'):raise Stop()
        config['candidate']=flags[0].lower()=='true'
    connection=config.get('connectionString','')
    if not connection or len(connection)>65536 or '@Microsoft.KeyVault(' in connection:raise Stop()
    # Predeploy gate proves OFF. Signed activation is a later distinct action.
    mode=environment.get('OPTIONC_EXECUTION_MODE','').lower()
    activation=environment.get('FIELDVISIT_UAT_ACTIVATION','false').lower()
    flags_match=config['candidate'] is False and mode in ('','disabled','off') and activation=='false'
    return config,flags_match,sources


def read_line(stream,seconds,max_bytes=1048576):
    # Unbuffered descriptors: a partial line cannot evade the absolute deadline.
    end=time.monotonic()+seconds;line=bytearray();fd=stream.fileno()
    while len(line)<=max_bytes:
        remaining=end-time.monotonic()
        if remaining<=0 or not select.select([fd],[],[],remaining)[0]:raise Stop()
        item=os.read(fd,1)
        if not item:raise Stop()
        if item==b'\n':return line.decode('utf-8').rstrip('\r')
        line.extend(item)
    raise Stop()


def emit(value):
    print(json.dumps(value,separators=(',',':')),flush=True)


def discover_main(nonce):
    """Stage D0: fixed runtime bytes only; no configuration/driver/token/SQL.

    Does not start .NET, read /proc/*/environ, appsettings or connection strings.
    This deliberately cannot issue or accept a SQL GO command.
    """
    try:
        if len(nonce)!=64 or any(c not in '0123456789abcdef' for c in nonce):raise Stop()
        pid=find_api();start,_=process_start(pid);runtime=runtime_snapshot(pid)
        fixed={}
        for name in ('Microsoft.Data.SqlClient.dll','FieldVisit.Api.runtimeconfig.json'):
            path=ROOT/name
            if path.is_symlink() or path.stat().st_size>67108864:raise Stop()
            before=path.stat();fixed[name]=digest(path.read_bytes());after=path.stat()
            if (before.st_ino,before.st_mtime_ns,before.st_size)!=(after.st_ino,after.st_mtime_ns,after.st_size):raise Stop()
        if process_start(pid)[0]!=start or runtime_snapshot(pid)!=runtime:raise Stop()
        emit(dict(kind='G1_DISCOVERY',nonce=nonce,runtime=runtime,fixedFiles=fixed,
                  processStable=True,sqlConnections=0,configuration='NOT_READ'))
        return 0
    except Exception:
        emit(dict(kind='G1_DISCOVERY_STOP',details='SUPPRESSED',sqlConnections=0))
        return 1


def memory_process(binary,runtimeconfig,entry='G1Run'):
    if not hasattr(os,'memfd_create') or len(binary)>1048576:raise Stop()
    fd=os.memfd_create('fieldvisit-reviewed-g1',flags=0)
    try:
        os.write(fd,binary);os.lseek(fd,0,os.SEEK_SET)
        # Never inherit startup hooks/profilers/additional dependencies or creds.
        env=dict(DOTNET_ROOT='/usr/share/dotnet',DOTNET_EnableDiagnostics='0',
                 DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',
                 COMPlus_PerfMapEnabled='0')
        return subprocess.Popen(['/usr/bin/python3','-I','-B','-c',HOST_BOOTSTRAP,str(runtimeconfig),f'/proc/self/fd/{fd}',entry],
            stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,bufsize=0,
            pass_fds=(fd,),env=env)
    finally:os.close(fd)


def run_memory_probe(binary,runtimeconfig,packet,go_stream,expected_nonce,seconds=240):
    process=None
    try:
        process=memory_process(binary,runtimeconfig)
        process.stdin.write((json.dumps(packet)+'\n').encode());process.stdin.flush()
        prepared=json.loads(read_line(process.stdout,30))
        if prepared.get('kind')!='DB_PREPARED' or prepared.get('nonce')!=expected_nonce or prepared.get('sqlConnections')!=0:
            known={'INPUT','DRIVER','CONSENT','HOST_LIBRARY','HOST_INITIALIZE','HOST_DELEGATES','HOST_LOAD_BYTES','HOST_ENTRY'}
            stage=prepared.get('stage')
            raise Stop(stage if stage in known else 'PREPARE_REJECTED')
        yield prepared
        go=read_line(go_stream,180,100)
        if go!='G1_SQL_GO '+expected_nonce:raise Stop()
        process.stdin.write((go+'\n').encode());process.stdin.flush()
        result=json.loads(read_line(process.stdout,seconds))
        if result.get('kind')!='DB_RESULT' or result.get('nonce')!=expected_nonce or result.get('sqlConnections')!=1:raise Stop()
        process.wait(timeout=5)
        if process.returncode!=0:raise Stop()
        yield result
    finally:
        if process is not None:
            if process.poll() is None:process.kill()
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:pass
            for pipe in (process.stdin,process.stdout):
                if pipe is not None:pipe.close()


def main(expected_probe_sha):
    pid=None;runner=None
    try:
        data=json.loads(read_line(sys.stdin,30,1572864))
        if set(data)!={'nonce','probeBase64','driverHashes','runtimeconfigSha256'}:raise Stop()
        nonce=data['nonce']
        if not isinstance(nonce,str) or len(nonce)!=64 or any(c not in '0123456789abcdef' for c in nonce):raise Stop()
        binary=base64.b64decode(data['probeBase64'],validate=True)
        if digest(binary)!=expected_probe_sha:raise Stop()
        driver_hashes=data['driverHashes']
        if not isinstance(driver_hashes,dict) or not 1<=len(driver_hashes)<=256:raise Stop()
        for name,sha in driver_hashes.items():
            if (not isinstance(name,str) or '/' in name or '\\' in name or not name.endswith('.dll') or
                not isinstance(sha,str) or len(sha)!=64 or any(c not in '0123456789abcdef' for c in sha)):raise Stop()
            file=ROOT/name
            if file.is_symlink() or file.stat().st_size>67108864 or digest(file.read_bytes())!=sha:raise Stop()
        runtimeconfig=ROOT/'FieldVisit.Api.runtimeconfig.json'
        if runtimeconfig.is_symlink() or runtimeconfig.stat().st_size>65536 or digest(runtimeconfig.read_bytes())!=data['runtimeconfigSha256']:raise Stop()
        pid=find_api();start,start_utc=process_start(pid)
        runtime=runtime_snapshot(pid)
        with open(f'/proc/{pid}/environ','rb') as stream:environment=selected_environment(stream)
        config,flags,sources=effective_configuration(pid,environment,start_utc)
        packet=dict(nonce=nonce,connectionString=config['connectionString'],
            driverHashes=driver_hashes,
            identityEndpoint=environment.get('IDENTITY_ENDPOINT',''),identityHeader=environment.get('IDENTITY_HEADER',''))
        runner=run_memory_probe(binary,runtimeconfig,packet,sys.stdin,nonce)
        prepared=next(runner)
        emit(dict(kind='G1_OBSERVATION',nonce=nonce,runtime=runtime,flags='MATCH' if flags else 'HOLD',
            authentication=prepared['authentication'],sqlConnections=0,configuration='STARTUP_BOUND'))
        result=next(runner)
        if process_start(pid)[0]!=start or runtime_snapshot(pid)!=runtime or any(digest(p.read_bytes())!=h for p,h in sources):raise Stop()
        # Only controlled fields/hashes; no configuration or provider text.
        emit(dict(kind='G1_RESULT',nonce=nonce,processStable=True,apiPrincipal=result['apiPrincipal'],
            schema=result['schema'],principalSidSha256=result['principalSidSha256'],
            actualSchemaSha256=result['actualSchemaSha256'],sqlConnections=1))
        return 0
    except Exception:
        emit(dict(kind='G1_STOP',details='SUPPRESSED',sqlConnections='UNKNOWN_AFTER_GO'))
        return 1
    finally:
        if runner is not None:runner.close()
