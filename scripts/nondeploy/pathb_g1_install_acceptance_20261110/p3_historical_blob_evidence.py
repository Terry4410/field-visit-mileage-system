"""Option C: present-day read-only predecessor check, NOT historical provenance.

Existing SELECT22 v2/v3 objects have no retroactively provable immutability.
Only verify current authenticated HEAD/GET ETag and frozen body contract.
Never treat them as proof of prior Parent execution or historical immutability.
Installer's separate create-only Blob guard and live pre-DDL gates stay mandatory.
"""
import json
import time
from p3_blob_guard import (ACCOUNT, CONTAINER, PREDECESSORS,
                           OneTimeInstallBlobGuard, pinned_http_request, require,
                           InstallGuardHold)

class HistoricalEvidenceHold(Exception):
    pass

def must(ok, code):
    if not ok:
        raise HistoricalEvidenceHold(code)

def observed_blob_etag(obj, expected_name):
    """Return read-only HEAD ETag; never infer immutable historical provenance.

    Azure currently reports no legal hold or locked retention for either predecessor.
    Content is validated independently by the unchanged authenticated GET guard.
    """
    must(isinstance(obj, dict) and obj.get('name') == expected_name,
         'HISTORICAL_BLOB_IDENTITY')
    props = obj.get('properties') or {}
    must(isinstance(props, dict), 'HISTORICAL_PROPERTIES_UNKNOWN')
    etag = obj.get('etag') or props.get('etag')
    must(isinstance(etag, str) and len(etag) > 2, 'HISTORICAL_ETAG_MISSING')
    return etag

def verify_readonly(cfg, azure_json, storage_token, *, now=None, request=None):
    """Check two exact current objects and authenticated content + ETag.

    azure_json(args)-> parsed JSON from an authenticated, read-only Azure CLI.
    request is injectable ONLY for unit tests. Production uses pinned TLS REST.
    No Blob PUT/DELETE or non-GET request is made here.
    """
    must(cfg.get('storage_account')==ACCOUNT and cfg.get('storage_container')==CONTAINER,
         'HISTORICAL_STORAGE_TARGET')
    now=time.time() if now is None else now
    expected={}
    for name,_ in PREDECESSORS:
        record=azure_json(['storage','blob','show','--account-name',ACCOUNT,
                           '--container-name',CONTAINER,'--name',name,
                           '--auth-mode','login'])
        expected[name]=observed_blob_etag(record,name)
    actual_request=pinned_http_request(storage_token) if request is None else request
    def safe_request(method,name,headers,body=b''):
        must(method=='GET' and name in expected and not body,'HISTORICAL_READ_ONLY')
        res=actual_request(method,name,headers,body)
        must(getattr(res,'etag',None)==expected[name], 'HISTORICAL_ETAG_MISMATCH')
        return res
    guard=OneTimeInstallBlobGuard(storage_token,request=safe_request)
    guard.check_predecessors()  # Existing frozen semantic/identity integrity checks
    must(guard.predecessors_verified,'HISTORICAL_CONTENT_NOT_VERIFIED')
    return {'select22_blob_records':2,'current_head_get_contract':'PASS',
            'historical_immutability_proven':False,
            'exact_owner_sql_release_and_query':'PASS',
            'historical_parent_execution_proven':False}
