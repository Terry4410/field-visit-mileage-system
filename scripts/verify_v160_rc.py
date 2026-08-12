#!/usr/bin/env python3
from pathlib import Path
import sys, subprocess, xml.etree.ElementTree as ET, re

ROOT=Path.cwd()
errors=[]; warnings=[]

def req(path):
    p=ROOT/path
    if not p.exists(): errors.append(f'MISSING {path}')
    return p

def contains(path, text):
    p=req(path)
    if p.exists() and text not in p.read_text(encoding='utf-8',errors='ignore'):
        errors.append(f'{path}: missing marker: {text}')

required=[
 'frontend/src/App.tsx','frontend/src/pages/UnifiedQueryPage.tsx','frontend/src/v160-final.css',
 'backend/src/FieldVisit.Api/V160FinalController.cs','backend/src/FieldVisit.Api/BackgroundJobHostedService.cs',
 'backend/src/FieldVisit.Application/V160FinalService.cs','backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs',
 'backend/src/FieldVisit.Infrastructure/ReportDocumentService.cs','backend/src/FieldVisit.Infrastructure/WorkbookImportService.cs',
 'database/migrations/1600_002_final/Up.sql','database/migrations/1600_002_final/Verify.sql','database/migrations/1600_002_final/Rollback.sql',
 'docs/release/FINAL-REVIEW-v1.6.0.md','docs/uat/UAT-SMOKE-TEST-v1.6.0.md'
]
for x in required:req(x)

contains('frontend/package.json','"version": "1.6.0"')
contains('frontend/src/App.tsx','UAT v1.6.0 RC')
contains('backend/src/FieldVisit.Api/V160FinalController.cs','query/trips/export.pdf')
contains('backend/src/FieldVisit.Api/V160FinalController.cs','imports/{importBatchId:guid}/errors.xlsx')
contains('backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs','VisitTripSnapshots')
contains('backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs','PendingAdminClose')
contains('backend/src/FieldVisit.Infrastructure/BackgroundJobService.cs','AllPending')
contains('backend/src/FieldVisit.Infrastructure/BackgroundJobService.cs','DateRange')
contains('backend/src/FieldVisit.Infrastructure/WorkbookImportService.cs','CreateErrorReportAsync')
contains('database/migrations/1600_002_final/Up.sql',"SnapshotType=N'BackfillApproved'")
contains('database/migrations/1600_002_final/Verify.sql','Approved trips missing Snapshot')

# Old browser-side report generator must be gone.
if (ROOT/'frontend/src/utils.ts').exists(): errors.append('frontend/src/utils.ts still exists; FINAL uses server-side Excel/PDF')
# Old controllers/endpoints removed by apply script.
if (ROOT/'backend/src/FieldVisit.Api/Controllers/ReportsController.cs').exists(): errors.append('legacy ReportsController.cs still exists')
for path, banned in [
 ('backend/src/FieldVisit.Api/Controllers/TripsController.cs','mileage-jobs'),
 ('backend/src/FieldVisit.Api/Controllers/TripsController.cs','trips/history'),
 ('backend/src/FieldVisit.Api/Controllers/MasterController.cs','locations/batch-publish')]:
    p=ROOT/path
    if p.exists() and banned in p.read_text(encoding='utf-8'): errors.append(f'{path} still contains legacy endpoint {banned}')

# Common accidental leftovers in final application code (historical migrations/docs excluded).
scan_dirs=[ROOT/'frontend/src',ROOT/'backend/src']
patterns=[('Prototype v2',re.compile(r'Prototype\s*v2',re.I)),('hard-coded 40 km',re.compile(r'40\.0')),('NotImplemented',re.compile(r'NotImplementedException|TODO\b|FIXME\b'))]
for d in scan_dirs:
    if not d.exists():continue
    for f in d.rglob('*'):
        if not f.is_file() or f.suffix.lower() not in {'.ts','.tsx','.cs','.json','.css'}:continue
        text=f.read_text(encoding='utf-8',errors='ignore')
        for label,pat in patterns:
            if pat.search(text): errors.append(f'{f.relative_to(ROOT)} contains {label}')

# XML project files parse.
for f in ROOT.rglob('*.csproj'):
    try: ET.parse(f)
    except Exception as ex: errors.append(f'{f.relative_to(ROOT)} invalid XML: {ex}')

# TypeScript syntax parser if global TypeScript is available.
node=shutil_node=None
try:
    node=subprocess.check_output(['bash','-lc','command -v node'],text=True).strip()
except Exception: node=''
if node:
    candidates=['/opt/nvm/versions/node/v22.16.0/lib/node_modules/typescript','/usr/local/lib/node_modules/typescript']
    tsmod=next((x for x in candidates if Path(x).exists()),None)
    if tsmod:
        js='''const ts=require(process.argv[2]);const fs=require('fs');let bad=0;for(const f of process.argv.slice(3)){const r=ts.transpileModule(fs.readFileSync(f,'utf8'),{compilerOptions:{jsx:ts.JsxEmit.ReactJSX,target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext},fileName:f,reportDiagnostics:true});for(const d of (r.diagnostics||[]).filter(x=>x.category===ts.DiagnosticCategory.Error)){bad++;console.error(f+': '+ts.flattenDiagnosticMessageText(d.messageText,' '));}}process.exit(bad?1:0);'''
        check=Path('/tmp/v160-ts-syntax.js');check.write_text(js)
        files=[str(x) for x in (ROOT/'frontend/src').rglob('*.ts')]+[str(x) for x in (ROOT/'frontend/src').rglob('*.tsx')]
        r=subprocess.run([node,str(check),tsmod,*files])
        if r.returncode: errors.append('TypeScript syntax parser reported errors')
    else:warnings.append('Global TypeScript module not found; npm build will be authoritative.')
else:warnings.append('node not found; npm build will be authoritative.')

print('=== v1.6.0 RC Static Verification ===')
for w in warnings:print('WARN:',w)
if errors:
    for e in errors:print('FAIL:',e)
    print(f'RESULT: FAIL ({len(errors)} issues)')
    sys.exit(1)
print('RESULT: PASS')
print('NOTE: This is static verification only. npm/dotnet build, Azure SQL Verify and runtime UAT remain required.')
