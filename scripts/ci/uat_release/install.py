"""Standalone create-only installer. Dry-run is the default. No reset/deletion."""
import argparse,hashlib,json,os,subprocess,sys
from pathlib import Path
SERVER='sql-fieldvisit-jpe-uat.database.windows.net';DATABASE='db-fieldvisit-uat'
def sha(b): return hashlib.sha256(b).hexdigest()
def promoted(raw,name):
 text=raw.decode('utf-8-sig')
 target="IF DB_NAME()<>N'db-fieldvisit-uat' OR CONVERT(int,SERVERPROPERTY('EngineEdition'))<>5 THROW 56610,N'EXACT_UAT_TARGET_REQUIRED',1;\n"
 if name=='20_route_reason_check.sql':
  old="IF DB_NAME() NOT IN\n   (N'OptionCIsolated_FieldVisitV180CleanInstall',\n    N'OptionCIsolated_FieldVisitV180Replay')\n    THROW 56040, N'CLEAN_ROUTE_DISPOSABLE_TARGET_REQUIRED', 1;"
  if text.count(old)!=1: raise ValueError('ROUTE_TARGET_GUARD_DRIFT')
  text=text.replace(old,target)
 if name=='60_final_functional_capabilities.sql':
  old="IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%' THROW 56600,N'DISPOSABLE_ONLY',1;"
  if text.count(old)!=1: raise ValueError('CAPABILITY_TARGET_GUARD_DRIFT')
  text=text.replace(old,target)
 return (target+'GO\n'+text).encode()
def main():
 p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--target',choices=['fixture','uat'],required=True);p.add_argument('--execute',action='store_true');a=p.parse_args()
 root=a.root;planfile=root/'standalone-install-manifest.json';plan=json.loads(planfile.read_text());mode=plan[a.target]
 for x in mode['sequence']:
  assert sha((root/x['file']).read_bytes())==x['sha256'],'INSTALLER_INPUT_HASH_MISMATCH'
 if not a.execute: print('INSTALLER=HASH_VERIFIED_DRY_RUN_NO_CONNECTION');return 0
 if os.environ.get('FIELDVISIT_APPROVED_INSTALL_MANIFEST')!=sha(planfile.read_bytes()): raise ValueError('EXACT_MANIFEST_EXECUTION_APPROVAL_REQUIRED')
 server,database=(SERVER,DATABASE) if a.target=='uat' else ('localhost,1433','OptionCIsolated_FieldVisitV180CleanInstall')
 args=['sqlcmd','-S',server,'-d',database,'-I','-b','-l','10']
 if a.target=='fixture':args+=['-C']
 else:args+=['-N']
 if os.environ.get('SQLCMDUSER'):args+=['-U',os.environ['SQLCMDUSER']]
 elif a.target=='uat':args+=['-G']
 else:raise ValueError('INSTALLER_IDENTITY_REQUIRED')
 def run(raw):
  r=subprocess.run(args,input=raw,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=120)
  if r.returncode:raise ValueError('INSTALLER_SQL_FAILED_DETAILS_SUPPRESSED')
 # Inspection precedes any schema mutation; an existing nonempty target fails.
 run(b"IF EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0) THROW 56611,N'EMPTY_DATABASE_REQUIRED',1;\n")
 prologue=b'SET ANSI_NULLS ON;SET ANSI_PADDING ON;SET ANSI_WARNINGS ON;SET ARITHABORT ON;SET CONCAT_NULL_YIELDS_NULL ON;SET QUOTED_IDENTIFIER ON;SET NUMERIC_ROUNDABORT OFF;\nGO\n'
 for x in mode['sequence']:run(prologue+(root/x['file']).read_bytes())
 run(b"IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo'))<>69 OR (SELECT COUNT(*) FROM dbo.SchemaVersions)<>3 THROW 56612,N'FINAL_INSTALL_SCOPE_FAILED',1;\nIF EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1) OR EXISTS(SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1) THROW 56613,N'CONSTRAINT_TRUST_FAILED',1;\n")
 print('INSTALLER=FINAL_FOURTEEN_LAYER_SCOPE_PASS')
 return 0
if __name__=='__main__':
 try:sys.exit(main())
 except Exception:print('INSTALLER=FAIL_CLOSED_DETAILS_SUPPRESSED');sys.exit(1)
