#!/usr/bin/env python3
"""Pin and derive 47 exact created-table column facets from 004/007.

Covers only columns in four brand-new tables created by reviewed PRE-010
migrations. Never invents the preexisting 1.5 base schema and never reads SQL.
"""
from __future__ import annotations
import importlib.util
import re
from pathlib import Path

HERE=Path(__file__).resolve().parent
SPEC=importlib.util.spec_from_file_location("pre010_src",HERE/"g1_pre010_source_contract.py")
SRC=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SRC)

SCOPE={
 "TeamLocationNotes":("004",9),
 "TeamLocationNoteHistory":("004",10),
 "GeocodingAttempts":("007",11),
 "RouteCalculationAttempts":("007",17),
}
TABLE=re.compile(r"\bCREATE\s+TABLE\s+dbo\.([A-Za-z_]\w*)\s*\(",re.I)
COLUMN=re.compile(r"^\s*([A-Za-z_]\w*)\s+(BIGINT|INT|NVARCHAR|DATETIME2|VARBINARY|UNIQUEIDENTIFIER|ROWVERSION|BIT)\b(?:\s*\(\s*(\d+)\s*(?:,\s*\d+\s*)?\))?",re.I)
TYPES={"bigint":(8,0),"int":(4,0),"uniqueidentifier":(16,0),"rowversion":(8,0),"bit":(1,0)}

class SourceStop(Exception):
    def __init__(self,code):self.code=code

def body_end(text, start):
    depth=0
    quote=False
    pos=start
    while pos<len(text):
        ch=text[pos]
        if ch=="'":
            if quote and pos+1<len(text) and text[pos+1]=="'":
                pos+=2
                continue
            quote=not quote
        elif not quote:
            if ch=="(":depth+=1
            elif ch==")":
                depth-=1
                if depth==0:return pos
        pos+=1
    raise SourceStop("DDL_PARENS")

def split_columns(body):
    out=[]
    start=0
    depth=0
    quote=False
    pos=0
    while pos<len(body):
        ch=body[pos]
        if ch=="'":
            if quote and pos+1<len(body) and body[pos+1]=="'":
                pos+=2
                continue
            quote=not quote
        elif not quote:
            if ch=="(":depth+=1
            elif ch==")":depth-=1
            elif ch=="," and depth==0:
                out.append(body[start:pos].strip())
                start=pos+1
        pos+=1
    if quote or depth!=0:raise SourceStop("DDL_PARENS")
    out.append(body[start:].strip())
    return out

def derive(root:Path):
    src=SRC.derive(root)
    output={}
    for mid in ("004","007"):
        info=src["Migrations"][mid]
        text=(root/info["Path"]).read_text(encoding="utf-8-sig")
        for m in TABLE.finditer(text):
            table=m.group(1)
            if table not in SCOPE:
                continue
            if SCOPE[table][0]!=mid or table in output:
                raise SourceStop("TABLE_PROVENANCE")
            closing=body_end(text,m.end()-1)
            rows={}
            for clause in split_columns(text[m.end():closing]):
                c=COLUMN.match(clause)
                if not c:continue
                name,typ,param=c.groups()
                typ=typ.lower()
                if name in rows:raise SourceStop("DUPLICATE_COLUMN")
                if typ=="nvarchar":
                    if param is None:raise SourceStop("DDL_TYPE")
                    length,scale=int(param)*2,0
                elif typ=="varbinary":
                    if param is None:raise SourceStop("DDL_TYPE")
                    length,scale=int(param),0
                elif typ=="datetime2":
                    scale=int(param or 7)
                    length=6 if scale<=2 else 7 if scale<=4 else 8
                else:length,scale=TYPES[typ]
                row={
                    "SchemaName":"dbo","TableName":table,"ColumnName":name,
                    "SqlType":"timestamp" if typ=="rowversion" else typ,
                    "MaxLengthBytes":length,"Scale":scale,
                    "IsNullable":not bool(re.search(r"\bNOT\s+NULL\b|\bPRIMARY\s+KEY\b",clause,re.I)),
                    "IsIdentity":bool(re.search(r"\bIDENTITY\s*\(",clause,re.I))
                }
                rows[name]=row
            if len(rows)!=SCOPE[table][1]:raise SourceStop("COLUMN_SOURCE_COUNT")
            output[table]=rows
    if set(output)!=set(SCOPE):raise SourceStop("TABLE_SOURCE_SCOPE")
    result=[output[t][n] for t in sorted(output) for n in sorted(output[t])]
    if len(result)!=47:raise SourceStop("SOURCE_COUNT")
    return result
