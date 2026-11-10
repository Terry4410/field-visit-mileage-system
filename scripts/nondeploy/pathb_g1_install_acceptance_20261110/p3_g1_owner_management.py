"""Only the read-only Azure Owner management and pinned package download helpers
required by the new G1 Parent. No legacy SSH passwords or live CLI entrypoint.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import urllib.parse
import urllib.request

from p3_ssh_transport_contract import RG


class ManagementHold(Exception):
    pass


def _require(v):
    if not v:raise ManagementHold('G1_MANAGEMENT_HOLD')


def _run(args,timeout=90,parse=False):
    try:
        r=subprocess.run(args,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,
                         timeout=timeout,check=True)
        return json.loads(r.stdout) if parse else r.stdout
    except Exception:raise ManagementHold('G1_MANAGEMENT_HOLD') from None


def az(args,timeout=60):
    return _run(['az',*args,'-o','json','--only-show-errors'],timeout,True)


def download_once(url,path,expected):
    from p3_install_gates import digest
    u=urllib.parse.urlsplit(url)
    _require(u.scheme=='https' and u.hostname in {'packages.microsoft.com','deb.debian.org'}
             and not u.username and not u.password and not u.fragment
             and isinstance(expected,str) and len(expected)==64)
    # Reject redirects to an unpinned host and oversized opaque responses.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self,*args,**kwargs):return None
    opener=urllib.request.build_opener(NoRedirect())
    try:
        response=opener.open(urllib.request.Request(url,headers={
            'User-Agent':'fieldvisit-g1-original-install/1'}),timeout=90)
        data=response.read(75_000_001)
    except Exception:raise ManagementHold('G1_DOWNLOAD_HOLD') from None
    _require(len(data)<=75_000_000 and digest(data)==expected)
    path=Path(path)
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
    with os.fdopen(fd,'wb') as f:f.write(data);f.flush();os.fsync(f.fileno())


def blob_download(cfg,name,path):
    _require(cfg.get('storage_account')=='stfvsc261026a' and
             cfg.get('storage_container')=='schema-evidence' and
             name in {cfg.get('package_blob'),cfg.get('seal_blob')})
    _run(['az','storage','blob','download','--account-name','stfvsc261026a',
      '--container-name','schema-evidence','--name',name,'--file',str(path),
      '--auth-mode','login','--only-show-errors','-o','none'],timeout=120)
    path=Path(path)
    _require(path.is_file() and not path.is_symlink())
    path.chmod(0o600)


def private_endpoint_ip(sql_server_id):
    """Exact SQL-server-linked PE/NIC, never an IP guess or wildcard fallback."""
    _require(isinstance(sql_server_id,str) and sql_server_id.startswith('/subscriptions/'))
    endpoints=az(['network','private-endpoint','list','-g',RG])
    _require(isinstance(endpoints,list))
    matches=[]
    for pe in endpoints:
        links=(pe.get('privateLinkServiceConnections') or [])+(pe.get('manualPrivateLinkServiceConnections') or [])
        for link in links:
            state=link.get('privateLinkServiceConnectionState') or {}
            if (str(link.get('privateLinkServiceId','')).lower()==sql_server_id.lower() and
                link.get('groupIds')==['sqlServer'] and state.get('status')=='Approved'
                and pe.get('provisioningState')=='Succeeded'):
                matches.append(pe)
    _require(len(matches)==1 and matches[0].get('name')=='pe-sql-fieldvisit-pathb-uat')
    nics=matches[0].get('networkInterfaces') or []
    _require(len(nics)==1 and isinstance(nics[0].get('id'),str))
    nic=az(['network','nic','show','--ids',nics[0]['id']])
    _require(nic.get('id','').lower()==nics[0]['id'].lower() and
             nic.get('provisioningState')=='Succeeded')
    ipcs=nic.get('ipConfigurations') or []
    _require(len(ipcs)==1 and isinstance(ipcs[0],dict))
    ipc=ipcs[0]
    alloc=ipc.get('privateIPAllocationMethod',ipc.get('privateIpAllocationMethod'))
    ip=ipc.get('privateIPAddress',ipc.get('privateIpAddress'))
    _require(alloc in ('Dynamic','Static') and ip=='10.90.2.4')
    return ip
