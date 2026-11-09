#!/usr/bin/env python3
"""Child half of proposed Owner Entra private-SQL one-shot; REVIEW ONLY.
No code path executes until a separately reviewed, authenticated parent has securely
provided a token through a no-echo TTY. Never run this file directly in Azure.
"""
import hashlib
import getpass
import shutil
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import sys
import tempfile
import time
import termios
from owner_catalog_durable_receipt_REVIEW_ONLY import store as durable_store

BASE = Path('/tmp/fv-pathb-p3-kudu-install-d6c2208-v3-work/tools')
SQLCMD = BASE/'opt/mssql-tools18/bin/sqlcmd'
DRIVER = BASE/'opt/microsoft/msodbcsql18/lib64/libmsodbcsql-18.6.so.1.1'
EXPECTED = {
    SQLCMD:'4234f2b7db28e68b3f216712ab9fb016229823c111e008dc97749a8ae9190609',
    DRIVER:'bfc10e264cf1b065485a472bb8186a57566a0b98cbf6712bf2fad48b6a9f369c',
    BASE/'usr/lib/x86_64-linux-gnu/libltdl.so.7.3.2':
        'eefec20eb69408b2c76922cf95ee307f89d8ce1d33890689631cb0086fecc17e',
    BASE/'usr/lib/x86_64-linux-gnu/libodbc.so.2.0.0':
        '94de4cd1acf7718eaf88873af551b92beee6a0643e21ad3d1726bc6fd8fb4619',
    BASE/'usr/lib/x86_64-linux-gnu/libodbcinst.so.2.0.0':
        'ef8c8cf9be0885fac22a101a3e8075b598ab6e1efdf78f3b2754e748708e884c',
}
SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DB = 'db-fieldvisit-pathb-uat'
# The claim MUST NOT be cleared on failure. A new separate Blob claim is required
# before this child can ever be live approved, because /home may be CIFS.
CLAIM_PREFIX = '/home/LogFiles/fv-pathb-owner-catalog-r4-'

SQL_SHA = 'd5a4f5825cd95227661921ed8577882053998f0fb103c49b64658f69ccca39b0'


def stat_regular_sha(p, want):
    st = p.lstat()
    return stat.S_ISREG(st.st_mode) and not p.is_symlink() and hashlib.sha256(p.read_bytes()).hexdigest() == want


def sanitize_sql_result(raw):
    """Conservative catalog classification: never turn a partial install into UAT GO.

    This is only a first-stage metadata inventory, NOT schema fingerprint acceptance.
    """
    lines = [line.strip() for line in raw.decode('utf-8', 'replace').splitlines() if line.strip()]
    if len(lines) != 1:
        return 'HOLD_RESULT_SHAPE'
    parts = lines[0].split('|')
    if len(parts) != 14 or parts[0] != 'FV_CAT':
        return 'HOLD_RESULT_SHAPE'
    if any(x not in ('PASS', 'HOLD') for x in parts[1:5]):
        return 'HOLD_RESULT_SHAPE'
    if any(not re.fullmatch(r'\d{1,9}', x) for x in parts[5:9]):
        return 'HOLD_RESULT_SHAPE'
    if any(x != 'NA' and not re.fullmatch(r'\d{1,9}', x) for x in parts[9:11]):
        return 'HOLD_RESULT_SHAPE'
    if any(not re.fullmatch(r'\d{1,9}', x) for x in parts[11:]):
        return 'HOLD_RESULT_SHAPE'
    if 'HOLD' in parts[1:5]:
        return 'HOLD_SQL_TARGET_OR_METADATA'
    tables, dbo, versions_exists, ledger_exists = map(int, parts[5:9])
    if versions_exists not in (0, 1) or ledger_exists not in (0, 1):
        return 'HOLD_RESULT_SHAPE'
    if dbo > tables:
        return 'HOLD_RESULT_INCONSISTENT'
    all_versions, required_distinct = parts[9:11]
    problems = [int(x) for x in parts[11:14]]
    if tables == 0:
        if dbo == 0 and versions_exists == 0 and ledger_exists == 0 and all_versions == required_distinct == 'NA' and not any(problems):
            return 'EMPTY_CATALOG_CANDIDATE'
        return 'HOLD_PARTIAL_OR_INCONSISTENT'
    if versions_exists != 1 or all_versions == 'NA' or required_distinct == 'NA':
        return 'HOLD_PARTIAL_OR_INCONSISTENT'
    if tables != 69 or dbo != 69 or int(required_distinct) != 3 or any(problems):
        return 'HOLD_PARTIAL_OR_INCONSISTENT'
    if int(all_versions) < 3:
        return 'HOLD_RESULT_INCONSISTENT'
    return 'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED'


def terminal(status, operation_id, candidate_sha, sql_attempted):
    """Persist the sanitized result before reporting it; never overwrite or replay."""
    try:
        durable_store(operation_id, candidate_sha, status, sql_attempted)
    except Exception:
        print('OWNER_SQL_CATALOG=HOLD_RECEIPT_PERSISTENCE\nINSTALLER=NOT_RUN\nDDL=NO\nRETRY=FORBIDDEN', flush=True)
        return False
    print('OWNER_SQL_CATALOG=' + status + '\nINSTALLER=NOT_RUN\nDDL=NO\nRETRY=FORBIDDEN', flush=True)
    return True


