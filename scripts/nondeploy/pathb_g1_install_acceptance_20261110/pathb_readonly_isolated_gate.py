#!/usr/bin/env python3
"""NONDEPLOY-only isolated Path B preflight gate.

This deliberately has NO live mode, network/SQL client, token acquisition, shell,
installation, Blob, Kudu, or Azure calls. It verifies pinned inputs before a
separately reviewed read-only transport is ever considered.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPECTED_SQL = 'e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add'
EXPECTED_SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
EXPECTED_DB = 'db-fieldvisit-pathb-uat'
EXPECTED_APP = 'api-fieldvisit-pathb-uat'
EXPECTED_RG = 'rg-fieldvisit-uat'
EXPECTED_RELEASE = 'd6c220877b9976b6478d85b5171d4e06293595ae'

class GateHold(Exception):
    pass

def require(ok):
    if not ok:
        raise GateHold('READONLY_INPUT_HOLD')

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def verify(root=HERE):
    root = Path(root)
    require(root.is_dir() and not root.is_symlink())
    binding_path = root / 'binding-index.json'
    config_path = root / 'executor_config.json'
    sql_path = root / 'preflight.sql'
    for path in (binding_path, config_path, sql_path):
        require(path.is_file() and not path.is_symlink())
    binding = json.loads(binding_path.read_bytes())
    cfg = json.loads(config_path.read_bytes())
    require(binding.get('contract') == 'FIELDVISIT_PATHB_G1_ORIGINAL_BINDING_V1')
    require(binding.get('approved_release') == EXPECTED_RELEASE)
    require(binding.get('files', {}).get('preflight.sql') == EXPECTED_SQL)
    require(binding.get('files', {}).get('executor_config.json') == sha(config_path.read_bytes()))
    require(sha(sql_path.read_bytes()) == EXPECTED_SQL)
    require((cfg.get('server'), cfg.get('database'), cfg.get('app'), cfg.get('resource_group'),
             cfg.get('approved_release'), cfg.get('preflight_sha256')) ==
            (EXPECTED_SERVER, EXPECTED_DB, EXPECTED_APP, EXPECTED_RG,
             EXPECTED_RELEASE, EXPECTED_SQL))
    sql = sql_path.read_text(encoding='utf-8')
    # The SQL is immutable by SHA. This check is only a second, defense-in-depth guard.
    require(re.match(r'^SET NOCOUNT ON;\\s*SELECT\\s', sql, re.I) is not None)
    require(sql.rstrip().endswith('@@SPID;'))
    return {'SOURCE_BINDING': 'PASS', 'PINNED_SQL': 'PASS',
            'EXACT_TARGET': 'PASS', 'LIVE_TRANSPORT': 'NOT_IMPLEMENTED',
            'AZURE_CALLS': '0', 'SQL_CALLS': '0', 'BLOB_CALLS': '0'}

def main(argv=None):
    parser = argparse.ArgumentParser(description='Offline-only fixed SQL integrity gate')
    parser.add_argument('--offline', action='store_true', required=True)
    args = parser.parse_args(argv)
    try:
        results = verify()
    except Exception:
        print('READONLY_OFFLINE=HOLD')
        print('LIVE_TRANSPORT=NOT_IMPLEMENTED')
        return 1
    for k, v in results.items():
        print(f'{k}={v}')
    print('READONLY_OFFLINE=PASS')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
