"""Offline source extraction, NOT SQL execution or approval of candidate migrations."""
import hashlib,json,pathlib,re,sys
def endparen(s,start):
 depth=0;quoted=False;i=start
 while i<len(s):
  c=s[i]
  if c=="'":
   if quoted and i+1<len(s) and s[i+1]=="'":i+=2;continue
   quoted=not quoted
  elif not quoted:
   if c=='(':depth+=1
   elif c==')':
    depth-=1
    if depth==0:return i
  i+=1
 raise ValueError('unbalanced')
def parts(s):
 result=[];start=0;i=0;quoted=False
 while i<len(s):
  if s[i]=="'":
   if quoted and i+1<len(s) and s[i+1]=="'":i+=2;continue
   quoted=not quoted
  elif not quoted and s[i]=='(':i=endparen(s,i)
  elif not quoted and s[i]==',':result.append(s[start:i].strip());start=i+1
  i+=1
 return result+[s[start:].strip()]
def derive(root):
 out={'rc':'f29affb3299828f45b1f131321779e8bea2f86a8','scope':'CANDIDATE_NECESSARY_CONTRACT_NOT_OBSERVED_UAT','inputs':{},'columns':[],'checks':{},'indexes':[],'foreignKeys':[],'sourceOnly':[]}
 ids=('011','012','014','015','016','017','018','019','020')
 for mid in ids:
  path=pathlib.Path('docs/release/optionc-canonical-sql-draft/candidate')/('1800_'+mid)/'Up.sql'
  data=(root/path).read_bytes();out['inputs'][str(path)]=hashlib.sha256(data).hexdigest()
  text=re.sub(r'/\*.*?\*/|--[^\n]*','',data.decode(),flags=re.S)
  blocks=[]
  for m in re.finditer(r'CREATE TABLE dbo\.(\w+)\s*\(',text):
   start=m.end()-1;end=endparen(text,start);table=m[1];blocks.append((m.start(),end,table))
   for clause in parts(text[start+1:end]):
    c=re.match(r'(\w+)\s+(bigint|int|bit|nvarchar|binary|varbinary|decimal|datetime2|uniqueidentifier|rowversion)\b(?:\s*\((max|\d+)(?:\s*,\s*(\d+))?\))?',clause,re.I)
    if not c:continue
    name,typ,a,b=c.groups();typ=typ.lower();length,precision,scale={'bigint':(8,19,0),'int':(4,10,0),'bit':(1,1,0),'uniqueidentifier':(16,0,0),'rowversion':(8,0,0)}.get(typ,(None,0,0))
    if typ in ('nvarchar','binary','varbinary'):length=-1 if a=='max' else int(a)*(2 if typ=='nvarchar' else 1)
    if typ=='decimal':precision=int(a);scale=int(b);length=5 if precision<=9 else 9 if precision<=19 else 13 if precision<=28 else 17
    if typ=='datetime2':scale=int(a or 7);precision=20+scale if scale else 19;length=6 if scale<=2 else 7 if scale<=4 else 8
    out['columns'].append(dict(table=table,column=name,type='timestamp' if typ=='rowversion' else typ,max_length=length,precision=precision,scale=scale,is_nullable=0 if re.search(r'NOT NULL|PRIMARY KEY',clause,re.I) else 1,is_identity=1 if re.search(r'IDENTITY\s*\(',clause,re.I) else 0,migration=mid))
  def table_at(pos):
   for start,end,table in blocks:
    if start<=pos<=end:return table
   alters=list(re.finditer(r'ALTER TABLE dbo\.(\w+)',text[:pos]))
   if not alters:raise ValueError('unbound')
   return alters[-1][1]
  # A final-state reference must retire constraints explicitly removed by the
  # next migration, including renamed/replaced constraints with different names.
  for m in re.finditer(r'ALTER TABLE dbo\.(\w+)\s+DROP CONSTRAINT\s+(\w+)',text,re.I):
   out['checks'].pop(m[1]+'.'+m[2],None)
  for m in re.finditer(r'CONSTRAINT\s+(\w+)\s+CHECK\s*\(',text,re.I):
   start=m.end()-1;end=endparen(text,start);table=table_at(m.start())
   out['checks'][table+'.'+m[1]]=dict(table=table,name=m[1],definition=text[start:end+1],migration=mid)
  for m in re.finditer(r'CREATE\s+(UNIQUE\s+)?INDEX\s+(\w+)\s+ON\s+dbo\.(\w+)\s*\(',text,re.I):
   start=m.end()-1;end=endparen(text,start);tail=text[end+1:text.index(';',end)]
   out['indexes'].append(dict(table=m[3],name=m[2],unique=bool(m[1]),keys=[x.strip().split()[0] for x in parts(text[start+1:end])],filter=tail.strip()[5:].strip() if tail.strip().upper().startswith('WHERE') else None,migration=mid))
  for m in re.finditer(r'CONSTRAINT\s+(\w+)\s+FOREIGN KEY\s*\(([^)]+)\)\s*REFERENCES\s+dbo\.(\w+)\s*\(([^)]+)\)(?:\s+ON DELETE\s+(NO ACTION|CASCADE))?',text,re.I):
   out['foreignKeys'].append(dict(table=table_at(m.start()),name=m[1],columns=parts(m[2]),referencedTable=m[3],referencedColumns=parts(m[4]),deleteAction=(m[5] or 'NO ACTION').replace(' ','_'),updateAction='NO_ACTION',migration=mid))
 # Model/history/source010 are source evidence only; no invented baseline/SQL rendering.
 more=[pathlib.Path('backend/src/FieldVisit.Infrastructure/AppDbContext.cs'),pathlib.Path('database/015_schema_v1_5_verify.sql'),pathlib.Path('docs/release/SCHEME_A_BUSINESS_UAT_ROLE_CASES_NONDEPLOY.md')]
 more+=sorted(p.relative_to(root) for p in (root/'database/migrations').glob('1800_0*/Up.sql'))
 for path in more:out['inputs'][str(path)]=hashlib.sha256((root/path).read_bytes()).hexdigest()
 out['checks']=list(out['checks'].values())
 out['sourceOnly']=['010 deployed metadata NOT OBSERVED','base010 exact facets/FK/index/trigger reconciliation UNKNOWN','candidate defaults/PK/triggers and full EF property comparison require review','history row consistency NOT OBSERVED; accepted regression is synthetic only','019 not mandatory for frozen25;013 reserved']
 return out
if __name__=='__main__':print(json.dumps(derive(pathlib.Path(sys.argv[1])),indent=2))
