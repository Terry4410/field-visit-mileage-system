"""Strict sanitized contract. This review package cannot certify G1 PASS."""
import hashlib,json,pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parent
SOURCE='88c21921860888f531a0962867f916c307452bbc'
RC='f29affb3299828f45b1f131321779e8bea2f86a8'
GROUPS=[['TARGET','VERSION_SHAPE','VISIBILITY'],['MARKER_010'],
 ['COMPLETE_FACET_EQUIVALENCE','ENABLED_INDEXES','HISTORICAL_COMPATIBILITY','REQUIRED_COLUMNS','SCOPED_CHECK_DEFINITIONS','SCOPED_COLUMN_FACETS','SCOPED_FK_MAPPINGS','SCOPED_INDEX_KEYS','TRUSTED_RELATIONSHIPS'],
 ['API_OPERATION_PERMISSION_MATRIX','API_PRINCIPAL_BINDING','NO_CREATE_TABLE','NO_DATABASE_ALTER','NO_DATABASE_CONTROL','NO_REGISTRY_DELETE','NO_REGISTRY_INSERT','NO_REGISTRY_UPDATE','NO_SCHEMA_ALTER']]
UNPROVEN={'COMPLETE_FACET_EQUIVALENCE','HISTORICAL_COMPATIBILITY','API_OPERATION_PERMISSION_MATRIX','API_PRINCIPAL_BINDING'}
def pairs(items):
    out={}
    for k,v in items:
        if k in out: raise ValueError('duplicate')
        out[k]=v
    return out
def load(path):
    return json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'),object_pairs_hook=pairs)
def pins():
    m=load(ROOT/'manifest.json')
    if m['source']!=SOURCE or m['rc']!=RC: raise ValueError('source')
    for name,digest in m['files'].items():
        if pathlib.PurePosixPath(name).name!=name: raise ValueError('path')
        if hashlib.sha256((ROOT/name).read_bytes()).hexdigest()!=digest: raise ValueError('hash')
    return m
def validate(e,m,final=True):
    if set(e)!={'contract','source','rc','pins','groups'}: raise ValueError('envelope')
    if (e['contract'],e['source'],e['rc'],e['pins'])!=('G1-MINIMAL-REVIEW-3',SOURCE,RC,m['files']): raise ValueError('binding')
    if not isinstance(e['groups'],list) or not 1<=len(e['groups'])<=4: raise ValueError('groups')
    stopped=False
    for i,rows in enumerate(e['groups']):
        if stopped: raise ValueError('after-stop')
        if not isinstance(rows,list) or len(rows)!=len(GROUPS[i]): raise ValueError('rows')
        if any(not isinstance(r,dict) or set(r)!={'code','status'} for r in rows): raise ValueError('columns')
        if sorted(r['code'] for r in rows)!=GROUPS[i]: raise ValueError('codes')
        for r in rows:
            if r['status'] not in ('PASS','FAIL','UNKNOWN'): raise ValueError('status')
            if r['code'] in UNPROVEN and r['status']!='UNKNOWN': raise ValueError('false-proof')
        # Only identity/visibility/shape and marker are execution prerequisites.
        # Known catalog FAIL/UNKNOWN is evidence, not permission to release;
        # independent permission SELECTs may still collect on the guarded connection.
        stopped=i<2 and any(r['status']!='PASS' for r in rows)
    if final and not stopped and len(e['groups'])<4: raise ValueError('incomplete')
    return 'FAIL' if any(r['status']=='FAIL' for g in e['groups'] for r in g) else 'UNKNOWN'
if __name__=='__main__':
    try:
        m=pins(); print('FORMAT_PASS; G1='+validate(load(sys.argv[1]),m,final='--prefix' not in sys.argv[2:]))
    except Exception:
        print('FORMAT_FAIL; G1=UNKNOWN; Details=Suppressed');sys.exit(1)
