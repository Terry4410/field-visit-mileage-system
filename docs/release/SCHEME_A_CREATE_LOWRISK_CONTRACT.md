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
A separate **isolated-only** orchestrator now reads live requester,
Leader, Admin, team scope, appointment/delegation and employment authority
inside the same Serializable transaction before calling the writer.
A guarded candidate endpoint `POST /api/v1/optionc-candidate/requests/{id}/publish-reviewed`
is available only when the isolated candidate setting is enabled on a
local disposable SQL Server. No UAT/Production activation is authorized.
The **LowRisk published-update** candidate now supports a real Visitor
HTTP submission, server-owned risk classification, pending private stage
without unnecessary geocoding, independent live Leader first-level approval,
Leader-bound duplicate evidence and Leader final atomic publication. The
isolated SQL/HTTP/DI test also covers premature evidence, unauthorized Admin
substitution, Leader revocation, Leader rejection and stage abandonment,
versioned publication and immutable Trip/Snapshot references.

**Earlier CreateLocation tests** seeded Requests and Stages directly in
disposable SQL. As of exact SHA `5ca1060423e9ef326b82b6cd6c1025250a0d578a`
(CI `38534680973 — SUCCESS`), a real isolated-only HTTP CreateLocation
submission now reserves the LocationId and persists Request, Submitted audit,
Reservation and private Stage atomically. It is exercised with independent
Leader review, Admin geocode/duplicate evidence and V1 publication. Full
published-query exclusion coverage, Create-specific revocation/concurrency
and Business User UI remain incomplete.
It does **not** constitute a deployable production migration or UI.

The isolated SQL rehearsal tests creation of the reservation without
publishing, LowRisk request acceptance, CreateLocation/Low denial, missing
before-image denial, additive constraint preservation and rerun denial.
Unit tests cover server-owned risk classification and source shape.

## Excel create fail-closed delta — 2026-10-14

The legacy `WorkbookImportService.ConfirmAsync` path previously inserted an
inactive Pending Location on Excel `Create` without writing a canonical
ChangeRequest, reservation or independent Leader/Admin review. This was
**not** a complete Scheme A CreateLocation transaction. The Recovery branch
now refuses location-create import batches before processing any row with
`LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW`. Location Preview also
marks actionable Create rows with this blocking error, while existing
NoChange rows remain unaffected. The existing published
update guard remains in place. Project imports and location previews are
not intentionally disabled. Do not re-enable Excel location creation until
it is transactionally routed through the canonical CreateLocation service
with server-owned identity, reservation, live authorization and evidence.
This is a safety closure, **not** functional Excel-import completion.

## Candidate CreateLocation read / Admin queue visibility — 2026-10-14

The isolated candidate `requests/mine` and `requests/{id}` read paths now allow
requester-owned High-risk `CreateLocation` requests (not Low-risk creates).
The independent Admin high-risk `requests/queue` now includes pending
`CreateLocation` alongside `UpdatePublishedLocation`. Existing live Admin,
organization/team, independence and requester authorization checks are retained.
These read/queue adjustments do not implement HTTP creation, reservation,
evidence, approval or public Location visibility. They do not enable UAT flags.

## CreateLocation evidence endpoint gate — 2026-10-14

The isolated candidate Stage evidence endpoint now recognizes High-risk
`CreateLocation` only when a matching **Pending** canonical
`LocationCreateReservations` row links the same Request, LocationId,
OrganizationId and TeamId. Its existing Serializable transaction, request/Stage
RowVersion, live independent Admin authorization and event audit remain in
force. It still does not construct the reservation, request or Stage through
HTTP. In particular, this is **not** the complete CreateLocation submission
workflow and is not evidence of pending-location exclusion in legacy queries.
No deployed flags are enabled.

## Candidate CreateLocation HTTP reservation delta — 2026-10-14

The isolated candidate POST `api/v1/optionc-candidate/locations` now
implements a server-owned LocationId (never supplied by the client),
live HR/role/team submission check, inactive Pending Location, canonical
CreateLocation/High Request, Submitted event, Pending reservation and
private PendingGeocode Stage inside one Serializable transaction. Matching
pending submissions are serialized with a transaction-owned application
lock; SQL insertion supports both IDENTITY application tables and the
disposable fixture's explicit key table without making either client-owned.
Rejected/rolled-back inserts leave no committed reservation or Stage.

A new disposable real HTTP/SQL test verifies Pending state, Source RowVersion
binding, audit, absence from the published-version ledger, duplicate denial
and cross-organization denial. It is not a test of every legacy Picker, trip,
mileage or report read path; public pending-location exclusion and complete
Visitor/Leader/Admin UI remain HOLD. No deployed flags enabled.

## Release blockers

- LowRisk published-update candidate now has an HTTP submission-to-publication
  path, but it is not a deployable Business User UI or UAT feature.
  CreateLocation still has SQL-seeded reservation and Stage only; no real
  CreateLocation HTTP submission or complete UI workflow yet.
- No complete positive and negative Leader review matrix for these operations.
- No complete Pending Location picker/search/trip exclusion proof.
- Excel, legacy direct mutation and background publication not canonically
  routed end to end.
- Exact UAT target, read-only preflight, backup/restore evidence, migration
  execution authorization, deployment and Business UAT remain HOLD.

**NO-GO** until each blocker is independently closed and Owner separately
authorizes UAT execution.


## Scheme A authoritative nondeploy checkpoint — 2026-10-14

- Verified Recovery implementation SHA: `5ca1060423e9ef326b82b6cd6c1025250a0d578a`.
- Exact-SHA CI: `38534680973 — SUCCESS`; backend **753 PASS**, additional
  SQL Admin E2E **1 PASS**, SQL 011/012/014 rehearsal **2 PASS**,
  frontend **24 test files PASS** and production build PASS.
- Real disposable SQL Server 2022 HTTP/DI CreateLocation E2E now passes:
  Visitor submission → server-owned identity and inactive Pending reservation →
  private Stage → independent live-appointed Leader review →
  application-owned geocode and duplicate evidence → independent Admin approval →
  atomic V1 publication. A second request is independently Leader-rejected
  and its Stage/reservation are Abandoned without published history.
- Negative evidence: self-review denied; cross-org submission denied;
  duplicate Pending create denied; premature publication denied; canonical
  Submitted/LeaderApproved/GeocodeEvidenceRecorded/
  DuplicateReviewEvidenceRecorded/Approved/Applied events and historical
  Trip/Snapshot reference preservation verified.
- The next candidate extends the same test to prove actual HTTP Picker
  exclusion before publication and inclusion after it; **do not count that
  extension PASS until its own Exact-SHA CI succeeds**.
- Excel Location Create remains fail-closed in Preview and Confirm;
  legacy published mutation/direct-note routes are guarded. Full Excel routing,
  broad Legacy/Background audit, all UI roles and all operational read-path
  proofs remain incomplete.
- No UAT/Production SQL writes, migration, deployment, promotion, merge,
  flag activation, UAT-target preflight or Business UAT was performed.
  **Scheme A HOLD / NO-GO; NOT READY for UAT execution authorization review.**
  This current checkpoint supersedes older 2026-10-14 engineering status
  paragraphs where the CreateLocation HTTP flow is described as absent.