def verify_noecho_tty_and_signal(stdin=None, stdout=None):
    """Fail closed unless a real TTY has ECHO disabled before token request.

    Parent expects the acknowledgement as a complete line, followed by the
    exact getpass readiness prompt. Does not consume/request any token here.
    """
    stdin = sys.stdin if stdin is None else stdin
    stdout = sys.stdout if stdout is None else stdout
    try:
        fd = stdin.fileno()
        if not os.isatty(fd) or not os.isatty(stdout.fileno()):
            return False
        if termios.tcgetattr(fd)[3] & termios.ECHO:
            return False
        stdout.write('FV_OWNER_ECHO_OFF=PASS\n')
        stdout.flush()
        return True
    except (OSError, ValueError, termios.error):
        return False


def candidate_execute(sql_path, sql_sha, operation_id, candidate_sha, blob_claim_verified=False):
    """Reference execution path. No active caller. Requires two independent gates."""
    if not isinstance(operation_id, str) or not re.fullmatch(r'[0-9a-f]{32}', operation_id):
        return 1
    if not re.fullmatch(r'[0-9a-f]{64}', str(sql_sha)) or not re.fullmatch(r'[0-9a-f]{64}', str(candidate_sha)):
        return 1
    if not blob_claim_verified:
        terminal('HOLD_DURABLE_ONCE_GUARD', operation_id, candidate_sha, False)
        return 1
    claim = Path(CLAIM_PREFIX + operation_id + '.claim')
    receipt = Path('/home/LogFiles/fv-pathb-owner-catalog-r4-' + operation_id + '.json')
    try:
        if claim.exists() or receipt.exists():
            return 1
        if not stat_regular_sha(Path(sql_path),sql_sha):
            raise RuntimeError('SQL_HASH')
        if any(not stat_regular_sha(p,h) for p,h in EXPECTED.items()):
            raise RuntimeError('TOOLS_HASH')
        # Claim BEFORE token handling. This filesystem claim is secondary; only
        # external conditional-create Blob claim is the durable primary gate.
        fd=os.open(str(claim),os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(fd,'w') as f:f.write('CONSUMED\n')
    except Exception:
        terminal('HOLD_PRECHECK', operation_id, candidate_sha, False)
        return 1
    ram=None
    try:
        # Parent must have established an authenticated no-echo SSH TTY.
        # getpass does not echo token; never store in logs/env/argv.
        if not verify_noecho_tty_and_signal():
            raise RuntimeError('TTY_NOT_NOECHO')
        token=getpass.getpass('FV_OWNER_TOKEN_READY:')
        if not 100<=len(token)<=4096 or any(c in token for c in '\r\n'):
            raise RuntimeError('TOKEN_SHAPE')
        ram=Path(tempfile.mkdtemp(prefix='fv-owner-catalog-',dir='/dev/shm'))
        os.chmod(ram,0o700)
        tokenfile=ram/'token.utf16le'
        fd=os.open(str(tokenfile),os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(fd,'wb') as f:f.write(token.encode('utf-16le'))
        del token
        odbc=ram/'odbc';odbc.mkdir(mode=0o700)
        (odbc/'odbcinst.ini').write_text('[ODBC Driver 18 for SQL Server]\nDriver='+str(DRIVER)+'\nUsageCount=1\n')
        (odbc/'odbc.ini').write_text('')
        env={'PATH':'/usr/bin:/bin','HOME':str(ram),'LC_ALL':'C.UTF-8',
             'ODBCSYSINI':str(odbc),'ODBCINI':'odbc.ini','ODBCINSTINI':'odbcinst.ini',
             'LD_LIBRARY_PATH':':'.join((str(BASE/'opt/microsoft/msodbcsql18/lib64'),str(BASE/'usr/lib/x86_64-linux-gnu')))}
        args=[str(SQLCMD),'-S',SERVER,'-d',DB,'-I','-b','-l','30','-t','30',
              '-Nm','-G','-P',str(tokenfile),'-h','-1','-W','-w','65535',
              '-i',str(sql_path)]
        sql_attempted = True  # fail closed once process launch is attempted
        proc=subprocess.Popen(args,stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE,env=env,start_new_session=True)
        try:
            stdout,stderr=proc.communicate(timeout=75)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid,signal.SIGKILL);proc.communicate()
            raise RuntimeError('SQL_TIMEOUT')
        if proc.returncode!=0:
            # Do not print stderr or stdout, not even a filtered excerpt.
            terminal('HOLD_SQL_LOGIN_OR_QUERY', operation_id, candidate_sha, True)
            return 1
        result=sanitize_sql_result(stdout)
        saved = terminal(result, operation_id, candidate_sha, True)
        return 0 if saved and result in ('EMPTY_CATALOG_CANDIDATE', 'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED') else 1
    except Exception:
        terminal('HOLD_OPERATION', operation_id, candidate_sha, bool(locals().get('sql_attempted', False)))
        return 1
    finally:
        if ram and ram.exists() and not ram.is_symlink():
            try:shutil.rmtree(ram)
            except OSError:pass


def main():
    # Hard nondeploy lock. Must never remove without review and new exact SHA authorization.
    print('OWNER_SQL_CATALOG=HOLD_REVIEW_ONLY_NOT_EXECUTABLE')
    return 1

if __name__ == '__main__':sys.exit(main())
