#!/usr/bin/env python3
"""PR50 CatalogContract source-bound facet diagnostic; NONDEPLOY.

Does not connect to SQL/Azure. A scoped synthetic match never authorizes G1,
a migration, or whole 63-table / 26-history parity.
"""
import argparse,hashlib,json,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
CATALOG="docs/release/g1-execution/promotion/CatalogContract.sql"
MANIFEST="docs/release/g1-execution/promotion/manifest.json"
HISTORIC="database/migrations/1700_005_people_bulk_confirm_claim/Up.sql"
NAMES=("OPTIONC_COLUMNS","BATCH_AND_OPTIONC_CHECKS","OPTIONC_INDEXES",
       "OPTIONC_FOREIGN_KEYS","SOURCE_TRIGGERS","LEGACY_STATUS_COLUMNS")
CONTRACT="uat010-facet-source-nondeploy-v1"
class Hold(ValueError): pass
def digest(blob):return hashlib.sha256(blob).hexdigest()
def split(source):
    starts=[m.start() for m in re.finditer(r"\bCOALESCE\s*\(\s*\(\s*SELECT\b",source,re.I)]
    if len(starts)!=6:raise Hold("facet-count")
    parts=[];ends=[]
    for start in starts:
        i=start;depth=0;quote=False
        while i<len(source):
            c=source[i]
            if c=="'":
                if quote and i+1<len(source) and source[i+1]=="'":i+=2;continue
                quote=not quote
            elif not quote:
                if c=="(":depth+=1
                elif c==")":
                    depth-=1
                    if depth==0:i+=1;break
                    if depth<0:raise Hold("paren")
            i+=1
        if depth or quote:raise Hold("unterminated")
        parts.append(source[start:i]);ends.append(i)
    if any(end>start for end,start in zip(ends,starts[1:])):raise Hold("overlap")
    concat=source[starts[0]:ends[-1]]
    for st,en in reversed(list(zip(starts,ends))):
        concat=concat[:st-starts[0]]+"X"+concat[en-starts[0]:]
    if not re.fullmatch(r"X(?:\s*\+\s*X){5}",concat):raise Hold("noncanonical")
    for value,token in zip(parts,("sys.tables","sys.check_constraints",
                                   "sys.indexes","sys.foreign_keys","sys.triggers","sys.columns")):
        if token.lower() not in value.lower():raise Hold("facet-order")
    return parts
def inputs():
    manifest=json.loads((ROOT/MANIFEST).read_text())
    catalog=(ROOT/CATALOG).read_bytes()
    hist=(ROOT/HISTORIC).read_bytes()
    if manifest.get("approval")!="PROPOSED_NOT_AUTHORIZED" or manifest.get("schema010SourceBoundBaseline",{}).get("status")!="HOLD":
        raise Hold("release")
    if digest(catalog)!=manifest.get("sourceHashes",{}).get(CATALOG):raise Hold("source")
    if not all(v in hist.decode("utf-8-sig") for v in ("CK_ImportBatches_Status","N'Confirming'","N'Previewed'","N'Confirmed'","N'PartiallyFailed'")):
        raise Hold("historical1700")
    return split(catalog.decode("utf-8")),{"catalogSha256":digest(catalog),"history1700Sha256":digest(hist)}
def seal(rows,pins):
    values={}
    if not isinstance(rows,list):raise Hold("rows")
    for row in rows:
        name,value=row.get("Facet"),row.get("Digest")
        if name not in NAMES or name in values or not isinstance(value,str) or not re.fullmatch(r"[a-fA-F0-9]{64}",value):
            raise Hold("facet-entry")
        values[name]=value.upper()
    if set(values)!=set(NAMES):raise Hold("facets-incomplete")
    return {"contract":CONTRACT,**pins,"scope":"DISPOSABLE_SQL2022_AFTER_HISTORICAL1700",
            "facets":values,"g1":"HOLD","liveUatCompared":False,
            "full63TableVerified":False,"real26HistoryVerified":False,"triggersReferenceComplete":False}
