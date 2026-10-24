"""Exact-source D0 runner: 2 ARM GETs, one main WSS session, ZERO SQL.

Independent workflow/consent from the still-unregistered full-G1 one-shot.
No argv/target override, app configuration, SQL token, .NET launch or credentials.
"""
import hashlib
import json
import os
from pathlib import Path
import secrets
import sys
from arm import APP, RG, CONTEXT_SHA256, Reader, Stop, context_valid, exchange_token, paths
from main_channel import DiscoverySession


def run(connect=None):
    session=None
    try:
        env=os.environ
        if (not context_valid(env) or env.get('G1_MODE')!='D0_VERSION_ONLY' or
            env.get('ACCEPT_COST')!='false' or env.get('G1_D0_GUARD')!='PASS' or sys.argv[1:]):raise Stop()
        context=[env.get(k,'').lower() for k in ('AZURE_TENANT_ID','AZURE_SUBSCRIPTION_ID','AZURE_CLIENT_ID')]
        if hashlib.sha256('\n'.join(context).encode()).hexdigest()!=CONTEXT_SHA256:raise Stop()
        full=paths(context[1]);site=full['api']
        instances=site.split('?')[0]+'/instances?api-version=2024-04-01'
        plan={'api':site,'instances':instances}
        token=exchange_token('https://management.azure.com/.default')
        reader=Reader(plan,token,collections=(instances,))
        pages={k:reader.get(p) for k,p in plan.items()}
        properties=pages['api'].get('properties',{})
        if (pages['api'].get('name')!=APP or properties.get('reserved') is not True or
            properties.get('state')!='Running'):raise Stop()
        if connect is None:
            import websocket
            if websocket.__version__!='1.8.0':raise Stop()
            connect=websocket.create_connection
        source=Path(__file__).with_name('main_probe.py').read_bytes()
        nonce=secrets.token_hex(32)
        session=DiscoverySession(pages['api'],pages['instances'],token,source,nonce,connect)
        result=session.observe()
        # No raw HTTP/PTY/provider text, process environment or identity IDs.
        print(json.dumps(dict(stage='D0_VERSION_ONLY',sourceSha=env['GITHUB_SHA'],armGets=reader.calls,
            sqlConnections=0,mainObservation=result,liveG1='NOT_EXECUTED',businessUat='HOLD'),sort_keys=True))
        return 0
    except Exception:
        print(json.dumps(dict(stage='D0_VERSION_ONLY',status='HOLD',details='SUPPRESSED',
            sqlConnections=0,liveG1='NOT_EXECUTED')))
        return 1
    finally:
        if session is not None:session.close()


if __name__=='__main__':sys.exit(run())
