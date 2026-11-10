"""App-side P3 SSH-stdin stage receiver, a NONDEPLOY library.

No CLI/execution entrypoint and no SQL/Blob/Token acquisition. The parent must
pin the exact stage archive SHA256, use an authenticated/pinned SSH channel,
and require an exact fresh authorisation before attaching any live runner.
The package inside MUST be the original sealed Path B ZIP.
"""
import hashlib
import io
import os
import tempfile
from pathlib import Path
import re
import stat
import zipfile
from p3_install_gates import validate_original_package, digest
from p3_blob_guard import InstallGuardHold

STAGE_ROOT = '/tmp/fv-pathb-p3-install-once2-stage-20261103'
STAGE_FILES = frozenset({
    'original.zip', 'original.seal.json', 'frozen_install.py.snapshot',
    'token_adapter.py', 'preflight.sql', 'p3_blob_guard.py',
    'p3_install_gates.py', 'p3_ssh_transport_contract.py',
    'seal_verifier.py', 'postcheck.sql', 'schema-fingerprint.sql',
})
MAX_BYTES = 250000

def stop(code):raise InstallGuardHold(code)

def strict_seal_crossbinding(original_zip, original_seal):
    """Use exact original PR #84 Seal verifier; do NOT weaken 34/33 gates."""
    from p3_install_gates import (LAYER_NAMES, LAYER_HASHES, RELEASE, SERVER,
         DATABASE, ORIGINAL_ZIP_SHA, MANIFEST_SHA, INSTALLER_SHA)
    from seal_verifier import verify
    cfg = {'blob_object_name': 'pathb-sql-install-package-d6c220877b9976b6-20261027.zip',
           'seal_package_file': 'pathb-sql-install-package.zip',
           'package_sha256': ORIGINAL_ZIP_SHA,
           'seal_sha256': 'a37dc3c7fbf1f805639fb72a76cc788bd9bec1f0f9933e07909e2eacae5c4a5f',
           'manifest_sha256': MANIFEST_SHA, 'installer_sha256': INSTALLER_SHA,
           'release': RELEASE, 'server': SERVER, 'database': DATABASE,
           'layers': [{'file': 'pathb-uat-sql/' + n, 'sha256': h}
                      for n,h in zip(LAYER_NAMES,LAYER_HASHES)]}
    with tempfile.TemporaryDirectory(prefix='fv-p3-seal-verify-',dir='/tmp') as d:
        a=Path(d)/'original.zip';b=Path(d)/'original.seal.json'
        a.write_bytes(original_zip);b.write_bytes(original_seal)
        proof=verify(a,b,cfg,cfg['blob_object_name'])
    if proof.get('seal_files')!=34 or proof.get('checksum_files')!=33:
        stop('ORIGINAL_SEAL_FULL_VERIFICATION')
    return True

