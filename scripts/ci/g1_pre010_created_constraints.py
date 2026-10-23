#!/usr/bin/env python3
"""Source-backed PRE-010 necessary constraints on four 004/007 new tables.

Offline only; does not reconstruct pre-existing v1.5 tables or claim check
definition semantic parity. Derivation fails closed on changed migration inputs.
"""
from __future__ import annotations
import importlib.util
import re
from pathlib import Path

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("g1cols",HERE/"g1_pre010_created_columns.py")
cols=importlib.util.module_from_spec(spec)
spec.loader.exec_module(cols)
FK=re.compile(
    r"^CONSTRAINT\s+(FK_\w+)\s+FOREIGN\s+KEY\s*\(\s*(\w+)\s*\)"
    r"\s+REFERENCES\s+dbo\.(\w+)\s*\(\s*(\w+)\s*\)",re.I)
CK=re.compile(r"^CONSTRAINT\s+(CK_\w+)\s+CHECK\s*\b",re.I)
EXPECT_FKS={"TeamLocationNotes":4,"TeamLocationNoteHistory":4,
            "GeocodingAttempts":2,"RouteCalculationAttempts":3}
EXPECT_CHECKS={"TeamLocationNotes":1,"TeamLocationNoteHistory":1,
               "GeocodingAttempts":2,"RouteCalculationAttempts":9}
REASON="CK_RouteCalculationAttempts_Reason"
class SourceStop(Exception):
    def __init__(self,code):self.code=code

def derive(root:Path)->dict:
    manifest=cols.SRC.derive(root)
    fk,ck=[],[]
    for table,(mid,_count) in cols.SCOPE.items():
        raw=(root / manifest["Migrations"][mid]["Path"]).read_text(encoding="utf-8-sig")
        candidates=[m for m in cols.TABLE.finditer(raw) if m.group(1)==table]
        if len(candidates)!=1:raise SourceStop("SOURCE_TABLE")
        match=candidates[0]
        end=cols.body_end(raw,match.end()-1)
        body=raw[match.end():end]
        for line in cols.split_columns(body):
            f=FK.match(line)
            if f:
                name,parent,ref_table,ref_col=f.groups()
                fk.append({"SchemaName":"dbo","TableName":table,"ForeignKeyName":name,
                    "ParentColumn":parent,"ReferencedTable":ref_table,"ReferencedColumn":ref_col})
                continue
            c=CK.match(line)
            if c:ck.append({"SchemaName":"dbo","TableName":table,"CheckConstraintName":c.group(1)})
        if sum(x["TableName"]==table for x in fk)!=EXPECT_FKS[table]:
            raise SourceStop("FK_SOURCE_COUNT")
        if sum(x["TableName"]==table for x in ck)!=EXPECT_CHECKS[table]:
            raise SourceStop("CHECK_SOURCE_COUNT")
    # 010 replaces rather than adds the RouteCalculationAttempts reason CHECK.
    src010=(root/manifest["Migrations"]["010"]["Path"]).read_text(encoding="utf-8-sig")
    if (src010.count("CONSTRAINT "+REASON)!=3 or "DROP CONSTRAINT "+REASON not in src010
        or "N'BackgroundMileageJob'" not in src010):
        raise SourceStop("REASON_SOURCE")
    if len(fk)!=13 or len(ck)!=13 or len({(x["TableName"],x["ForeignKeyName"]) for x in fk})!=13:
        raise SourceStop("SOURCE_COUNT")
    if len({(x["TableName"],x["CheckConstraintName"]) for x in ck})!=13:
        raise SourceStop("SOURCE_COUNT")
    return {"ForeignKeys":sorted(fk,key=lambda x:(x["TableName"],x["ForeignKeyName"])),
            "Checks":sorted(ck,key=lambda x:(x["TableName"],x["CheckConstraintName"])),
            "CheckDefinitionParity":"UNKNOWN_NOT_CANONICAL_SQLSERVER_DDL",
            "LegacyBase":"UNKNOWN","ReleaseGate":"HOLD"}
