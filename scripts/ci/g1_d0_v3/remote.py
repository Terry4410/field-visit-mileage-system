"""D0 v3 remote payload: fixed /proc and six-file reads, no environment/SQL.

Only this file's bytes are sent to the main container. No imports from v2's
configuration/SQL helper and no filesystem writes or .NET process launch.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path('/home/site/wwwroot')
DLLS = ('FieldVisit.Api.dll', 'FieldVisit.Application.dll',
        'FieldVisit.Domain.dll', 'FieldVisit.Infrastructure.dll')
FIXED = ('Microsoft.Data.SqlClient.dll', 'FieldVisit.Api.runtimeconfig.json')


class ContractStop(Exception):
    pass


def digest(value):
    return hashlib.sha256(value).hexdigest()


def find_api():
    candidates = []
    for p in Path('/proc').iterdir():
        if not p.name.isdecimal():
            continue
        try:
            if (p / 'comm').read_text().strip() not in ('dotnet', 'FieldVisit.Api'):
                continue
            args = (p / 'cmdline').read_bytes().split(b'\0')
            if str(ROOT / DLLS[0]).encode() in args and len([a for a in args if a]) == 2:
                candidates.append(int(p.name))
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
    if len(candidates) != 1 or Path(f'/proc/{candidates[0]}/cwd').resolve() != ROOT:
        raise ContractStop()
    return candidates[0]


def process_start(pid):
    value = Path(f'/proc/{pid}/stat').read_text()
    return int(value[value.rfind(')') + 2:].split()[19])


def file_hash(path):
    if path.is_symlink() or not path.is_file():
        raise ContractStop()
    before = path.stat()
    if before.st_size > 67108864:
        raise ContractStop()
    content = path.read_bytes()
    after = path.stat()
    if (before.st_ino, before.st_mtime_ns, before.st_size) != (after.st_ino, after.st_mtime_ns, after.st_size):
        raise ContractStop()
    return digest(content)


def runtime_snapshot(pid):
    maps = Path(f'/proc/{pid}/maps').read_text().splitlines()
    hashes = {}
    for name in DLLS:
        path = ROOT / name
        before = path.stat()
        matching = [s.split(maxsplit=5) for s in maps
                    if len(s.split(maxsplit=5)) == 6 and s.split(maxsplit=5)[5] == str(path)]
        if not matching or any(int(s[4]) != before.st_ino for s in matching):
            raise ContractStop()
        hashes[name] = file_hash(path)
        after = path.stat()
        if (before.st_ino, before.st_mtime_ns, before.st_size) != (after.st_ino, after.st_mtime_ns, after.st_size):
            raise ContractStop()
    composite = digest(''.join(n + ':' + hashes[n] + '\n' for n in DLLS).encode())
    return dict(assemblies=hashes, apiCompositeSha256=composite)


def emit(value):
    print(json.dumps(value, separators=(',', ':')), flush=True)


def run(nonce):
    # This is a closed diagnostic protocol, never exception/message projection.
    stage = 'MAIN_PROCESS'
    try:
        if not isinstance(nonce, str) or len(nonce) != 64 or any(c not in '0123456789abcdef' for c in nonce):
            return 1
        def progress(state):
            emit(dict(kind='D0_V3_STAGE', nonce=nonce, stage=stage, state=state))
        progress('ATTEMPT')
        pid = find_api()
        start = process_start(pid)
        progress('COMPLETE')
        stage = 'SIX_HASHES'
        progress('ATTEMPT')
        runtime = runtime_snapshot(pid)
        fixed = {name: file_hash(ROOT / name) for name in FIXED}
        if process_start(pid) != start or runtime_snapshot(pid) != runtime:
            raise ContractStop()
        progress('COMPLETE')
        emit(dict(kind='D0_V3_OBSERVATION', nonce=nonce, runtime=runtime,
                  fixedFiles=fixed, processStable=True, sqlConnections=0, configuration='NOT_READ'))
        return 0
    except Exception as error:
        classification = ('CONTRACT_REJECTED' if isinstance(error, ContractStop) else
                          'PERMISSION_DENIED' if isinstance(error, PermissionError) else
                          'FILE_UNAVAILABLE' if isinstance(error, (FileNotFoundError, ProcessLookupError)) else
                          'TIMEOUT' if isinstance(error, TimeoutError) else 'UNKNOWN')
        emit(dict(kind='D0_V3_STOP', nonce=nonce, stage=stage, errorClass=classification))
        return 1