def stage_archive(raw, expected_sha, *, root=STAGE_ROOT, ssh_connection=None,
                  stdin_tty=False, original_verifier=validate_original_package):
    """Verifies the entire stage archive locally, then exclusively stages it.

    Not a live CLI.  Caller must independently check Seal 34/34 and checksum
    index 33/33 with the existing original pinned verifier before installing.
    Failure may leave an unused /tmp directory, never a database mutation.
    """
    if not ssh_connection or stdin_tty:stop('SSH_STDIN_REQUIRED')
    if not isinstance(expected_sha,str) or not re.fullmatch('[a-f0-9]{64}',expected_sha):stop('STAGE_SHA_SHAPE')
    if not isinstance(raw,bytes) or len(raw)>MAX_BYTES or digest(raw)!=expected_sha:stop('STAGE_SHA_MISMATCH')
    p=Path(root)
    if str(p)!=STAGE_ROOT:stop('STAGE_TARGET_REJECTED')
    if p.exists() or p.is_symlink():stop('STAGE_ALREADY_EXISTS')
    try:
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            infos=z.infolist()
            names=[x.filename for x in infos]
            if len(names)!=len(set(names)):stop('STAGE_DUPLICATE_NAMES')
            if set(names)!=(set(STAGE_FILES)|{'SHA256SUMS'}):stop('STAGE_FILE_SET')
            if any((Path(n).is_absolute() or '..' in Path(n).parts or
                    ((info.external_attr>>16)&0o170000)==0o120000 or
                    info.file_size>250000)
                    for info,n in zip(infos,names)):stop('STAGE_MEMBER_PATH_OR_SIZE')
            lines=z.read('SHA256SUMS').decode('ascii').splitlines()
            parsed=[line.split('  ',1) for line in lines if line.strip()]
            if len(parsed)!=len(STAGE_FILES):stop('STAGE_CONTROL_COUNT')
            if any(len(x)!=2 or not re.fullmatch('[a-f0-9]{64}',x[0]) for x in parsed):stop('STAGE_CONTROL_FORMAT')
            if set(n for d,n in parsed)!=STAGE_FILES or len({n for d,n in parsed})!=len(STAGE_FILES):stop('STAGE_CONTROL_NAMES')
            contents={name:z.read(name) for name in STAGE_FILES}
            for expected, name in parsed:
                if digest(contents[name])!=expected:stop('STAGE_CONTROL_HASH')
    except InstallGuardHold:raise
    except Exception as ex:raise InstallGuardHold('STAGE_ARCHIVE_INVALID') from ex
    # The original approved DB installer must be verified BEFORE any remote
    # write.  The stager does not perform schema updates or marker creation.
    if original_verifier(contents['original.zip'])!='ORIGINAL_14_LAYERS_PASS':stop('ORIGINAL_PACKAGE_NOT_VERIFIED')
    # In production the full Seal verifier is a second mandatory check.
    # The seal file bytes are compared to the independently pinned SHA below.
    if digest(contents['original.seal.json'])!='a37dc3c7fbf1f805639fb72a76cc788bd9bec1f0f9933e07909e2eacae5c4a5f':
        stop('ORIGINAL_SEAL_HASH')
    if digest(contents['frozen_install.py.snapshot'])!='b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f':stop('ORIGINAL_INSTALLER_HASH')
    if digest(contents['token_adapter.py'])!='703d04bffa6542d80a214bd257adb221f86fdc45aee38981267239fc913596e4':stop('ODBC_ADAPTER_HASH')
    if digest(contents['preflight.sql'])!='e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add':stop('SELECT_QUERY_HASH')
    if digest(contents['seal_verifier.py'])!='33d8bd044839a9b549a39eeeb2fd2616e0663bca0a7df41a78fdd49ab5480597':stop('SEAL_VERIFIER_HASH')
    if digest(contents['postcheck.sql'])!='dbfdc865f1998682a04e9b009398e3652a436aaeaa32d5cd362f333e1875b1ef':stop('POSTCHECK_HASH')
    if digest(contents['schema-fingerprint.sql'])!='3c18fc1cf67d298a53919fc824b03ae6c1bf014a97c754c2f7cc90335b096c18':stop('FINGERPRINT_HASH')
    try:
        strict_seal_crossbinding(contents['original.zip'],contents['original.seal.json'])
    except Exception as ex:raise InstallGuardHold('ORIGINAL_SEAL_34_33_VERIFICATION_HOLD') from ex
    # At this point no secret has been supplied and no query may be invoked.
    old=os.umask(0o077)
    try:
        p.mkdir(mode=0o700)
        st=p.stat()
        if st.st_uid!=os.geteuid() or stat.S_IMODE(st.st_mode)!=0o700:
            stop('STAGE_LOCAL_POSIX_OWNER_MODE')
        for name,data in contents.items():
            fd=os.open(p/name,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
            with os.fdopen(fd,'wb') as f:f.write(data)
    finally:
        os.umask(old)
    return {'stage':'PASS','files':len(STAGE_FILES),'tokens':0,'sql_calls':0,'blob_puts':0}

if __name__=='__main__':
    raise SystemExit('NONDEPLOY: P3 STAGING HAS NO LIVE ENTRYPOINT')
