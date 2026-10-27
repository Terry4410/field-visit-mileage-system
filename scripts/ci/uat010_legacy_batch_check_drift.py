#!/usr/bin/env python3
"""Nondeploy audit: prove PR50 synthetic ImportBatches CHECK omits required 1700_005 Confirming.

No database, Azure, network, credentials, or application runtime access.
Result is SOURCE_DRIFT_CONFIRMED, explicitly not LIVE_SCHEMA_PASS or promotion.
"""
import argparse
import hashlib
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
SYNTHETIC = "scripts/ci/run_full_candidate_migration.ps1"
HISTORICAL = "database/migrations/1700_005_people_bulk_confirm_claim/Up.sql"
CATALOG = "docs/release/g1-execution/promotion/CatalogContract.sql"
HARNESS = "scripts/ci/test_g1_promotion.ps1"

class SourceStop(Exception):
    pass

def old_check(text):
    b = re.search(r"\$boot=@'\r?\n(.*?)\r?\n'@", text, re.S)
    if not b:
        raise SourceStop("FIXTURE_MISSING")
    line = next((x for x in b.group(1).splitlines()
        if "CREATE TABLE dbo.ImportBatches(" in x), "")
    m = re.search(r"CK_ImportBatches_Status\s+CHECK\s*\(\s*Status\s+IN\s*\((.*?)\)\s*\)", line, re.I | re.S)
    if not m:
        raise SourceStop("FIXTURE_CHECK_MISSING")
    return re.findall(r"N'([A-Za-z]+)'", m.group(1))

def approved_check(text):
    m = re.search(r"ADD\s+CONSTRAINT\s+CK_ImportBatches_Status\s+CHECK\s*\(\s*Status\s+IN\s*\((.*?)\)\s*\)", text, re.I | re.S)
    if not m or "1.7.0-005" not in text:
        raise SourceStop("HISTORICAL_1700_005_CHECK_MISSING")
    return re.findall(r"N'([A-Za-z]+)'", m.group(1))

def validate(fixture, migration, catalog, harness):
    old = old_check(fixture)
    approved = approved_check(migration)
    if old != ["Previewed", "Confirmed", "PartiallyFailed"]:
        raise SourceStop("FIXTURE_DRIFT_UNKNOWN")
    if approved != ["Previewed", "Confirming", "Confirmed", "PartiallyFailed"]:
        raise SourceStop("HISTORICAL_SOURCE_DRIFT_UNKNOWN")
    if "sys.check_constraints" not in catalog or "CK_ImportBatches_Status" not in catalog or "definition" not in catalog:
        raise SourceStop("CATALOG_SOURCE_UNBOUND")
    if "function Fixture" not in harness or "$c=Fixture" not in harness or "$initial=Scalar $c $catalog" not in harness:
        raise SourceStop("REFERENCE_ORIGIN_UNBOUND")
    if old == approved:
        raise SourceStop("NO_DRIFT")
    return {
        "contract": "existing010-import-check-fixture-source-audit-v1",
        "fixtureAllowsConfirming": False,
        "approved1700005AllowsConfirming": True,
        "pr50InitialReferenceDerivedFromSimplifiedFixture": True,
        "importBatchStatusCheckIncludedInCatalogDigest": True,
        "sourceConflict": "CONFIRMED",
        "liveSqlInspected": False,
        "liveSchemaFidelity": "HOLD",
        "syntheticReferenceAcceptedAsLiveUat": False,
        "liveMigrationAuthorized": False,
        "azureAccess": False,
        "sqlWrite": False
    }

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--output", type=pathlib.Path)
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    p = [ROOT / x for x in (SYNTHETIC, HISTORICAL, CATALOG, HARNESS)]
    t = [x.read_text(encoding="utf-8-sig") for x in p]
    payload = validate(*t)
    if args.self_test:
        for col, changed in (
            (0, t[0].replace("N'PartiallyFailed'))", "N'PartiallyFailed',N'Confirming'))")),
            (1, t[1].replace("N'Confirming',", "N'Other',")),
            (2, t[2].replace("CK_ImportBatches_Status", "CK_OTHER")),
            (3, t[3].replace("$initial=Scalar $c $catalog", "$initial=N'fake'")),
        ):
            modified = list(t)
            modified[col] = changed
            try:
                validate(*modified)
            except SourceStop:
                pass
            else:
                raise SourceStop("NEGATIVE_TEST_FAILED")
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        payload["inputSha256"] = {path: hashlib.sha256(data.encode("utf-8")).hexdigest()
            for path, data in zip((SYNTHETIC,HISTORICAL,CATALOG,HARNESS),t)}
        data = (json.dumps(payload, sort_keys=True, indent=2) + "\n").encode("utf-8")
        args.output.write_bytes(data)
        digest = hashlib.sha256(data).hexdigest()
        args.output.with_suffix(".sha256").write_text(digest + "  " + args.output.name + "\n")
    print("Existing010SyntheticImportStatus=SOURCE_CONFLICT_CONFIRMED; LiveG1=HOLD; Azure=NONE;")

if __name__ == "__main__":
    try:
        main()
    except SourceStop as e:
        print("Existing010SyntheticImportStatus=STOP; Reason=" + str(e))
        raise SystemExit(1)
