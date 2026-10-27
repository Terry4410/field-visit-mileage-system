#!/usr/bin/env python3
"""G1 014 source-pinned SELECT-only predecessor data preflight; NO live execution."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BACKFILL = "docs/release/optionc-canonical-sql-draft/candidate/1800_014/Backfill.sql"
MANIFEST = "docs/release/g1-execution/promotion/manifest.json"
CODES = (51433, 51434)


class SourceHold(ValueError):
    pass


def extract_predicate(source: str, code: int) -> str:
    # Do not restate business predicates by hand: use the exact frozen 014 source.
    pattern = (
        r"^IF EXISTS\s*\(\s*"
        r"(SELECT 1\s+FROM dbo\.Locations l(?:(?!^IF EXISTS)[\s\S])*?)"
        r"^\)\s*THROW\s+" + str(code) + r"\b"
    )
    matches = list(re.finditer(pattern, source, flags=re.MULTILINE))
    if len(matches) != 1:
        raise SourceHold("source-block-boundary")
    predicate = matches[0].group(1).strip()
    if predicate.count("WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1") != 1:
        raise SourceHold("approved-only-scope")
    if code == 51433 and not all(
        token in predicate for token in
        ("NULLIF(LTRIM(RTRIM(l.LocationName))", "NULLIF(LTRIM(RTRIM(l.Address))",
         "l.CreatedByUserId<=0", "l.RowVersion IS NULL")
    ):
        raise SourceHold("projection-precondition")
    if code == 51434 and not all(
        token in predicate for token in
        ("LEFT JOIN dbo.Organizations", "LEFT JOIN dbo.Teams",
         "LEFT JOIN dbo.Users", "u.OrganizationId<>l.OrganizationId",
         "l.TeamId IS NOT NULL", "t.OrganizationId<>l.OrganizationId")
    ):
        raise SourceHold("ownership-precondition")
    return predicate


def build_sql(source: bytes, manifest: dict) -> str:
    pins = manifest.get("sourceHashes", {})
    expected = pins.get(BACKFILL)
    if not isinstance(expected, str) or len(expected) != 64:
        raise SourceHold("missing-source-pin")
    if hashlib.sha256(source).hexdigest() != expected:
        raise SourceHold("source-sha256-drift")
    if manifest.get("approval") != "PROPOSED_NOT_AUTHORIZED":
        raise SourceHold("release-boundary")
    text = source.decode("utf-8")
    if text.count("THROW 51433,") != 1 or text.count("THROW 51434,") != 1:
        raise SourceHold("source-guard-cardinality")
    predicates = [extract_predicate(text, code) for code in CODES]
    # This script may be executed only with a reviewed, separate target/session:
    # SQL Azure fixed UAT, or disposable Developer SQL2022 with explicit session opt-in.
    sql = """-- SOURCE-DERIVED 014 PRECONDITION CHECK ONLY. No mutation, no row data output.
SET NOCOUNT ON;
DECLARE @G1Live bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N'EngineEdition'))=5
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2=N'sql-fieldvisit-jpe-uat'
 AND DB_NAME() COLLATE Latin1_General_100_BIN2=N'db-fieldvisit-uat' THEN 1 ELSE 0 END;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N'EngineEdition'))=3
 AND CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N'Edition')) LIKE N'Developer Edition%'
 AND DB_NAME() LIKE N'OptionCIsolated[_]%'
 AND LEN(DB_NAME())=LEN(N'OptionCIsolated_')+32
 AND SUBSTRING(DB_NAME(),LEN(N'OptionCIsolated_')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%'
 AND TRY_CONVERT(int,SESSION_CONTEXT(N'FieldVisit.G1.NonDeploy'))=1 THEN 1 ELSE 0 END;
IF @G1Live=0 AND @G1Offline=0
 THROW 52140,N'G1_014_TARGET_STOP',1;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
 OR OBJECT_ID(N'dbo.Locations',N'U') IS NULL
 OR OBJECT_ID(N'dbo.Organizations',N'U') IS NULL
 OR OBJECT_ID(N'dbo.Teams',N'U') IS NULL
 OR OBJECT_ID(N'dbo.Users',N'U') IS NULL
 THROW 52141,N'G1_014_REQUIRED_OBJECT_STOP',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
              ORDER BY AppliedAt DESC,VersionNumber DESC),N'')<>N'1.8.0-010'
 OR EXISTS(SELECT 1 FROM dbo.SchemaVersions
    WHERE VersionNumber IN(N'1.8.0-011',N'1.8.0-012',N'1.8.0-013',
      N'1.8.0-014',N'1.8.0-015',N'1.8.0-016',N'1.8.0-017',
      N'1.8.0-018',N'1.8.0-019',N'1.8.0-020'))
 THROW 52142,N'G1_014_PREDECESSOR_STOP',1;
-- No row values, identifiers, counts, hashes of secrets, or business names returned.
SELECT code,status FROM (VALUES
 (N'014_APPROVED_MASTER_FIELDS',
  CASE WHEN EXISTS(
""" + predicates[0] + """
  ) THEN N'HOLD' ELSE N'PASS' END),
 (N'014_APPROVED_MASTER_AUTHORITY',
  CASE WHEN EXISTS(
""" + predicates[1] + """
  ) THEN N'HOLD' ELSE N'PASS' END)
) AS v(code,status) ORDER BY code;
"""
    # Structural defense against accidentally converting the source preflight
    # into a migration. BEGIN, EXEC, DML, DDL, row projection are not allowed.
    no_comments = re.sub(r"--[^\n]*", "", sql)
    if re.search(r"\b(?:INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|TRUNCATE|CREATE|GRANT|REVOKE|EXEC(?:UTE)?|OPENROWSET)\b", no_comments, re.I):
        raise SourceHold("nonread-only-token")
    if "SELECT code,status FROM" not in sql or "THROW 52140" not in sql:
        raise SourceHold("unsafe-output-or-target")
    return sql


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.output.exists():
            raise SourceHold("refuse-overwrite")
        sql = build_sql((ROOT / BACKFILL).read_bytes(), json.loads((ROOT / MANIFEST).read_text()))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(sql, encoding="utf-8")
        print("UAT010014SourceClosure=PASS; Generated=SELECT_ONLY; LiveAccess=NONE; DataValues=NONE; Gate=HOLD;")
        return 0
    except (OSError, ValueError, UnicodeError):
        print("UAT010014SourceClosure=HOLD; Details=SUPPRESSED; LiveAccess=NONE;")
        return 1


if __name__ == "__main__":
    sys.exit(main())
