# Scheme A — Excel published Location Update (Recovery-only, NONDEPLOY)

Status: **ENGINEERING CANDIDATE / HOLD / NO-GO**. The latest exact-SHA
CI must pass before this increment is accepted. No UAT/Production writes,
migrations, deployments, flags, promotions or Business UAT are authorized.

## One canonical review pipeline

The existing Excel **Create** review submission remains complete and
unchanged. The new Update-only candidate route is
`POST /api/v1/optionc-candidate/imports/{importBatchId}/submit-updates-for-review`.
It is protected by `OptionC:Candidate:Enabled` (OFF by default), loopback
SQL Server 2022 `OptionCIsolated_*` execution policy, the canonical
011/012/014 and additive Create/LowRisk/Stage/Leader contracts, and
`1.8.0-EXCEL-CANONICAL-STATUS`.

Actual Excel/CSV Preview stages `Update` rows as `Error` with the
explicit `LOCATION_EXCEL_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL`
marker and stores the published source's RowVersion in the staged JSON.
This marker is a controlled handoff, **not** a valid legacy Confirm.
The new route preflights every row before any canonical request:
only published Customer Update rows and unchanged rows are accepted;
Create/Update mixed batches, unknown errors, invalid team or missing
RowVersion fail closed. No direct published Location write occurs.

Each eligible row enters the **existing** canonical
`V180OptionCCandidateService.SubmitCoreAsync` in a Serializable SQL
transaction. The live actor, organization, team, location ownership and
operation eligibility are rechecked; a caller cannot submit on another
owner's behalf. The LocationCode, TeamCode, all staged mutable fields,
and preview RowVersion are checked against the locked published source.
The source's TaxId and MasterNote are preserved because the legacy Excel
template does not contain those fields. The server classifies LowRisk
versus HighRisk; the same transaction creates the ChangeRequest, private
Stage, immutable Submitted audit with batch/row identity, and updates
ImportBatchItem to SubmittedForReview.

Review is unchanged: independent live-appointed Leader first-level;
LowRisk requires independent Leader and duplicate evidence; HighRisk
requires Leader, geocode/duplicate evidence and independent Admin final
approval. The existing atomic publisher preserves LocationId, RowVersion
invariants, published version history and historical Trip/Snapshot
references. No second approval workflow exists.

## Replay, concurrency and failure semantics

An already SubmittedForReview row is resolved via exactly one canonical
Submitted audit event for its ImportBatchId and ImportBatchItemId;
no duplicate request or LocationId is allocated. Unknown-commit recovery
must use the canonical audit before retry. A stale preview RowVersion,
pending request, ownership revocation, changed team, invalid status,
missing additive contract, or unexpected row status fails closed.
Each row is transaction-atomic. If a later row fails, earlier rows remain
traceable as PartiallySubmittedForReview, and retries recover their
original RequestPublicIds. The import batch becomes SubmittedForReview
only after all eligible Update rows are linked.

The existing legacy `/imports/{id}/confirm` remains prohibited for
published Updates; it cannot substitute for Leader/Admin review.
`OPTIONC_EXCEL_UPDATE_UI_ENABLED=false` is a separate disabled frontend
gate. The candidate AdminImportPanel offers a reviewed Update action
only when that gate is separately authorized and the preview is
Update-only; it displays canonical Request IDs rather than claiming
publication.

## Migration dependency order (draft only)

1. Validate exact target and canonical 011 -> 012 -> 014 with catalog preflight.
2. Apply additive Stage, Stage Evidence audit and Leader review union
   constraints in compatible, verified order.
3. Apply Create/LowRisk reservation and versioned publication contracts.
4. Apply **`1.8.0-EXCEL-CANONICAL-STATUS`** from
   `excel-import/ExcelImportStatusContract.disposable.sql` with enabled
   `CK_ImportBatches_Status` and `CK_ImportBatchItems_Status` accepting
   `PartiallySubmittedForReview` and `SubmittedForReview`.
5. Verify SQL constraints, status transition, rerun stop/rollback, audit
   compatibility, API DI, isolated HTTP/SQL E2E and disabled flags.
6. Only after all gates pass, request separate Owner UAT target preflight
   and execution authorizations. Migration 013 remains reserved/untouched.

## Known limitations / acceptance

The route deliberately refuses mixed Create/Update batches instead of
attempting unverified partial mixed routing. The current published Update
requires the effective importing Leader to own the Location; admin users
cannot impersonate a Visitor owner. Cross-owner bulk maintenance needs
a separate explicit delegated-operation authorization contract, not a
silent relaxation of `AuthorizeSubmissionAsync`.

Mandatory remaining engineering evidence: full Update-only multi-row
partial-failure/retry matrix, stale preview, concurrent legacy mutation,
revocation, low-risk Excel Update, and report/export/Trip/mileage
write-path sweep. Existing HTTP tests verify positive canonical high-risk
publication and history only when the corresponding exact-SHA CI succeeds.
No Business UAT GO.
