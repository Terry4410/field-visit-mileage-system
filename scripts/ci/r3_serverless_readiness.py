"""Bounded management-plane readiness; one separately approved connection-only wake.
No Azure access on import. No SQL text. stdout/stderr from child processes are captured.
"""
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

SERVER = 'sql-fieldvisit-jpe-uat'
DATABASE = 'db-fieldvisit-uat'
GROUP = 'rg-fieldvisit-uat'
REPOSITORY = 'Terry4410/field-visit-mileage-system'
REF = 'refs/heads/post-uat/v1.8.0'
MAX_SECONDS = 300
MAX_READS = 13
INTERVAL = 10

class Stop(Exception):
    pass

def validate_source(env):
    if (env.get('GITHUB_ACTIONS') != 'true' or env.get('RUNNER_DEBUG') == '1' or
        env.get('GITHUB_REPOSITORY') != REPOSITORY or env.get('GITHUB_REF') != REF or
        not re.fullmatch('[0-9a-f]{40}', env.get('GITHUB_SHA','')) or
        env.get('R3_APPROVED_SHA') != env.get('GITHUB_SHA')):
        raise Stop('PrerequisiteRejected')
    if not re.fullmatch(r'[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}',env.get('R3_SUBSCRIPTION_ID','')):
        raise Stop('PrerequisiteRejected')
    if env.get('R3_AUTHORIZE_WAKE') not in ('true','false') or env.get('R3_ACCEPT_CHARGES') not in ('true','false'):
        raise Stop('PrerequisiteRejected')

def state_from_metadata(data, subscription):
    expected=f'/subscriptions/{subscription}/resourceGroups/{GROUP}/providers/Microsoft.Sql/servers/{SERVER}/databases/{DATABASE}'
    if not isinstance(data,dict) or data.get('name') != DATABASE or data.get('id') != expected:
        raise Stop('TargetGuardRejected')
    state=data.get('status')
    if state not in ('Online','Paused','Resuming'):
        raise Stop('UnavailableState')
    return state

def run_readiness(read, wake, authorized=False, accept_charges=False, clock=time.monotonic, sleep=time.sleep):
    """Pure state controller. Only verified Online can pass. Never invokes R3 itself."""
    deadline=clock()+MAX_SECONDS
    wake_used=False
    for index in range(MAX_READS):
        if clock()+10 >= deadline: raise Stop('ReadinessTimeout')
        state=read()
        if clock() >= deadline: raise Stop('ReadinessTimeout')
        if state == 'Online':
            # Independent fresh management GET, including after wake/resuming.
            if clock()+10 >= deadline: raise Stop('ReadinessTimeout')
            if read() != 'Online': raise Stop('OnlineRecheckRejected')
            if clock() >= deadline: raise Stop('ReadinessTimeout')
            return wake_used
        if state == 'Paused':
            if not authorized or not accept_charges: raise Stop('WakeNotAuthorized')
            if wake_used: raise Stop('PausedAfterWake')
            if clock()+140 >= deadline: raise Stop('ReadinessTimeout')
            wake_used=True
            if wake() not in ('Opened','ResumeSignaled'): raise Stop('WakeRejected')
        elif state != 'Resuming':
            raise Stop('UnavailableState')
        if index == MAX_READS-1 or clock()+INTERVAL+10 >= deadline: raise Stop('ReadinessTimeout')
        sleep(INTERVAL)
    raise Stop('ReadinessTimeout')

def captured(command, timeout):
    try:
        result=subprocess.run(command,capture_output=True,text=True,timeout=timeout,check=False)
    except subprocess.TimeoutExpired:
        raise Stop('ReadinessTimeout') from None
    except Exception:
        raise Stop('ManagementAccessRejected') from None
    if result.returncode != 0: raise Stop('ManagementAccessRejected')
    return result.stdout

def azure_read(subscription):
    raw=captured(['az','sql','db','show','--subscription',subscription,'--resource-group',GROUP,
                  '--server',SERVER,'--name',DATABASE,'--query','{id:id,name:name,status:status}',
                  '--output','json','--only-show-errors'],10)
    try: data=json.loads(raw)
    except Exception: raise Stop('MetadataContractRejected') from None
    return state_from_metadata(data,subscription)

def connection_only_wake():
    script=Path(__file__).with_name('r3_connection_only_wake.ps1')
    # Token remains inside the PowerShell process. No token/connection string argument.
    raw=captured(['pwsh','-NoProfile','-NonInteractive','-File',str(script)],130).strip()
    if raw not in ('Opened','ResumeSignaled'): raise Stop('WakeRejected')
    return raw

def main():
    source=os.environ.get('GITHUB_SHA','')
    if not re.fullmatch('[0-9a-f]{40}',source): source='0'*40
    try:
        validate_source(os.environ)
        used=run_readiness(lambda: azure_read(os.environ['R3_SUBSCRIPTION_ID']), connection_only_wake,
            os.environ['R3_AUTHORIZE_WAKE']=='true',os.environ['R3_ACCEPT_CHARGES']=='true')
        print(f'Readiness=PASS; Wake={"UsedOnce" if used else "NotUsed"}; SourceSha={source};')
        return 0
    except Stop as failure:
        # Only our closed enum can reach the log, never external exception text.
        allowed={'PrerequisiteRejected','TargetGuardRejected','UnavailableState','ReadinessTimeout',
                 'WakeNotAuthorized','PausedAfterWake','WakeRejected','OnlineRecheckRejected',
                 'ManagementAccessRejected','MetadataContractRejected'}
        code=str(failure) if str(failure) in allowed else 'PrerequisiteRejected'
    except Exception:
        code='PrerequisiteRejected'
    print(f'Readiness=STOP; Classification={code}; SourceSha={source};')
    return 1

if __name__=='__main__':
    raise SystemExit(main())
