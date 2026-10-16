# Option C canonical schema gap matrix — R6

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
| 020 | Additive status CHECK updates to existing `ImportBatches` and `ImportBatchItems`, preserving batch identity, row data, request link, status and audit semantics. Used by the actual Excel preview/submission flow. | Candidate migration copies `excel-import/ExcelImportStatusContract.disposable.sql`, widening status CHECKs on existing EF ImportBatches and ImportBatchItems. | **Aligned to the isolated Writer contract; UAT compatibility HOLD.** The R5 same-chain HTTP test runs Excel Create and Update preview/submission on the migrated disposable database. |

## R5 additive chain evidence boundary

The disposable full-chain driver now invokes the six additive contract sources
used by the real Writer (`audit`, `leader-review`, `create-lowrisk`,
`lowrisk-direct`, `merge`, and `excel-import`) as numeric candidate migrations
015–020. Each has a fail-closed Preflight, atomic Up and numeric marker,
structural Verify, and SHA-256 emitted by the driver. SQL runs only in a newly
created `OptionCIsolated_*` SQL Server 2022 database; 013 remains absent.

The legacy `ImportBatches` / `ImportBatchItems` baseline in this driver is a
synthetic EF-shaped 010 fixture, not an observed UAT catalog. The SQL migration
runner and each application integration test create separate disposable
databases. R5 added full-chain HTTP runs for LowRisk Direct and Excel Create /
Update. R6 adds full-chain HTTP variants for Visitor CreateLocation, Visitor
HighRisk → Leader → application-owned evidence → independent Admin publication,
and MergeFields. Each scenario uses a newly migrated 010 fixture → 020
database; the scenarios are not yet one sequential business journey in a
single shared database. This distinction remains an integration gap.

## E2E evidence boundary

## R6 010 schema fidelity boundary

Protected source `ce70eddc290e85375c64311b6d96eedc09942dba` contains the real
1600/1700 history and 1800_001–010 migrations. Existing authorized read-only
evidence (run `38707441735`) proves UAT `db-fieldvisit-uat` was at
`1.8.0-010`, reports catalog fingerprint
`665C66D7BA7DE5F75AD855CDDDA10175E8AB31114B6572EC010813799BFCD975`, and
shows the four Option C phase flags absent. That evidence did **not** return
the full UAT column/type/nullability, key, index, check, or rowversion
inventory. Its catalog fingerprint is based on dbo object names/types/modify
dates, so it cannot prove column-level fidelity. Run `38835570403` re-used the protected workflow's preflight SQL pinned to SHA-256 `44c20438c8125150d8cdbfae5e2dab40f23acea6386507b96bec31ff3cb10955`; it did not execute the new inventory query. The protected workflow has no compatible source-hash/output validator for that inventory. Its empty FK/CHECK/index result sets are not a whole-catalog inventory.

| Fidelity area | Protected 001–010 / EF source | Authorized actual UAT evidence | R6 result |
|---|---|---|---|
| Migration state | 1800_001–010 protected chain | Latest `1.8.0-010`; SchemaVersions rows captured | State matches; no replay of 001–010 against UAT |
| Tables / columns / SQL types / nullability | Readable in Protected migration SQL and `AppDbContext` mappings | Full UAT inventory was not returned | **Unknown / HOLD** |
| PK / FK / CHECK / indexes / trusted state | Readable in migration SQL; compare requires target definitions | No whole-catalog definitions returned | **Unknown / HOLD** |
| RowVersion | Required by EF and several writer contracts | UAT column-level evidence absent | **Unknown / HOLD** |
| Existing-data and Backfill prerequisites | Canonical 014 requires exact approved active Location projections; prerequisites defined by source SQL | Summary says Option C tables absent; no row-level/location compatibility projection returned | **Unknown / HOLD** |
| Catalog fingerprint | Protected query emits dbo object-name/type/modify-date hash | Exact hash above | Hash matches recorded evidence only; it is not a schema-shape proof |

`UatSchemaFidelity.readonly.sql` (SHA-256 `${sqlHash}`) prepares a metadata-only inventory for a future separately approved read-only evidence run. Static review confirms exact expected server/database guards, system-catalog shape queries, aggregate-only historical row counts/high-watermarks, and no business-row values or actor identity fields. The query has not been run against UAT. Its hash is pinned by `scripts/ci/test_r6_schema_fidelity.py`; changes invalidate that test and require re-review. 010 fixture parity and UAT compatibility therefore remain HOLD.

## R6 real writer evidence

The new tests call the real ASP.NET controllers, DI, EF and SqlClient after
the full candidate migration chain. CreateLocation, LowRisk Direct, HighRisk
submission/Leader review/evidence/Admin publication, MergeFields, and Excel
Preview/Create/Update/Replay each run against a database that has reached
1.8.0-020. The SQL fixtures seed actor identities and history only; they do
not issue the business writes being asserted. Each flow currently has its own
disposable migrated database rather than sharing one database across all
eleven R6 cases. The dedicated CreateLocation test also calls the operational
project-location endpoint and proves its pending reservation is excluded.

Real Browser E2E is still not run. CI has only the 3-test Playwright suite
whose API responses are fully mocked (`MOCK PASS`). Real isolated SQL-backed
Browser, backup Restore, UAT execution, feature activation and Business UAT
remain HOLD. Migration 013 stays absent/reserved.
