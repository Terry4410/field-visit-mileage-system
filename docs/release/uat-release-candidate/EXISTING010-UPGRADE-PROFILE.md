# Field Visit v1.8.0 — Existing010Upgrade Runtime Trust (NONDEPLOY)
 
This is a **new release profile**, distinct from the accepted PR48 NewEmpty/69-table/3-capability RC. The PR48 immutable artifact and PR50 atomic promotion SQL/Verify stay unchanged. This delta adds a runtime enforcement path and signed-profile tests; it does not certify the actual UAT010 baseline, a post-migration schema fingerprint, new API/Frontend candidate or rollback/deployment.

## Required upgrade contract and signed attestation

A **newly built and independently tested release** must supply its own exact `candidateSourceSha`, 4-assembly API hash, frontend ZIP hash, *complete post-migration* `schemaSha256`, plus:
- `schemaProfile = "Existing010Upgrade"` (the only new accepted profile);
- `tableCount`: exact independently observed final dbo table count, bounded 70–100, **never guessed** or taken from the NewEmpty manifest;
- `historicalMarkerSha256`: SHA-256 of UTF-8 encoding of the exact SQL `FOR JSON PATH, INCLUDE_NULL_VALUES` ordered projection `VersionNumber,Description,AppliedAt,AppliedBy` for the **26 predecessor rows**, excluding the 13 source-approved 011–020 new marks;
- `promotionPackageId`: the signed, **approved** SHA-256-sized PR50 promotion package ID, verified against each of nine `FieldVisitMigrationLedger` rows;
- `ledgerSha256`: SHA-256 of the UTF-8 `FOR JSON PATH, INCLUDE_NULL_VALUES` SQL projection `VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt` ordered by VersionNumber for all nine ledger rows.

No token/secret/identity, raw historical row or SQL definition should be published. Capture private nonsecret *hashes only* after a separately reviewed, safely recoverable migration. The five upgrade-specific fields must be present, equal to independently checked deployment facts and inside the same HMAC-signed time-bounded runtime attestation. Existing NewEmpty attestation remains valid for **its** original profile but **cannot** authorize Existing010Upgrade.

## Runtime fail-closed rules

- Fixed UAT Azure SQL host/database; TLS certificate verification; same principal/source/API/frontend/schema exact-hash pin; non-owner/no-DDL and deny mutation of SchemaVersions, SchemaMigrationDataBaselines and the new ledger.
- Strict table-count and full catalog SHA; **26 exact predecessor rows**, precisely 13 new names including the four approved aliases, no reserved `1.8.0-013`; one `1.8.0-010`; 39 total history rows for the proven predecessor shape. All 26 predecessor *values* must hash to the approved baseline, not just counts.
- Precisely nine ledger versions 011,012,014–020 and common approved package ID; exact ledger SHA bound to runtime release and HMAC. Each cumulative new marker set must hash to the corresponding ledger `MarkersSha256` (as produced by PR50), refusing post-commit marker content changes.
- Any missing metadata, hidden permissions, source hash drift, unknown history, wrong profile/slot, unknown connection principal, extra ledger/marker, token mismatch, untrusted structure or inconsistent state DENIES. No automatic repair/skip/replay/DDL.
- No grant/identity change, live SQL migration, empty-db installer, key injection, flags or Azure deploy in this NONDEPLOY source delta.

## Strict boundaries and remaining P0

The new `prepare_candidate.py` source overlay copies the upgrade gate into a **new build identity**; it does not repack or modify the accepted PR48 ZIP. The existing `sign_activation.py` can sign an externally inspected, exact new upgrade contract but never activates it. It rejects unsigned/missing profile fields. The focused compilation/signature test uses synthetic facts and does not query or impersonate Azure SQL.

Real pre-010 lineage and 63-table catalog must be compared with PR50's exact source prerequisite contract. The original PR50 synthetic 26-history regression PASS is **not** this validation. Before any in-place existing UAT migration or release: verify correct live API application principal, accepted current binary/frontend fingerprints, complete post-upgrade catalog, exact prechange API+FE+DB recovery pair, stable flags and independent safety review; otherwise `G1=HOLD`, `BusinessUAT=NO-GO`.

**Separation:** NewEmpty remains frozen; Existing010Upgrade is a new source/compiled artifact requiring its own hashes, tests, operator release plan and independent acceptance. Do not merge this stack into PR48 or Protected until that acceptance.
