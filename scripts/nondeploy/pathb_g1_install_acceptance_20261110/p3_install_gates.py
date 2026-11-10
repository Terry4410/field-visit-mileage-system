"""Pure fail-closed Path B installation gates. NOT a live operator.

Reuses the exact original sealed installer, query, ODBC adapter. This code
may be imported into a future independently authorised one-shot supervisor.
No network, DB, Azure access, or file mutation occurs on import.
"""
import hashlib
import io
import json
import re
import uuid
import zipfile
from pathlib import Path

from p3_blob_guard import InstallGuardHold

RELEASE = 'd6c220877b9976b6478d85b5171d4e06293595ae'
SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
SHORT_SERVER = 'sql-fieldvisit-jpe-pathb-uat'
DATABASE = 'db-fieldvisit-pathb-uat'
ORIGINAL_ZIP_SHA = '5a0c956d4ef5c88e0ff49d2d606ee9c1cb34e81e7f0c0f85b5aea8c5743e5828'
MANIFEST_SHA = 'e94c76952eebdf55e56ab6fffa9b1dd0dfc38ec1c4ae79cae36dfacb83c1a9c9'
INSTALLER_SHA = 'b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f'
ADAPTER_SHA = '703d04bffa6542d80a214bd257adb221f86fdc45aee38981267239fc913596e4'
SELECT_SHA = 'e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add'
OWNER_HASH = '2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604'
LAYER_NAMES = (
    'model-baseline.sql','00_schema_versions.sql','sql-only-core-tables.sql',
    'effective-source-triggers.sql','optionc-sql-only-candidates.sql',
    'sql-only-extra-indexes.sql','20_route_reason_check.sql',
    '007_source_checks.sql','001_004_source_checks.sql',
    '005_indexable_facets.sql','005_source_controls.sql',
    'optionc-final-target-checks.sql','40_clean_install_017_capability.sql',
    '60_final_functional_capabilities.sql',
)
LAYER_HASHES = (
 '8cce0c97b707c8c47012617fe037dd423ef7183720aef5d71eff54b4ccf829a7',
 '26ef6ee675130d39d9b78b0855bf023d90676a71f49aac4c34ae162a6236195e',
 'f88840befedaf4bdbe69aa6b5d32c3d431865b2ddc11b952a0b8991fc6a2095b',
 'fe33cff5940fe3c9d020aa93ff01380ab390b686c92dc0ac23bb7c77b2374bed',
 'ef0cb7d76201aedcc4681cbf6fa3ed9fd23a1dd06e3b873e8fcfaa1f21362798',
 '7098909416c18f708dc6360c5798b3864a6b904c9cb06a323cc4d6b8e71a7b6c',
 'f5d1353e6c1f06396d096501dbcec0b4418e377bd1cfcd0aca5c15166c4c39b0',
 'c70fc7f0327fe81de06cb2388601075232e359ba8b391e73be52645c9712a539',
 '71fd588421b0fda17734d300fadfe723038fbcaa11f4a0081bb22cbdfb9de4de',
 'ec40d911490c9872c7e53fa815113eaaeb70f483a87fb7d4f98cf7f4ebfa76de',
 'f3afb663ad44f70f590fb660ea37ef6d093d7305d22b9e71476caa7c27bd12a0',
 '119053a0c1f0dd77ab7912b96edbc492e29eb07e236dbe3e47f35d726cb56829',
 'c16b20dddd86afd25f380f239f2e9f293df30dbb47406af69e5ff34c1a3f9a65',
 '58c9e43f080fd939ecc07a4286404908568588780d6c375754035974d6b63396',
)
PAYLOAD_SHA256 = (
 'f0ebcc67ce71694172da1aa55504373c66c4a2315ea39de229503fd28a1a9faf',
 '4810ca544178027c89febdcde15d1a90a32dcb2f3352dc55df48a7c48c0f0e25',
 '63e2d5b88bea21c94aa393e7f745e2062ecd7b9eaa02b4f026fb65ea9583e0bf',
 '3b49c0dcaa01fd7ed29e3f0ca6335b70881a9a30ca97ee3c128aefe2b4f84f03',
 '23370af2871b1b07b249c74c00084769bffb28cb3fb84c0546c6639cba13f1c0',
 '2c09cfc710c590a1cf2a80c4d5326defc0cbdabb3fec0314a482749197ef4017',
 '6021775bb6d67e34b6c9d92a08bbc18f71c69ab6ec6c2c3bb2ab2ee33fc2c76b',
 '7df2f41b76c15b84d0aab64168e80d2938e31d9c1ef4223e70b43a9add889e8a',
 '3813f8437e18f29ddb27430c3d6d272f8c23977442730417cf201da129a865f6',
 '44f3c7444c26fd957eb08243cf9a14ed30372a8c9d33f12195830ccdab8228f9',
 'abe54422483f02c253041c890a4720a5fa88d9653fa6cfdd14f53ca96baf0d04',
 'c5fe5f04b5e3fdb3a2b04f89585f5b9cd60c2cad559dacf7681028f3071a5818',
 '5cbef97ae5200d4860488b38b1c094bce00d0408bc161beaa66bb2f5b82bf901',
 '519f031e7ae42c28adadf3c771560a0ae5893759199a90d58f50351313ceecec',
 'd11f98f6f4595c2fc38ec8b6bf746ab64abae9f754f4ff6a194031f351ac87b6',
 '871f2054367e184414412473a7dd0efc30f7f1256409e607c0a160d288cca3e8',
)
# Placeholder above is deliberately checked at import/testing time; all digests
# must be length 64 to avoid a deceptively 'complete' release candidate.

