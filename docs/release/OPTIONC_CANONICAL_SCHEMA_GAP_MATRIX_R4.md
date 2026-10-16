# Option C canonical schema gap matrix — R5

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
| 015 | `ChangeRequestEvents` CHECK extension for `GeocodeEvidenceRecorded` and `DuplicateReviewEvidenceRecorded` in `audit/StageEvidenceAuditEvents.disposable.sql`. | Candidate migration copies the real `audit/StageEvidenceAuditEvents.disposable.sql` contract and extends canonical `ChangeRequestEvents` CHECKs. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** |
| 016 | `ChangeRequestLeaderReviews`: request/reviewer FKs, review state CHECKs, JSON evidence, `rowversion`, unique request/stage and decision-key indexes. Used by EF `V180OptionCLeaderReview`. | Candidate migration copies `leader-review/LeaderReviewDetails.disposable.sql`, including `ChangeRequestLeaderReviews`, FKs, CHECKs, indexes, and rowversion. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** |
| 017 | `LocationCreateReservations`: request/location/org/team FKs, pending/published/abandoned status CHECK, `rowversion`, unique Location reservation. Used by CreateLocation Writer and Stage guard. | Candidate migration copies `create-lowrisk/CreateLowRiskContract.disposable.sql`, including reservation identity, status, scoped FKs, and unique Location index. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** |
| 018 | Extends canonical request/event CHECKs for direct LowRisk publication and audit while keeping publication in the canonical ledger. | Candidate migration copies `lowrisk-direct/LowRiskDirectContract.disposable.sql`, extending canonical request/event checks and preserving the ledger. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** |
| 019 | `LocationMergeDetails`: canonical ChangeRequest FK; source/survivor IDs, org/team, both rowversions, before/proposed/evidence JSON, scoped CHECKs. Used by merge submission, publication and reconciliation. | Candidate migration copies `merge/LocationMergeDetails.disposable.sql`, with ChangeRequest FK, organization/team/rowversion/JSON fields and checks. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** |
| 020 | Additive status CHECK updates to existing `ImportBatches` and `ImportBatchItems`, preserving batch identity, row data, request link, status and audit semantics. Used by the actual Excel preview/submission flow. | Candidate migration copies `excel-import/ExcelImportStatusContract.disposable.sql`, widening status CHECKs on existing EF ImportBatches and ImportBatchItems. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** Excel Create/Update app tests are not run on this same chain database. |

## R5 additive chain evidence boundary

The disposable full-chain driver now invokes the six additive contract sources
used by the real Writer (`audit`, `leader-review`, `create-lowrisk`,
`lowrisk-direct`, `merge`, and `excel-import`) as numeric candidate migrations
015–020. Each has a fail-closed Preflight, atomic Up and numeric marker,
structural Verify, and SHA-256 emitted by the driver. SQL runs only in a newly
created `OptionCIsolated_*` SQL Server 2022 database; 013 remains absent.

The legacy `ImportBatches` / `ImportBatchItems` baseline in this driver is a
synthetic EF-shaped 010 fixture, not an observed UAT catalog. The six numeric
migrations have not yet been proven in the same database as the complete real
HTTP Writer sequence. Migration-chain PASS therefore does not establish UAT
compatibility or Writer same-database E2E.

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