def query(parts,pins,ref=None):
    offline=ref is None
    if not offline:
        if not isinstance(ref,dict) or ref.get("contract")!=CONTRACT or ref.get("scope")!="DISPOSABLE_SQL2022_AFTER_HISTORICAL1700" or ref.get("g1")!="HOLD" or any(ref.get(k)!=v for k,v in pins.items()):
            raise Hold("reference-pins")
        if any(ref.get(k) is not False for k in ("liveUatCompared","full63TableVerified","real26HistoryVerified","triggersReferenceComplete")):raise Hold("unsafe-reference")
        facts=ref.get("facets")
        if not isinstance(facts,dict) or set(facts)!=set(NAMES) or any(not isinstance(v,str) or not re.fullmatch(r"[A-F0-9]{64}",v) for v in facts.values()):raise Hold("reference-facets")
    pre=["SET NOCOUNT ON;"]
    if offline:
        pre+=["IF CONVERT(int,SERVERPROPERTY(N'EngineEdition'))<>3 OR CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))<>16",
              " OR DB_NAME() NOT LIKE N'OptionCIsolated[_]%'",
              " OR LEN(DB_NAME())<>LEN(N'OptionCIsolated_')+32",
              " OR TRY_CONVERT(int,SESSION_CONTEXT(N'FieldVisit.G1.NonDeploy'))<>1",
              " THROW 52200,N'G1_FACET_OFFLINE_ONLY',1;"]
    else:
        pre+=["IF COALESCE(CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5",
              " OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),N'') COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-uat'",
              " OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat'",
              " THROW 52200,N'G1_FACET_FIXED_TARGET',1;",
              "IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo'))<>63",
              " OR (SELECT COUNT(*) FROM dbo.SchemaVersions)<>26",
              " OR (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')<>1",
              " THROW 52201,N'G1_FACET_010_PREDECESSOR',1;"]
    pre+=["IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1",
          " THROW 52202,N'G1_FACET_VISIBILITY',1;"]
    chunks=[]
    for name,part in zip(NAMES,parts):
        h="CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),"+part+")),2)"
        if offline:
            chunks.append("SELECT N'"+name+"' AS Facet,"+h+" AS Digest")
        elif name=="SOURCE_TRIGGERS":
            # The PR50 bootstrap has no source-faithful 004/007 legacy triggers.
            chunks.append("SELECT N'"+name+"' AS Facet,N'REFERENCE_INCOMPLETE_HOLD' AS Status")
        else:
            chunks.append("SELECT N'"+name+"' AS Facet,CASE WHEN "+h+"=N'"+facts[name]+
                          "' THEN N'SYNTHETIC_MATCH_ONLY' ELSE N'MISMATCH_HOLD' END AS Status")
    pre.append("\nUNION ALL\n".join(chunks)+"\nORDER BY Facet;")
    if not offline:pre.append("-- Diagnostic ONLY: matches do not approve full schema, migration, G1 or Business UAT.")
    return "\n".join(pre)+"\n"
def main():
    p=argparse.ArgumentParser()
    p.add_argument("--mode",choices=("lint","offline-sql","seal","live-status-sql"),required=True)
    p.add_argument("--input",type=Path);p.add_argument("--output",type=Path)
    a=p.parse_args()
    try:
        parts,pins=inputs()
        if a.mode=="lint":print("G1FacetSource=PASS; Facets=6; Azure=NONE; G1=HOLD");return 0
        if a.output is None or a.output.exists():raise Hold("output")
        if a.mode=="offline-sql":txt=query(parts,pins)
        else:
            if a.input is None:raise Hold("input")
            data=json.loads(a.input.read_text())
            txt=json.dumps(seal(data,pins),sort_keys=True,indent=2)+"\n" if a.mode=="seal" else query(parts,pins,data)
        a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(txt)
        print("G1FacetDiagnostic=SOURCE_PINNED_NONDEPLOY; LiveUat=NONE; G1=HOLD");return 0
    except (ValueError,OSError,KeyError,TypeError,UnicodeError):
        print("G1FacetDiagnostic=HOLD; Details=SUPPRESSED; LiveUat=NONE");return 1
if __name__=="__main__":raise SystemExit(main())
