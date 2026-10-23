#!/usr/bin/env python3
"""Offline, source-derived PRE-010 trigger invariants. No Azure, SQL or deployment.

This source-only manifest deliberately does not claim to reconstruct the 1.5
database, prove the deployed 010 schema, or know SQL Server's stored trigger
definition hashes. Actual G1-S parity therefore remains HOLD.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

REFERENCE_SHA256 = "f86a42ad88230411b2351a59cd9c1bb40ad2586076631ab249949fce13e91eb7"
REFERENCE_PATH = "docs/release/g1-minimal-preflight/necessary-reference.json"
EXPECTED_RC = "f29affb3299828f45b1f131321779e8bea2f86a8"
TRIGGER_HEADER = re.compile(
    r"\bCREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+(?:\[?dbo\]?\.)?\[?([A-Za-z_][A-Za-z_0-9]*)\]?"
    r"\s+ON\s+(?:\[?dbo\]?\.)?\[?([A-Za-z_][A-Za-z_0-9]*)\]?"
    r"\s+AFTER\s+(INSERT|UPDATE|DELETE)(?:\s*,\s*(INSERT|UPDATE|DELETE))?"
    r"(?:\s*,\s*(INSERT|UPDATE|DELETE))?\s+AS\b", re.I
)
TRIGGER_NAME = re.compile(r"\bCREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+(?:\[?dbo\]?\.)?\[?([A-Za-z_][A-Za-z_0-9]*)\]?", re.I)
EXPECTED_TRIGGER_SOURCES = {
    "001": 1, "002": 5, "003": 7, "004": 2, "005": 1,
    "006": 0, "007": 5, "008": 1, "009": 0, "010": 0,
}
MANDATORY_010_CHECK = "CK_RouteCalculationAttempts_Reason"
FORBIDDEN_PRE010_CHECKS = {"CK_ImportBatches_Status", "CK_ImportBatchItems_Status"}
EXPECTED_LAST_TRIGGER_VERSION = {"TR_MileageRateRules_ProtectSeries": "008"}

class ContractStop(Exception):
    def __init__(self, code: str):
        self.code = code

def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()

def parse_triggers(raw: bytes, migration: str) -> list[dict]:
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeError:
        raise ContractStop("SOURCE_ENCODING")
    names = set(TRIGGER_NAME.findall(text))
    parsed = []
    for match in TRIGGER_HEADER.finditer(text):
        events = sorted(set(x.upper() for x in match.groups()[2:] if x))
        parsed.append({
            "Name": match.group(1),
            "TableName": match.group(2),
            "Events": ",".join(events),
            "SourceMigration": migration,
            "DefinitionSha256": None,
            "DefinitionProof": "UNKNOWN_PENDING_DISPOSABLE_SQL_SERVER",
            "ExpectedEnabled": True,
            "ExpectedInsteadOf": False,
        })
    if len(parsed) != EXPECTED_TRIGGER_SOURCES[migration] or names != {x["Name"] for x in parsed}:
        raise ContractStop("TRIGGER_SOURCE_SHAPE")
    if len(parsed) != len(names):
        raise ContractStop("TRIGGER_SOURCE_DUPLICATE")
    return parsed

def derive(candidate_root: Path) -> dict:
    root = candidate_root.resolve()
    reference_file = root / REFERENCE_PATH
    raw = reference_file.read_bytes()
    if sha256(raw) != REFERENCE_SHA256:
        raise ContractStop("REFERENCE_PIN")
    reference = json.loads(raw)
    if reference.get("rc") != EXPECTED_RC or reference.get("scope") != "CANDIDATE_NECESSARY_CONTRACT_NOT_OBSERVED_UAT":
        raise ContractStop("REFERENCE_SCOPE")
    inputs = reference.get("inputs")
    if not isinstance(inputs, dict) or len(inputs) != 22:
        raise ContractStop("SOURCE_MANIFEST")
    for key, digest in inputs.items():
        if not isinstance(key, str) or key.startswith("/") or ".." in Path(key).parts or not re.fullmatch(r"[0-9a-f]{64}", str(digest)):
            raise ContractStop("SOURCE_MANIFEST")
        if sha256((root / key).read_bytes()) != digest:
            raise ContractStop("SOURCE_DRIFT")
    trigger_map = {}
    sources = {}
    for mid in EXPECTED_TRIGGER_SOURCES:
        options = [p for p in inputs if re.fullmatch(rf"database/migrations/1800_{mid}_[^/]+/Up\.sql", p)]
        if len(options) != 1:
            raise ContractStop("MIGRATION_SOURCE_SCOPE")
        path = options[0]
        sources[mid] = {"Path": path, "Sha256": inputs[path]}
        for item in parse_triggers((root / path).read_bytes(), mid):
            trigger_map[item["Name"]] = item  # later migrations supersede earlier definitions
    if len(trigger_map) != 21 or any(trigger_map.get(name, {}).get("SourceMigration") != mid for name, mid in EXPECTED_LAST_TRIGGER_VERSION.items()):
        raise ContractStop("TRIGGER_EFFECTIVE_SOURCE")
    m010 = (root / sources["010"]["Path"]).read_text(encoding="utf-8-sig")
    if MANDATORY_010_CHECK not in m010 or "BackgroundMileageJob" not in m010:
        raise ContractStop("PRE010_CHECK_SOURCE")
    if any(x in m010 for x in FORBIDDEN_PRE010_CHECKS):
        raise ContractStop("PRE010_POST_MIXED")
    # Only source-proven facets are represented; historical base DDL is unknown.
    return {
        "ContractVersion": "g1-source-derived-pre010-scope-v1",
        "Source": "PINNED_001_TO_010_MIGRATIONS",
        "RC": EXPECTED_RC,
        "ReferenceSha256": REFERENCE_SHA256,
        "Migrations": sources,
        "Triggers": [trigger_map[n] for n in sorted(trigger_map)],
        "Checks": [{"Name": MANDATORY_010_CHECK, "SourceMigration": "010",
                    "ExpectedEnabled": True, "ExpectedTrusted": True,
                    "DefinitionProof": "SOURCE_DDL_REVIEWED_NOT_UAT_OBSERVED"}],
        "Post010ChecksExcluded": sorted(FORBIDDEN_PRE010_CHECKS),
        "Base15ColumnFacetParity": "UNKNOWN",
        "HistoryPreservation": "UNKNOWN",
        "ActualUat010": "UNKNOWN",
        "RuntimeApiPrincipal": "UNKNOWN",
        "ReleaseGate": "HOLD",
    }

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", type=Path, required=True)
    parser.add_argument("--private-output", type=Path)
    args = parser.parse_args()
    try:
        contract = derive(args.candidate_root)
        if args.private_output:
            # Private output is for later restricted use, never Actions artifact/log.
            if not args.private_output.is_absolute() or args.private_output.exists():
                raise ContractStop("PRIVATE_OUTPUT")
            args.private_output.write_text(json.dumps(contract, ensure_ascii=False, sort_keys=True, indent=2), encoding="utf-8")
        print("Pre010Source=PASS; TriggerNamesEvents=21; DefinitionHashProof=UNKNOWN; BaseFacets=UNKNOWN; ObservedUAT010=UNKNOWN; ReleaseGate=HOLD;")
        return 0
    except ContractStop as ex:
        print(f"Pre010Source=STOP; Class={ex.code}; Details=SUPPRESSED;")
    except Exception:
        print("Pre010Source=STOP; Class=INVALID; Details=SUPPRESSED;")
    return 1

if __name__ == "__main__":
    sys.exit(main())
