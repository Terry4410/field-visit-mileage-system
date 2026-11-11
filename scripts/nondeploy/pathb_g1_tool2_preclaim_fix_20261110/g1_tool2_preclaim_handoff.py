#!/usr/bin/env python3
"""Path B G1 exact-distribution Parent handoff. NOT AN AUTHORIZATION.

No Azure access occurs during --check. --live requires an independently prepared,
Owner-controlled 0600 authorization file and consumes an exclusive local claim.
This utility never generates an Owner approval, credential, SQL command or token.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
import zipfile

from g1_preclaim_tools import ToolHold, acquire_all

EXPECTED_ARCHIVE = '8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141'
EXPECTED_BINDING = '302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45'
EXPECTED_RELEASE = 'd6c220877b9976b6478d85b5171d4e06293595ae'
EXPECTED_CONTRACT = 'FIELDVISIT_PATHB_G1_ORIGINAL_BINDING_V1'
EXPECTED_AUTH_CONTRACT = 'FIELDVISIT_PATHB_G1_INSTALL_V1'
EXPECTED_DECISION = 'OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE'
EXPECTED_SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
EXPECTED_DATABASE = 'db-fieldvisit-pathb-uat'
EXPECTED_PRECLAIM_TOOLS = '5f2d49e3165c3506176d8c30e465462c23dcd8460ba7544331b54b2cbb4898e0'
EXPECTED_PREVERIFIED_PARENT = '3ddafa1cb0bbf435d5111a5f9e01d8b616ef757030bd5799e3fbabd6951e18d7'
REQUIRED_AUTH = {'operation_id','contract','decision','artifact_sha256',
                 'binding_index_sha256','server','database','installation_attempts',
                 'instance_sha256','not_before','expires_at'}

class GateHold(Exception):
    pass

def need(ok, code):
    if not ok:
        raise GateHold(code)

def sha(b):
    return hashlib.sha256(b).hexdigest()

def secure_regular(path: Path, *, private=False):
    info = path.lstat()
    need(stat.S_ISREG(info.st_mode) and info.st_uid == os.geteuid(), 'OWNED_REGULAR_FILE')
    if private:
        need(stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1, 'PRIVATE_AUTH_FILE')
    return info

def inspect_candidate(path: Path):
    secure_regular(path)
    need(sha(path.read_bytes()) == EXPECTED_ARCHIVE, 'CANDIDATE_HASH')
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        need(len(names) == len(set(names)) and len(names) >= 25, 'ZIP_MEMBERS')
        need(all(re.fullmatch(r'[A-Za-z0-9_.-]{1,120}', n) for n in names), 'ZIP_PATH')
        need(all(not (stat.S_IFMT(i.external_attr >> 16) == stat.S_IFLNK) for i in z.infolist()), 'ZIP_LINK')
        need(z.testzip() is None, 'ZIP_CRC')
        raw_binding = z.read('binding-index.json')
        need(sha(raw_binding) == EXPECTED_BINDING, 'BINDING_HASH')
        binding = json.loads(raw_binding)
        need(binding['contract'] == EXPECTED_CONTRACT and binding['approved_release'] == EXPECTED_RELEASE, 'BINDING_RELEASE')
        members = binding['files']
        need(isinstance(members, dict) and len(members) >= 20, 'BINDING_MEMBERS')
        for name, wanted in members.items():
            need(name in names and re.fullmatch(r'[a-f0-9]{64}', wanted) and sha(z.read(name)) == wanted, 'BOUND_FILE')
        need('p3_kudu_cloudshell_parent.py' in names and 'executor_config.json' in names, 'PARENT_MISSING')
        for line in z.read('SHA256SUMS').decode().splitlines():
            expected, name = line.split('  ', 1)
            need(name in names and sha(z.read(name)) == expected, 'SHA256SUMS')
    return len(names)

def validate_auth_file(path: Path):
    secure_regular(path, private=True)
    auth = json.loads(path.read_text('utf-8'))
    need(isinstance(auth, dict) and set(auth) == REQUIRED_AUTH, 'AUTH_SHAPE')
    need(auth['contract'] == EXPECTED_AUTH_CONTRACT and auth['decision'] == EXPECTED_DECISION, 'AUTH_DECISION')
    need(auth['artifact_sha256'] == EXPECTED_ARCHIVE and auth['binding_index_sha256'] == EXPECTED_BINDING, 'AUTH_BINDING')
    need(auth['server'] == EXPECTED_SERVER and auth['database'] == EXPECTED_DATABASE and type(auth['installation_attempts']) is int and auth['installation_attempts'] == 1, 'AUTH_TARGET')
    need(re.fullmatch(r'[a-f0-9]{32}', auth['operation_id']) and re.fullmatch(r'[a-f0-9]{64}', auth['instance_sha256']), 'AUTH_IDENTIFIERS')
    # Parent independently checks timestamp and actual Azure App instance.
    return auth['operation_id']

def stage(root: Path, archive: Path):
    with zipfile.ZipFile(archive) as z:
        for member in z.infolist():
            name = member.filename
            dest = root / name
            fd = os.open(dest, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, 'O_NOFOLLOW', 0), 0o600)
            with os.fdopen(fd, 'wb') as f:
                f.write(z.read(name))
    return root / 'p3_kudu_cloudshell_parent.py'

def preclaim_parent_gates(root: Path, auth: Path, archive: Path):
    """Read-only original Parent management + short-lived auth check BEFORE claim.

    This does not prove independent Owner authorization or live transport safety.
    It deliberately invokes no SQL/SSH/Blob/installer operation. Any failure HOLDs.
    """
    path = root / 'p3_kudu_cloudshell_parent.py'
    # Load only the pinned, hash-validated Parent from the fresh private stage.
    import sys as _sys
    import importlib.util as _iu
    original_path = list(_sys.path)
    try:
        _sys.path.insert(0, str(root))
        spec = _iu.spec_from_file_location('_fv_g1_preclaim_parent', path)
        need(spec is not None and spec.loader is not None, 'PARENT_IMPORT')
        module = _iu.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.validate_local_source(root)
        _, binding_sha = module.validate_binding(root)
        cfg = module.load(root / 'executor_config.json')
        # Original Parent performs the same checks again after handoff.
        instance, _facts = module.management(cfg)
        module.validate_authorization(str(auth), EXPECTED_ARCHIVE, binding_sha, instance)
        need(sha(archive.read_bytes()) == EXPECTED_ARCHIVE, 'CANDIDATE_RACE')
        secure_regular(auth, private=True)
    except Exception as exc:
        raise GateHold('PRECLAIM_MANAGEMENT_OR_AUTH_HOLD') from exc
    finally:
        _sys.path[:] = original_path
        for name in list(_sys.modules):
            if name == '_fv_g1_preclaim_parent' or name.startswith('p3_') or name.startswith('g1_'):
                mod = _sys.modules.get(name)
                if mod is not None and str(getattr(mod,'__file__','')).startswith(str(root) + os.sep):
                    del _sys.modules[name]
    return True

def consume_claim(directory: Path, operation_id: str):
    info = directory.lstat()
    need(stat.S_ISDIR(info.st_mode) and info.st_uid == os.geteuid() and
         stat.S_IMODE(info.st_mode) == 0o700, 'CLAIM_DIR')
    claim = directory / ('g1-' + operation_id + '.consumed')
    try:
        fd = os.open(claim, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os,'O_NOFOLLOW',0), 0o600)
    except FileExistsError as e:
        raise GateHold('ALREADY_CONSUMED') from e
    with os.fdopen(fd, 'w') as f:
        f.write('CONSUMED_BEFORE_PARENT_LAUNCH\n')
        f.flush()
        os.fsync(f.fileno())
    return claim

def run(args):
    archive = Path(args.candidate).expanduser().absolute()
    count = inspect_candidate(archive)
    if not args.live:
        print('G1_LOCAL_INTEGRITY=PASS')
        print('G1_ZIP_MEMBERS=' + str(count))
        print('G1_PARENT=NOT_LAUNCHED')
        print('G1_LIVE=NOT_AUTHORIZED')
        return 0
    need(bool(args.authorization) and bool(args.claim_dir) and args.owner_confirm == 'EXACT_G1_ONE_SHOT', 'EXPLICIT_APPROVAL_REQUIRED')
    auth = Path(args.authorization).expanduser().absolute()
    opid = validate_auth_file(auth)
    claim_directory = Path(args.claim_dir).expanduser().absolute()
    # The Parent revalidates true Owner, Azure target, App instance, auth time,
    # source binding, historical claims and the original sealed SQL package.
    source_root = Path(__file__).resolve().parent
    tools_source = source_root / 'g1_preclaim_tools.py'
    wrapper = source_root / 'g1_preverified_parent.py'
    need(sha(tools_source.read_bytes()) == EXPECTED_PRECLAIM_TOOLS,
         'PRECLAIM_TOOLS_SOURCE_HASH')
    need(sha(wrapper.read_bytes()) == EXPECTED_PREVERIFIED_PARENT,
         'PREVERIFIED_PARENT_SOURCE_HASH')
    with tempfile.TemporaryDirectory(prefix='fv-g1-exact-', dir=None) as tmp:
        temp_root = Path(tmp)
        candidate_root = temp_root / 'candidate'
        cache_root = temp_root / 'tool-cache'
        candidate_root.mkdir(mode=0o700)
        cache_root.mkdir(mode=0o700)
        parent = stage(candidate_root, archive)
        need(sha(parent.read_bytes()) == json.loads((candidate_root/'binding-index.json').read_text())['files']['p3_kudu_cloudshell_parent.py'], 'STAGED_PARENT_HASH')
        # Do NOT consume the irrevocable local attempt for a failed management
        # or stale/incorrect instance authorization. No SQL/SSH/Blob here.
        preclaim_parent_gates(candidate_root, auth, archive)
        cfg = json.loads((candidate_root / 'executor_config.json').read_text('utf-8'))
        # All exact tool bytes are acquired and verified before the irreversible
        # claim. The original Parent later receives only these bytes through a
        # network-free provider; it cannot download or substitute tools.
        acquire_all(cfg['tools'], cache_root)
        print('G1_PRECLAIM_TOOLS=PASS', flush=True)
        need(validate_auth_file(auth) == opid, 'AUTH_RACE')
        consume_claim(claim_directory, opid)  # irrevocable after preclaim PASS
        print('G1_LOCAL_CLAIM=CONSUMED', flush=True)
        print('G1_PARENT_HANDOFF=STARTED', flush=True)
        rc = subprocess.call([
            sys.executable, str(wrapper),
            '--candidate-root', str(candidate_root),
            '--tool-cache', str(cache_root),
            '--authorization', str(auth),
            '--distribution', str(archive),
            '--artifact-sha256', EXPECTED_ARCHIVE,
        ], cwd=temp_root)
        print('G1_PARENT_EXIT=' + ('PASS' if rc == 0 else 'HOLD'), flush=True)
        print('AUTO_RETRY=FORBIDDEN', flush=True)
        return 0 if rc == 0 else 1

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--candidate', required=True)
    p.add_argument('--check', action='store_true')
    p.add_argument('--live', action='store_true')
    p.add_argument('--authorization')
    p.add_argument('--claim-dir')
    p.add_argument('--owner-confirm')
    a = p.parse_args()
    try:
        need(a.live != a.check, 'MODE_REQUIRED')
        return run(a)
    except ToolHold as exc:
        print('G1_HANDOFF=HOLD')
        print('G1_PRECLAIM_TOOLS=' + exc.code)
        print('G1_PARENT=NOT_LAUNCHED')
        print('G1_LOCAL_CLAIM=NOT_CONSUMED')
        print('AUTO_RETRY=FORBIDDEN')
        return 1
    except Exception:
        print('G1_HANDOFF=HOLD')
        print('AUTO_RETRY=FORBIDDEN')
        return 1

if __name__ == '__main__':
    sys.exit(main())
