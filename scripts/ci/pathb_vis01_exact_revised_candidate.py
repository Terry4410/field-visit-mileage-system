"""Build EXACT immutable VIS01 revised 02 candidate from pinned locked review + one reviewed gate.
This source is NONDEPLOY CI only. It must match the already delivered ZIP member byte-for-byte.
"""
from hashlib import sha256
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
EXPECTED_SHA = "bb2272c4c16674edd3dfd90473e8515e020da93812b0fa17d8856812d30dee77"
EXPECTED_LENGTH = 15223
HEADER = """-- FIELD VISIT PATH B: VIS01 SYNTHETIC TRIP-CONTEXT INCREMENTAL EXECUTION CANDIDATE
-- NOT APPROVED FOR AZURE until Owner authorizes this exact new SHA-256.
-- One-shot only; stop on HOLD, errors or unknown result. Never replay the previous 20-row seed.
-- NEW EXACT-SHA OWNER APPROVAL REQUIRED BEFORE RUN; NOT PREVIOUSLY AUTHORIZED
"""
def build() -> str:
    locked = (ROOT / "database/uat/FieldVisit_PathB_VIS01_Context_Seed_LOCKED_REVIEW.sql").read_text(encoding="utf-8")
    gate = (ROOT / "scripts/ci/pathb_vis01_revised_seed_preflight.sql").read_text(encoding="utf-8")
    assert locked.count("SET NOCOUNT ON;") == 1
    assert locked.count("DECLARE @now datetime2(3)") == 1
    assert locked.count("THROW 59500, 'LOCKED_REVIEW_ONLY_NOT_AUTHORIZED_FOR_SQL_WRITE', 1;") == 1
    assert gate.startswith("-- Make all schema/permission failures happen before the first write.\n")
    assert gate.endswith("\n\n")
    body = locked[locked.index("SET NOCOUNT ON;"):]
    data = (HEADER + body.replace("DECLARE @now datetime2(3)", "\n" + gate + "DECLARE @now datetime2(3)", 1)).encode("utf-8")
    assert len(data) == EXPECTED_LENGTH, f"REVISED_02_SIZE_MISMATCH: {len(data)}"
    assert sha256(data).hexdigest() == EXPECTED_SHA, "REVISED_02_EXACT_SHA_MISMATCH"
    return data.decode("utf-8")

if __name__ == "__main__":
    import sys
    sys.stdout.write(build())
