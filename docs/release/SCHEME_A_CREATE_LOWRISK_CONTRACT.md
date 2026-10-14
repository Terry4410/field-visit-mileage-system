# Scheme A — CreateLocation / LowRisk controlled contract (NONDEPLOY)

Status: **ENGINEERING CANDIDATE / NO-GO**. This document and the
`create-lowrisk/CreateLowRiskContract.disposable.sql` candidate do not
authorize UAT/Production SQL, deployment, promotion or feature activation.

## Owner-confirmed behavior

- Visitor Location changes require independent live-authorized Leader review,
  including LowRisk. HighRisk additionally requires independent Admin approval.
- Leader-originated changes require a *different* authorized reviewer. No
  self-approval; no qualified reviewer means Pending/HOLD, never auto-approve.
- CreateLocation defaults to **High** because a new address/identity cannot
  be inferred safe from absence of an old value. An explicit, separately
  reviewed future policy would be required to classify any create as Low.
- Server-owned field comparison classifies existing published updates as High
  when city, district, address, plus code or tax ID changes; other changes
  may be Low only when an actual changed field is proven.
- Legacy UI, Excel and background publishing may not bypass the canonical
  request/review/publication gates. Deployed feature flags remain OFF.

## Source identity and SQL contract

1. **CreateLocation** reserves a new, stable LocationId using a
   `Locations` row that is **Pending and inactive**, not selectable or
   publishable. Reservation, canonical `ChangeRequests`, `Submitted`
   event and `LocationCreateReservations` detail must be inserted in one
   Serializable transaction; the same reserved row's 8-byte RowVersion is
   bound to `ExpectedEntityRowVersion`.
2. **UpdatePublishedLocation / Low** must bind the original Approved/Active
   LocationId, original 8-byte RowVersion, before-image JSON and proposed
   JSON. The Leader's decision is mandatory before any publish transaction.
3. **UpdatePublishedLocation / High** retains the existing independent
   Leader -> Admin, geocode/duplicate evidence and atomic publication path.
4. Canonical `LocationPublishedVersions` version 1 is either historical
   backfill (null ChangeRequestId) or a newly approved CreateLocation
   (non-null ChangeRequestId). Version 2+ always has a request.
5. A future **new** writer, not the existing High-only writer with relaxed
   checks, must verify the request operation, risk, live HR/team authority,
   independent Leader decision, Admin decision for High, RowVersion,
   reservation ownership and status, evidence, current ledger uniqueness
   and immutable audit **inside the same Serializable transaction**.
6. Create approval atomically marks reserved Location Approved/Active,
   publishes v1 with canonical RequestId, marks request Applied and
   reservation Published, and writes audit; failure rolls back all changes.
   Rejection leaves the reservation inactive and marks it Abandoned.
7. A separate query audit must prove Pending/Abandoned Locations are excluded
   from pickers, trip creation, mileage and every official business query;
   until proven, CreateLocation publication is **HOLD**.

## Disposable candidate only

Ordering: canonical 011 -> 012 -> 014 -> StageEvidenceAudit ->
LeaderReviewDetails -> CreateLowRiskContract.

The candidate adds a constrained reservation detail table, tightens the
canonical request operation/risk/before-image constraints and extends
published v1 origin to support an approved CreateLocation. An **unregistered,
isolated-only** application writer now performs Create/LowRisk atomic DML
inside a caller-owned Serializable transaction. It checks the independent
Leader decision and matching audit, canonical evidence events, source
RowVersion, reservation, risk-sensitive identity fields and version ledger.
It does **not** constitute a deployable orchestrator, API, UI or migration.
A future orchestrator must obtain **live** HR/role/team/appointment evidence
within the same transaction; a caller-supplied eligibility value alone is
not sufficient production authorization.

The isolated SQL rehearsal tests creation of the reservation without
publishing, LowRisk request acceptance, CreateLocation/Low denial, missing
before-image denial, additive constraint preservation and rerun denial.
Unit tests cover server-owned risk classification and source shape.

## Release blockers

- Isolated-only atomic writer candidate exists; no fully integrated
  CreateLocation/LowRisk submission, live-authorized orchestration or
  positive HTTP/DI/SQL E2E yet. The direct SQL test uses disposable fixtures.
- No complete positive and negative Leader review matrix for these operations.
- No complete Pending Location picker/search/trip exclusion proof.
- Excel, legacy direct mutation and background publication not canonically
  routed end to end.
- Exact UAT target, read-only preflight, backup/restore evidence, migration
  execution authorization, deployment and Business UAT remain HOLD.

**NO-GO** until each blocker is independently closed and Owner separately
authorizes UAT execution.
