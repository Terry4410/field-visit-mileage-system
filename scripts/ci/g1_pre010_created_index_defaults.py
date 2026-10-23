#!/usr/bin/env python3
"""Pinned PRE010 index/default reference for five tables created by 004/007.

Source-only; v1.5 legacy schema and live UAT remain UNKNOWN.
"""
from __future__ import annotations
import importlib.util
import re
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("g1cols",HERE/"g1_pre010_created_columns.py")
cols=importlib.util.module_from_spec(spec);spec.loader.exec_module(cols)
COUNTS={"TeamLocationNotes":(3,1),"TeamLocationNoteHistory":(2,1),
        "GeocodingAttempts":(3,0),"RouteCalculationAttempts":(4,0),
        "MileageGovernanceEvents":(3,1)}
INDEX=re.compile(r"\bCREATE\s+INDEX\s+(IX_\w+)\s+ON\s+dbo\.(\w+)\s*\(",re.I)
PRIMARY=re.compile(r"\bCONSTRAINT\s+(PK_\w+)\s+PRIMARY\s+KEY\b",re.I)
UNIQUE=re.compile(r"^\s*CONSTRAINT\s+(UQ_\w+)\s+UNIQUE\s*\(",re.I)
DEFAULT=re.compile(r"\bCONSTRAINT\s+(DF_\w+)\s+DEFAULT\s*\(",re.I)
KEY=re.compile(r"^\s*([A-Za-z_]\w*)(?:\s+(ASC|DESC))?\s*$",re.I)
class SourceStop(Exception):
    def __init__(self,code):self.code=code
def keys(value):
    out=[]
    for part in cols.split_columns(value):
        m=KEY.fullmatch(part)
        if not m:raise SourceStop("INDEX_KEY")
        out.append(m.group(1)+":"+(m.group(2) or "ASC").upper())
    if not out or len(set(out))!=len(out):raise SourceStop("INDEX_KEY")
    return ",".join(out)
def normalized(expr):
    if expr is None:return ""
    if not isinstance(expr,str):raise SourceStop("SQL_DEFINITION")
    return re.sub(r"[\s\[\]\(\)]","",expr).upper()
def derive(root:Path):
    verified=cols.SRC.derive(root)
    indexes,defaults=[],[]
    for table,(mid,_count) in cols.SCOPE.items():
        raw=(root / verified["Migrations"][mid]["Path"]).read_text(encoding="utf-8-sig")
        candidates=[m for m in cols.TABLE.finditer(raw) if m.group(1)==table]
        if len(candidates)!=1:raise SourceStop("SOURCE_TABLE")
        m=candidates[0];end=cols.body_end(raw,m.end()-1)
        parts=cols.split_columns(raw[m.end():end])
        ind,defs=[],[]
        def add(name,key,unique=False,pk=False,uq=False,includes="",where=""):
            ind.append({"SchemaName":"dbo","TableName":table,"IndexName":name,
              "KeyColumns":key,"IncludeColumns":includes,"IsUnique":unique,
              "IsPrimaryKey":pk,"IsUniqueConstraint":uq,"IsDisabled":False,
              "HasFilter":bool(where),"FilterDefinition":normalized(where)})
        for part in parts:
            pk=PRIMARY.search(part)
            if pk:
                first=part.split(maxsplit=1)[0]
                if not re.fullmatch(r"[A-Za-z_]\w*",first):raise SourceStop("PRIMARY_COLUMN")
                add(pk.group(1),keys(first),unique=True,pk=True)
            uq=UNIQUE.search(part)
            if uq:
                close=cols.body_end(part,uq.end()-1)
                add(uq.group(1),keys(part[uq.end():close]),unique=True,uq=True)
            default=DEFAULT.search(part)
            if default:
                close=cols.body_end(part,default.end()-1)
                col=part.split(maxsplit=1)[0]
                norm=normalized(part[default.end():close])
                if norm!="SYSUTCDATETIME":raise SourceStop("DEFAULT_EXPRESSION")
                defs.append({"SchemaName":"dbo","TableName":table,"ColumnName":col,
                             "DefaultName":default.group(1),"DefinitionNorm":norm})
        for match in INDEX.finditer(raw):
            if match.group(2)!=table:continue
            close=cols.body_end(raw,match.end()-1)
            tail=raw[close+1:].split(";",1)[0]
            inc=re.search(r"\bINCLUDE\s*\(",tail,re.I)
            inc_keys=""
            if inc:
                incend=cols.body_end(tail,inc.end()-1)
                arr=[x.strip() for x in cols.split_columns(tail[inc.end():incend])]
                if any(not re.fullmatch(r"[A-Za-z_]\w*",x) for x in arr):raise SourceStop("INDEX_INCLUDE")
                inc_keys=",".join(sorted(arr))
            filt=re.search(r"\bWHERE\s+([^;]+)",tail,re.I)
            where=filt.group(1).strip() if filt else ""
            add(match.group(1),keys(raw[match.end():close]),includes=inc_keys,where=where)
        if (len(ind),len(defs))!=COUNTS[table]:raise SourceStop("INDEX_DEFAULT_COUNT")
        indexes.extend(ind);defaults.extend(defs)
    if len(indexes)!=15 or len(defaults)!=3 or len({(x["TableName"],x["IndexName"]) for x in indexes})!=15:
        raise SourceStop("INDEX_DEFAULT_TOTAL")
    if len({(x["TableName"],x["ColumnName"]) for x in defaults})!=3:
        raise SourceStop("DEFAULT_DUPLICATE")
    return {"Indexes":sorted(indexes,key=lambda x:(x["TableName"],x["IndexName"])),
            "Defaults":sorted(defaults,key=lambda x:(x["TableName"],x["DefaultName"])),
            "LegacyIndexes":"UNKNOWN","LegacyDefaults":"UNKNOWN","ReleaseGate":"HOLD"}
