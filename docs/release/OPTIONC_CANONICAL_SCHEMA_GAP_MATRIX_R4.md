# Option C canonical schema gap matrix — R4

Status: **DISPOSABLE ENGINEERING ONLY — UAT compatibility remains HOLD.**

## Verified canonical core

The CI SQL Harness now executes byte-identical copies of canonical 011, 012,
and 014 `Preflight.sql`, `Up.sql`, `Verify.sql`, and the canonical 014
`Backfill.sql`. The candidate copies are pinned by a byte-equality test. The
real HTTP/DI/SQL test Harness uses the canonical source files and checks
version-1 JSON, SHA-256, publisher, source RowVersion, and trusted 014
constraints.

The 010 bootstrap is still a synthetic test schema. It includes only the
columns and parent rows needed by these migrations and tests; it is not proven
byte-for-byte or catalog-fingerprint equivalent to the production 010 schema.
The isolated SQL tests therefore prove canonical 011/012/014 behavior against
that test baseline, not UAT compatibility.

## Add-on schema comparison

| Step | Actual contract source / runtime consumer | R3 `candidate/1800_xxx` shape | Gap and status |
|---|---|---|---|
| 011 | Canonical `ChangeRequests` (20 columns incl. expected version, before/proposed/evidence JSON, review/apply state, `rowversion`) and `ChangeRequestEvents` (8 columns incl. correlation/decision keys); six trusted FKs, request/event CHECKs and named indexes. Used by EF `V180B3ChangeRequest`/`V180B3ChangeEvent`. | Candidate copy is now byte-identical to canonical SQL. | **Aligned to canonical source.** 010 baseline fidelity remains unproven. |
| 012 | Canonical widening of request/event states for Approved/Applied; trusted CHECK replacements, preserving 011 columns/indexes/FKs. | Candidate copy is now byte-identical to canonical SQL. | **Aligned to canonical source.** |
| 013 | Reserved leader-grant slot. | No migration files or marker added. | **Unchanged / reserved.** |
| 014 | Canonical `LocationPublicationStages` and `LocationPublishedVersions`: full publication/evidence fields, binary SHA-256, source/publisher/version fields, rowversion on Stage; trusted FK/CHECKs and filtered unique indexes. Explicit Approved+Active version-1 JSON projection. | Candidate copies now byte-identical to canonical SQL and backfill. | **Aligned to canonical source.** Historical completeness is asserted in SQL integration tests; real UAT rows were not read. |
| 015 | `ChangeRequestEvents` CHECK extension for `GeocodeEvidenceRecorded` and `DuplicateReviewEvidenceRecorded` in `audit/StageEvidenceAuditEvents.disposable.sql`. | Creates a separate `StageEvidenceAuditEvents` table with a different row shape. | **Mismatch.** Separate table does not extend the canonical event ledger. |
| 016 | `ChangeRequestLeaderReviews`: request/reviewer FKs, review state CHECKs, JSON evidence, `rowversion`, unique request/stage and decision-key indexes. Used by EF `V180OptionCLeaderReview`. | Creates `LeaderReviewDetails` with different column and constraint names. | **Mismatch.** Actual Writer tests use the additive disposable contract, not this fixture. |
| 017 | `LocationCreateReservations`: request/location/org/team FKs, pending/published/abandoned status CHECK, `rowversion`, unique Location reservation. Used by CreateLocation Writer and Stage guard. | Creates `CreateLowRiskContracts`; no reservation fields or uniqueness contract. | **Mismatch.** Actual Writer tests use `CreateLowRiskContract.disposable.sql`. |
| 018 | Extends canonical request/event CHECKs for direct LowRisk publication and audit while keeping publication in the canonical ledger. | Creates `DirectLowRiskAudit`; not consumed by the Writer. | **Mismatch.** Actual Writer tests use `LowRiskDirectContract.disposable.sql`. |
| 019 | `LocationMergeDetails`: canonical ChangeRequest FK; source/survivor IDs, org/team, both rowversions, before/proposed/evidence JSON, scoped CHECKs. Used by merge submission, publication and reconciliation. | Minimal `RequestId`, source/survivor IDs, key and timestamp; wrong FK and missing business evidence. | **Mismatch.** Actual MergeFields HTTP tests use `merge/LocationMergeDetails.disposable.sql`. |
| 020 | Additive status CHECK updates to existing `ImportBatches` and `ImportBatchItems`, preserving batch identity, row data, request link, status and audit semantics. Used by the actual Excel preview/submission flow. | Creates a separate `ExcelImportStatus` table. | **Mismatch.** Actual Excel Create HTTP tests use `excel-import/ExcelImportStatusContract.disposable.sql`; Excel Update remains unverified against a complete production baseline. |

## E2E evidence boundary

Existing isolated tests exercise real application HTTP/DI/EF/SqlClient paths
for CreateLocation, LowRisk direct publication, Visitor high-risk submission,
independent Leader review, Stage evidence, Admin publication, MergeFields and
Excel Create against canonical 011/012/014 plus the relevant additive
disposable contracts. The new assertions preserve all four Trip/Mileage
history tables during the Leader publication and MergeFields scenarios.

The independent full-chain driver still executes simple 015–020 candidate
fixtures after the canonical core. It labels those as candidate add-on
fixtures; they are not evidence that numeric 015–020 are canonical migrations.
The repository has not yet bound those six numeric migration markers to the
actual additive contracts, and the actual 010 catalog fingerprint has not
been replayed in the disposable writer Harness. Accordingly there is no
complete 010→020 canonical migration or UAT compatibility PASS.
