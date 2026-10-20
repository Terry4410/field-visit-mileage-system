"""Promotion candidate builder and private disposable evidence boundary."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
ROOT=Path('docs/release/g1-execution/promotion')
def digest(b):return hashlib.sha256(b).hexdigest()
def run(mode):
 r=subprocess.run(['pwsh','-NoProfile','-File','scripts/ci/test_g1_promotion.ps1','-Mode',mode],capture_output=True,text=True,timeout=900)
 for line in r.stdout.splitlines():
  if re.fullmatch(r'PromotionCase=[A-Za-z0-9_]+; Status=(PASS|STOP);(?: Details=SUPPRESSED;)?',line) or re.fullmatch(r'HarnessDiagnostic=STOP; Line=[0-9]+; ProviderCode=-?[0-9]+;',line):print(line)
 assert r.returncode==0
if __name__=='__main__':
 try:
  if sys.argv[1]=='reference':run('Reference')
  elif sys.argv[1]=='test':run('Test')
  else:raise ValueError('mode')
 except Exception:raise SystemExit('G1_PROMOTION_STOP phase='+sys.argv[1]+' details=SUPPRESSED') from None
