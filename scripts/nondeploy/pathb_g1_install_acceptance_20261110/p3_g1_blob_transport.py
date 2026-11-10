"""One-shot G1 Blob receipt HTTPS transport. NOT a standalone runner.

Requires a 0600 Owner-only RAM token prepared by the authorized App Child.
No redirects/retries/requests outside one fixed Azure Storage account and exact
operation-specific BEFORE/AFTER object namespace.
"""
import base64
import email.utils
import http.client
import json
import os
from pathlib import Path
import re
import ssl
import stat
import time

from g1_blob_call_journal_REVIEW_ONLY import PREFIX, BLOB_ACCOUNT, CONTAINER
from p3_blob_guard import OWNER_OID_SHA, TENANT_SHA
from p3_install_gates import digest


class BlobTransportHold(Exception):
    pass


def _validate_token(token):
    try:
        parts=token.split('.')
        if len(parts)!=3 or not all(parts): raise ValueError()
        claims=json.loads(base64.urlsafe_b64decode(parts[1]+'==='))
        now=time.time()
        # These are security gates, not programmer assertions.  Keep them
        # active even when Python is launched with -O/PYTHONOPTIMIZE.
        if digest(str(claims['oid']).lower().encode())!=OWNER_OID_SHA:
            raise ValueError()
        if digest(str(claims['tid']).lower().encode())!=TENANT_SHA:
            raise ValueError()
        if str(claims['aud']).rstrip('/')!='https://storage.azure.com':
            raise ValueError()
        if float(claims.get('nbf',0))>now+30:
            raise ValueError()
        if float(claims['exp'])-now<100:
            raise ValueError()
    except Exception:
        raise BlobTransportHold('OWNER_STORAGE_TOKEN_HOLD') from None


def build_request_from_private_storage_token(path):
    try:
        path=Path(path);st=path.lstat()
        if not(st.st_uid==os.geteuid() and stat.S_ISREG(st.st_mode)
               and stat.S_IMODE(st.st_mode)==0o600 and st.st_size<16384
               and str(path).startswith('/dev/shm/fv-pathb-g1-original-installer-20261110/')):
            raise BlobTransportHold('PRIVATE_TOKEN_FILE_HOLD')
        token=path.read_text('ascii')
        _validate_token(token)
    except BlobTransportHold:raise
    except Exception:raise BlobTransportHold('PRIVATE_TOKEN_FILE_HOLD') from None

    def request(method,key,body,headers):
        # Exactly 16 BEFORE and 16 AFTER objects at a single exclusive new op.
        if (method not in ('PUT','GET') or not isinstance(key,str)
            or re.fullmatch(re.escape(PREFIX)+r'[0-9a-f]{32}/(?:(?:0[0-9]|1[0-5])-(?:before|after)|postcheck)\.json',key) is None
            or (method=='PUT' and
                (headers.get('If-None-Match')!='*' or not isinstance(body,bytes)
                 or len(body)>1024))
            or (method=='GET' and body!=b'')):
            raise BlobTransportHold('G1_RECEIPT_TARGET_HOLD')
        _validate_token(token)
        request_headers={
            'Authorization':'Bearer '+token,
            'x-ms-version':'2023-11-03',
            'x-ms-date':email.utils.formatdate(usegmt=True),
        }
        if method=='PUT':
            request_headers.update({'If-None-Match':'*','x-ms-blob-type':'BlockBlob',
                       'Content-Type':'application/json','Content-Length':str(len(body))})
        conn=http.client.HTTPSConnection(BLOB_ACCOUNT+'.blob.core.windows.net',
                   timeout=18,context=ssl.create_default_context())
        try:
            conn.request(method,'/'+CONTAINER+'/'+key,body=body,headers=request_headers)
            r=conn.getresponse()
            raw=r.read(2049)
            if len(raw)>2048:raise BlobTransportHold('G1_RECEIPT_TOO_LARGE')
            return r.status,r.getheader('ETag',''),raw
        except BlobTransportHold:raise
        except Exception:raise BlobTransportHold('G1_RECEIPT_TRANSPORT_HOLD') from None
        finally:conn.close()
    return request