def digest(raw): return hashlib.sha256(raw).hexdigest()
def require(ok, code):
    if not ok: raise InstallGuardHold(code)

def validate_local_source(root):
    root=Path(root)
    require(digest((root/'frozen_install.py.snapshot').read_bytes())==INSTALLER_SHA, 'ORIGINAL_INSTALLER_HASH')
    require(digest((root/'token_adapter.py').read_bytes())==ADAPTER_SHA, 'ODBC_ADAPTER_HASH')
    require(digest((root/'preflight.sql').read_bytes())==SELECT_SHA, 'FIXED_SELECT_HASH')
    require(digest((root/'seal_verifier.py').read_bytes())=='33d8bd044839a9b549a39eeeb2fd2616e0663bca0a7df41a78fdd49ab5480597', 'FULL_SEAL_VERIFIER_HASH')
    require(digest((root/'postcheck.sql').read_bytes())=='dbfdc865f1998682a04e9b009398e3652a436aaeaa32d5cd362f333e1875b1ef', 'POSTCHECK_HASH')
    require(digest((root/'schema-fingerprint.sql').read_bytes())=='3c18fc1cf67d298a53919fc824b03ae6c1bf014a97c754c2f7cc90335b096c18', 'SCHEMA_FINGERPRINT_HASH')
    require(len(LAYER_HASHES)==14 and len(LAYER_NAMES)==14 and len(PAYLOAD_SHA256)==16, 'BINDING_COUNT')
    require(all(re.fullmatch('[a-f0-9]{64}', x) for x in LAYER_HASHES+PAYLOAD_SHA256), 'BINDING_DIGEST_FORMAT')
    return True

def verify_sql_target(server, database):
    require(isinstance(server,str) and server in (SERVER,SHORT_SERVER) and database==DATABASE, 'SQL_TARGET')
    return True

def strict_row_22(raw):
    """Safe 22-field, no raw row returned; caller separately validates TLS/private DNS.
    Mirrors the approved PR #84 owner SID, SQL permissions, and catalog predicates.
    """
    try:
        lines=raw.decode('utf-8','strict').splitlines()
        rows=[x.strip().split('|') for x in lines if x.strip().startswith('FV_SQL|')]
        require(len(rows)==1 and len(rows[0])==22 and rows[0][0]=='FV_SQL', 'PREFLIGHT_SHAPE')
        v=[p.strip() for p in rows[0]]
        verify_sql_target(v[1],v[2]);require(v[3]=='5','ENGINE_EDITION')
        sid=bytes.fromhex(v[4]);require(len(sid)==16,'SID_SHAPE')
        ids=(str(uuid.UUID(bytes=sid)),str(uuid.UUID(bytes_le=sid)))
        require(any(digest(x.encode())==OWNER_HASH for x in ids),'OWNER_SID')
        require(v[5:8]==['1','1','0'],'VISIBILITY_EMPTY')
        require(v[8].upper()=='TRUE' and v[10].upper()=='TCP','TLS_TCP')
        require(len(v[11:])==11 and all(re.fullmatch(r'0|[1-9][0-9]*',x) for x in v[11:]), 'EMPTY_METADATA_UNKNOWN')
        require(all(x=='0' for x in v[11:]), 'PARTIAL_SCHEMA_OR_MARKER')
    except InstallGuardHold:
        raise
    except Exception as exc:
        raise InstallGuardHold('PREFLIGHT_INVALID') from exc
    return 'DATABASE_EMPTY_PASS'

def validate_original_package(package_bytes):
    """Pure exact package, all 14 production layers and manifest verification.

    Additional 34-member Seal and 33-member checksum cross-binding is required
    independently by the existing pinned P3 seal_verifier, before staging.
    """
    require(digest(package_bytes)==ORIGINAL_ZIP_SHA, 'ORIGINAL_ZIP_HASH')
    try:
        with zipfile.ZipFile(io.BytesIO(package_bytes)) as z:
            names=z.namelist()
            require(len(names)==len(set(names)) and all(not n.startswith('/') and '..' not in Path(n).parts for n in names),'ZIP_MEMBERS')
            manifest=z.read('standalone-install-manifest.json')
            require(digest(manifest)==MANIFEST_SHA,'ORIGINAL_MANIFEST_HASH')
            source=z.read('install.py')
            require(digest(source)==INSTALLER_SHA, 'ORIGINAL_INSTALLER_HASH')
            j=json.loads(manifest)
            require(j.get('candidateSourceSha')==RELEASE,'ORIGINAL_RELEASE')
            t=j['pathb-uat'];require((t['server'],t['database'],t['schemaProfile'])==(SERVER,DATABASE,'PathBNewEmpty'),'ORIGINAL_TARGET')
            seq=t['sequence'];require(len(seq)==14,'ORIGINAL_LAYER_COUNT')
            for i,x in enumerate(seq):
                require(x['file']=='pathb-uat-sql/'+LAYER_NAMES[i] and x['sha256']==LAYER_HASHES[i],'ORIGINAL_LAYER_BINDING')
                require(digest(z.read(x['file']))==LAYER_HASHES[i],'ORIGINAL_LAYER_BYTES')
    except InstallGuardHold:raise
    except Exception as exc:raise InstallGuardHold('ORIGINAL_PACKAGE_SHAPE') from exc
    return 'ORIGINAL_14_LAYERS_PASS'

if __name__=='__main__':
    raise SystemExit('NONDEPLOY_SOURCE_ONLY; no SQL or Azure entrypoint')
