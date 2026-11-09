# Field Visit Path B — R4 Durable Catalog NONDEPLOY

This is a **new, separately authorization-locked R4 candidate**, forked from consumed R3
HEAD `d34a248394c2a583966fd7bf376a7f97827113c0`. It is NOT the R3
once-only operation and may NOT reuse any R3 approval, Blob marker, local claim
or cloud token. Original release, frozen SQL, UAT CI and installer remain untouched.

## Exact new candidate and binding
- Candidate ZIP SHA-256: `c3317aeb4a61ce69c685986aedb1cbb711563f379446619a8a13437691ad3693`
- Binding index SHA-256: `bb90f177e3c5dbc0556324ad0f0004d3fc07ab798cc980eee238f0a92cbea13a`
- Fixed SQL SHA-256: `d5a4f5825cd95227661921ed8577882053998f0fb103c49b64658f69ccca39b0`
- New marker: `operator-once/pathb-owner-catalog-once-r4-durable-20261109.json`
- New operation-specific local claim/receipt namespace:
  `/home/LogFiles/fv-pathb-owner-catalog-r4-<32-character-operation-id>...`

Use `build_owner_catalog_candidate.py` to produce the deterministic candidate
and binding from this exact source; compare SHA before any proposal to run it.

## Minimal engineering delta
- Child validates a new exact Operation ID and immutable **Candidate ZIP SHA**,
  rather than persisting the SQL script SHA in the receipt.
- Stage includes `owner_catalog_durable_receipt_REVIEW_ONLY.py` as the fourth ZIP
  member and verifies its SHA before any token can be passed.
- Child writes one fixed sanitized result record to a unique no-overwrite path
  **before printing a success receipt**. File and directory fsync are attempted,
  and any failed or uncertain write yields HOLD with no SQL replay.
- An independently authorized read-only `read_once(operation_id,candidate_sha)`
  can recover a validated prior record without running SQL. Missing records
  never prove no SQL was attempted.
- Unique R4 Blob claim and remote stage avoid the consumed R3 operation.
  No new Azure infrastructure is required.

## Security and status
**NONDEPLOY / NOT LIVE AUTHORIZED.** R4 has a distinct authorization decision,
and no fresh R4 Owner approval has been issued. The default adapter and Child
entrypoints remain locked; do not invoke `--execute` without new exact-SHA
authorization and independent review. No Azure, SSH, Storage Token, Blob PUT,
SQL Token, SQL connection, DDL, migration, installer, deployment, permission or
network change was executed for R4.

82/82 offline unit/contract regression tests PASS locally on source rebuilt
from the original complete R3 source, with the R4 changes; core candidate and
control files matched GitHub Git blobs. This is NOT proof of App `/home/LogFiles`
filesystem durability in Azure or end-to-end live reliability. Verify the
mount's ownership/fsync behavior without consuming SQL authorization before
making a live claim.

The original R3 one-shot remains consumed, the R3 marker was read ABSENT,
current database catalog remains UNKNOWN, Business UAT remains NO-GO.
IT handover documentation is deferred.
