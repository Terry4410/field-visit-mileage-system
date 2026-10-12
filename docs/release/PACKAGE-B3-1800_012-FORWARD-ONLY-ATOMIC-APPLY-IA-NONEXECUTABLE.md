# Package B3 — Forward-only 1800_012 approval/apply schema impact analysis
## NONEXECUTABLE DESIGN — NO SQL MIGRATION AUTHORIZATION

**Issue #24 | WORK candidate | Prepared under Owner's conditional UAT
preparation authority. This file contains NO executable DDL.**

### 1. Why a separate revision is mandatory

The 1800_011 candidate explicitly constrains `ChangeRequests.Status` to
`Pending` / `Rejected`, and `ChangeRequestEvents.EventType` to
`Submitted` / `Rejected`. Its review-state and event-decision CHECKs are
also rejection-only. Updating the C# code to write `Applied` or
`Approved` without a **separate** reviewed, forward-only schema revision
would violate trusted SQL constraints. Do **not** rewrite or quietly replace
1800_011, disable constraints, bypass readiness or run ad hoc ALTER commands.

**Design choice requiring independent signoff:** the approval and publication
are one indivisible transaction. The request has terminal `Applied`
(no persisted intermediate `Approved` status); the append-only event log
records `Approved` then `Applied` in the same commit. No asynchronous
post-approval publication, partial commit, or externally visible Approved-only
request state is permitted.

### 2. Exact schema delta for a proposed 1800_012 (specification only)

| Object | Proposed change | Mandatory invariant |
| --- | --- | --- |
| `SchemaVersions` | Append version `1.8.0-012` after exact latest 011 | Never alter or erase earlier version history |
| `CK_B3_ChangeRequests_KnownStatus` | Replace with trusted enabled CHECK allowing Pending, Rejected, Applied | No Returned/Cancelled/Approved request states |
| `CK_B3_ChangeRequests_ReviewState` | Replace with Pending, Rejected, Applied mutually exclusive shapes | Applied requires independent reviewer, nonblank reason, ReviewedAt and AppliedAt; Rejected has no AppliedAt |
| `CK_B3_ChangeRequestEvents_EventType` | Replace with Submitted, Rejected, Approved, Applied | Unknown event types denied |
| `CK_B3_ChangeRequestEvents_DecisionState` | Approved has nonnull actor and DecisionKey; Applied has actor and **null DecisionKey** | Only one unique decision key per decision; Approved/Applied pair correlated by request and correlation ID |
| `UX_B3_ChangeRequestEvents_DecisionKey` | Retain unique filtered index (non-null DecisionKey) | One-time decision; replay cannot create another decision |
| `UX_B3_ChangeRequests_Org_Entity_Pending` | Retain exact filtered unique Pending index | At most one Pending per organization/entity |
| `ChangeRequests.RowVersion` and source `Locations.RowVersion` | Retain physical ROWVERSION | Compare both freshly locked versions inside transaction |
| `ChangeRequestEvents` history | Append-only, NO ACTION FKs, no DML triggers | Both decision and apply events commit with location change or neither commits |

The precise CHECK definitions, constraint replacement order, SQL Server
three-valued NULL semantics, index fingerprints, data compatibility and
version marker must be independently reviewed before creating any Up/Verify
script. `CHECK UNKNOWN` must not accidentally admit NULL reviewer/reason
or malformed audit records.

### 3. Atomic transaction contract

1. Require **new** schema readiness: latest exactly `1.8.0-012`, all
   011 base catalog fingerprints plus reviewed 012 CHECK fingerprints.
   The current 011-only readiness MUST fail closed on 012 until a separate
   code change and independent security review are approved.
2. Begin serializable transaction, lock request by public ID/org with
   `UPDLOCK,HOLDLOCK`; independently resolve live HR status and effective
   Admin authority from authoritative DB (not claims or UI).
3. Lock source Location by ID/org with `UPDLOCK,HOLDLOCK`; recheck current
   requester team membership, source ownership, effective org/team, active
   approved Customer classification, and geocoding/duplicate/history rules.
4. Re-read Pending request and both SQL RowVersions, compare canonical
   BeforeJson with locked current source, validate exactly seven whitelisted
   proposed fields, compare DecisionKey and reason to locked decision input,
   ensure requester != reviewer, and reject DecisionKey replay.
5. Execute a single guarded source UPDATE with the original RowVersion and
   scope predicates; **require exactly one affected row**. No unconditional
   update and no publication by geocoding callback.
6. Update the Pending request to Applied with reviewed/applied timestamps,
   reason and reviewer, guarded by its original RowVersion; **require exactly
   one affected row**.
7. Append `Approved` and `Applied` immutable events in the **same**
   transaction with one correlation ID; the Approved event alone carries
   DecisionKey, while Applied links the same request/correlation.
8. Commit only after every UPDATE and audit INSERT succeeds. Any failure
   rolls back location, request and events together. No SQL error may be
   converted into success. Translate only known named duplicate indexes to
   409; unexpected SQL errors fail closed and trigger incident review.
9. After commit, read-only verification confirms exact expected location,
   terminal request, two immutable events and no duplicate DecisionKey.
   Verify historical trip/snapshot records remain unchanged.

### 4. Concurrency and replay test obligations

- Same Pending request, two reviewers: exactly one complete Applied
  transition, one correlated Approved/Applied pair, loser conflict.
- Same DecisionKey on different requests: second decision denied; no
  location/request/audit mutation.
- Stale source or request RowVersion: no updates, no events.
- Mid-transaction failure at source UPDATE, request UPDATE, Approved event
  insert, Applied event insert or commit: rollback all side effects.
- Cross-org and peer-team request IDs, self-review, revoked Admin,
  terminated/leave/expired requester or reviewer, unattested Leader:
  authorization denied with no side effects.
- Pending geocoding callback or duplicate-location candidate: cannot
  publish, auto-approve or bypass independent review.
- Disabled/untrusted CHECK, FK, unique index, altered filter, enabled trigger,
  unexpected schema version, partial 012 installation: readiness denied.
- Repeat requests after successful Applied: retain immutable history and
  require fresh source RowVersion for any new proposal.

### 5. UAT and production gates

| Gate | Current status |
| --- | --- |
| Owner conditional preparation authorization | GRANTED — preparation only |
| 1800_011 reviewed executable script | NOT AUTHORIZED |
| Actual UAT latest 010/011 catalog evidence | NOT VERIFIED |
| Proposed 1800_012 executable DDL | NOT WRITTEN / NOT AUTHORIZED |
| 012 SQL Server isolated upgrade and rollback testing | NOT EXECUTED |
| Atomic apply executor | NOT IMPLEMENTED / DENY ALL |
| Independent HR/IT VAL-B1-002 provenance | BLOCKED |
| Full 22-case UAT acceptance | 0/22 |
| B3 activation | OFF |
| Business UAT | HOLD |
| Production | HARD HOLD |

This IA is a dependency and test specification only. No migration is
performed by creating this document. The final consolidated Owner execution
approval packet must include exact immutable SQL script checksums, live UAT
database identity, schema predecessor evidence, IT recovery ownership,
independent security review, deployment artifacts and the formal B4 matrix.


### 6. Transaction failure-injection and rollback evidence contract (WORK only)

This is a NONEXECUTABLE acceptance contract. No UAT write or deployment
is authorized.

Capture baseline snapshots of the target Location (seven fields and
RowVersion), Pending request (review fields and RowVersion), request events,
and historical VisitTripStop/VisitTripSnapshotStop references in a disposable
SQL Server 2022 fixture. After each forced rollback, compare every captured
field and event identity with the baseline. SQL identity gaps are not
rollback evidence.

| Injected failure | Required observation |
| --- | --- |
| Before source UPDATE | No source, request or event mutation |
| Source UPDATE returns zero or multiple rows | Conflict; rollback all changes |
| After source UPDATE, before request UPDATE | Source rolled back; request remains Pending |
| Request UPDATE returns zero or multiple rows | Source and request rolled back; no events |
| After request UPDATE, before Approved INSERT | Both UPDATEs rolled back |
| Approved INSERT FK, CHECK or uniqueness failure | No source, request or audit mutation |
| After Approved INSERT, before Applied INSERT | Both UPDATEs and Approved event rolled back |
| Applied INSERT FK or CHECK failure | Both UPDATEs and both events rolled back |
| Commit exception or dropped connection | Outcome UNKNOWN; reconcile using immutable correlation and DecisionKey before retry, never return success blindly |
| Two concurrent reviewers | At most one complete Applied transition; losing reviewer conflicts |
| Duplicate DecisionKey across requests | Exactly one decision accepted; loser has no mutations |
| Live permission or schema drift after preview | Locked revalidation denies without writes |

Executor invariants: use one SQL connection and one Serializable transaction
for both guarded UPDATEs and both audit INSERTs. Lock request first, then
location; bind all untrusted values as SQL parameters; demand exactly one
affected row for each UPDATE. Do not convert concurrency, deadlock, timeout
or ambiguous commit errors into success. Never blindly retry a transaction
whose commit outcome is unknown. Approved and Applied share one nonempty
correlation ID and ordered UTC timestamps; only Approved carries DecisionKey.

Each injection requires a fixture/test ID, SQL Server version, exact WORK
SHA, test-run URL and captured before/after assertions. Pure C# preflight,
CI success and documentation do not count as isolated SQL runtime PASS or
formal B4 UAT. Keep B3 OFF, ApproveAsync DENY ALL, 012 nonexecutable and
Package B NO-GO pending independent approval.


### 7. Disposable hypothetical atomic-write rehearsal (NOT 1800_012)

A standalone test-only `B3ProbeSources` / `B3ProbeRequests` /
`B3ProbeEvents` schema is created **only** inside a randomly named
localhost SQL Server 2022 test database. It is not named `ChangeRequests`,
`ChangeRequestEvents`, or `Locations`, and is **not** a migration,
production executor, 012 CHECK definition or deployable schema change.

Five isolated cases rehearse: successful two-UPDATE/two-event single commit,
rollback after each of four write boundaries (including after Applied INSERT),
duplicate DecisionKey rollback, stale source/request RowVersion rejection,
and competing reviewers with at most one successful transaction. The helper
exists exclusively in the SQL **test project** and is unreachable from
`ApproveAsync` or any API. Every test remains linked to its exact-name
CI evidence manifest; test additions do not count as business acceptance.

This validates SQL transactional mechanics only, **not** the real Location
shape, complete seven-field persistence, effective HR/role/team permissions,
geocoding, duplicate/location-history requirements, reviewed 012 DDL,
013+ adjustments, ambiguous commit reconciliation, or end-to-end UAT.
Accordingly retain 012 **NONEXECUTABLE DRAFT**, B3 OFF, Approve/Apply
DENY ALL, B4 formal 0/22, Business UAT HOLD and Production HARD HOLD.


### 8. Seven-field hypothetical Location writes and fail-closed source scope (NOT 012)

Disposable `B3ProbeSources` now mirrors the exact seven high-risk Location
field names: `LocationName`, `City`, `District`, `Address`,
`PlusCode`, `TaxId`, and `MasterNote`. Four additional SQL 2022 fixture
cases require all seven parameterized fields to persist, SQL NULL optional
fields to remain NULL, rollback of all seven fields plus both RowVersions at
each postwrite fault boundary, and fail-closed refusal of foreign organization,
peer team, forged creator, inactive, unapproved or non-Customer sources.
Source organization/team/creator/classification/approval/active columns are
read-only in this rehearsal.

**Important limitation:** the `B3Probe*` tables are not production
`Locations` or reviewed B3 012 tables. These are mechanics/scope-rehearsal
cases only; no live HR/role check, duplicate/geocoding veto, historical
snapshot check, 012 readiness, or actual `ApproveAsync` publication has been
implemented. Never promote these probes into the application assembly,
schema migration or formal B4 acceptance. All HOLD gates remain unchanged.


### 9. 012-like CHECK truth-table rehearsal — NONEXECUTABLE / WORK ONLY

The test-only B3Probe requests and events now model the proposed 012 **row
states** without modifying the current 011 EF model, catalog readiness or
any application migration. This is a contract *candidate*, not signed-off
DDL. Actual reviewed SQL Server constraint definitions and full catalog
fingerprints for 012 remain a separate independent design gate.

| Request state | Reviewer/reason/time | AppliedAt | Allowed probe row |
| --- | --- | --- | --- |
| Pending | all absent | absent | YES |
| Pending | any review field filled | any | NO |
| Rejected | independent reviewer, nonempty reason, ReviewedAt | absent | YES |
| Rejected | missing reviewer/reason/time or self-review | any | NO |
| Rejected | valid rejection review | present | NO |
| Applied | independent reviewer, nonempty reason, ReviewedAt | present and not earlier than review | YES |
| Applied | missing reviewer/reason/time, self-review, blank reason or reversed time | any | NO |
| Returned / Approved / Cancelled or unknown | any | any | NO |

Event shape (actor must exist; correlation must be nonempty): Submitted and
Applied carry no DecisionKey; Rejected and Approved require distinct nonempty
DecisionKeys. The filtered unique DecisionKey index remains the authoritative
concurrent replay barrier. Additional SQL tests intentionally try null actor,
null/empty decision keys, empty correlation IDs and unknown event types to
catch three-valued NULL loopholes.

The disposable fixture also proves the *trusted enabled CHECK* catalog count
drops when the review constraint is disabled and recovers only after a
WITH CHECK restoration. This does **not** verify the actual 012 catalog,
exact normalized constraint fingerprints, actual UAT SchemaVersions,
real Locations DDL or live reviewer identity.

**Security boundary:** independent row CHECKs cannot enforce that an
Approved event and Applied event share the same transaction/correlation,
nor that a Rejected request has a corresponding audit event. The atomic
executor must enforce exact row counts, event ordering, immutable IDs,
effective HR/roles/team permissions, geocoding/duplicate/history checks,
and a single commit. B3 remains OFF; ApproveAsync DENY ALL.


### 10. Enabled-but-untrusted CHECK is a mandatory readiness denial

Independent test-only SQL Server 2022 drift verification now distinguishes
an enabled but **untrusted** request review constraint from a trusted one.
It performs the same negative catalog check for the audit decision constraint,
using `WITH NOCHECK CHECK` to deliberately re-enable without verification,
then `WITH CHECK CHECK` to revalidate within the disposable probe database.
A future 012 catalog gate MUST require BOTH
`is_disabled=0` AND `is_not_trusted=0` for every required CHECK/FK;
enabled alone does not mean safe. Neither probe check authorizes executable
012 DDL, changes the existing 011 readiness, or enables approval.

### 11. Real-named disposable 012 transaction rehearsal (NOT executable UAT migration)

This WORK-only suite now rehearses a **forward-only logical 011→012 contract**
using the *actual table names* `ChangeRequests` and `ChangeRequestEvents`
inside the randomly named localhost SQL Server fixture. Four candidate CHECKs
are dropped/recreated, a simulated `1.8.0-012` version marker is appended,
and all operations execute inside a single disposable Serializable transaction
that is **always rolled back**, including on failed assertions.

The named tests verify: 012 partial catalog shape and exact latest marker,
011 readiness denial against the interim 012 shape, enabled-but-untrusted
request CHECK denial and restoration, absence of version-marker denial,
disabled DecisionKey index denial, invalid blank review/self-review/NULL
decision event denial, valid Applied + correlated two-event insertion, replayed
DecisionKey rejection, and full rollback to intact 011 catalog and rows.

**Limits:** `PartialShapeSql` is intentionally *not* the full 012 release
readiness fingerprint. It verifies selected names/trust/uniqueness, NOT exact
constraint-definition hashes, filtered-key ordering, all 011 physical column
specifications, physical `Locations` write integration, HR/IT attestation,
or unknown commit outcome. The fixture does not contain the full live location
schema. B3 feature flag remains OFF; Approve/Apply remains DENY ALL.
No 012 DDL file is authorized or executed outside disposable test SQL.

### 12. Exact-metadata snapshot drift detector — WORK/disposable-only

Four isolated SQL Server 2022 cases exercise a *snapshot-equality*
candidate `B312DisposableCatalogFingerprint`, captured only **after**
the reviewed in-test 011→012 rehearsal has built its disposable catalog.
SHA-256 covers physical candidate columns (type, width, nullability, scale,
identity/computed), all eight **complete** CHECK definitions and trust bits,
six FK shapes/trust/delete-action bits, three unique-index ordered keys and
full filter strings, trigger definitions, and the 011→012 SchemaVersions
history. It also requires exact isolated fixture object counts and the
existing partial-shape check. The catalog is read through the SAME
serializable test-only SQL transaction: no DDL repair or mutation.

These cases deliberately show that the old partial check would accept
**a trusted CHECK with extra Cancelled state**, **a more restrictive
DecisionKey index filter**, **a duplicate 011 predecessor marker**,
or **an enabled side-effect trigger**; the exact snapshot detector
denies them. All mutations and the entire 012 schema rehearsal roll back.

**Critical limitation:** this is a short-lived fingerprint captured
against known fixture DDL; it is *not* a pinned IT-reviewed schema hash,
a production readiness function, or a substitute for proof of the actual
full `Locations` schema/rowversion and real HR/Team provenance.
Before any actual 012 readiness is designed for use by an executor, IT
must independently pin the approved DDL checksum and expected canonical
catalog fingerprint, verify complete 011 predecessor/revision ordering
in the named target environment, block untrusted/disabled CHECK/FK or
index/trigger drift, and sign the rollback/recovery plan.
`ApproveAsync` remains DENY ALL; the flag remains OFF.

### 13. Real-named `dbo.Locations` + Request/Event seven-field rehearsal (WORK only)

The next four disposable SQL Server tests use **all three application table
names**: `dbo.Locations`, `dbo.ChangeRequests` and
`dbo.ChangeRequestEvents`. The `Locations` table is created as a **reduced
test stub** with the seven candidate writable fields, organization/team/owner,
source status and physical ROWVERSION; it is **NOT** the full EF/011 live table.
The 011→012 DDL is still an in-transaction rehearsal, always rolled back.

The test-project-only `B312RealNamedLocationApplyProbe` locks Request first
then Location with `UPDLOCK,HOLDLOCK`, runs existing .NET immutable-plan and
audit-pair preflight helpers, compares both physical RowVersions, uses
parameterized seven-field source UPDATE and guarded Pending→Applied Request
UPDATE, requires exactly one affected row each, and inserts correlated
Approved/Applied events in the same serializable transaction. Tests cover
all seven field values, unchanged owner/scope columns, four injected
post-write failures rolled back to a SQL SAVEPOINT, reused DecisionKey,
simulated denied authorization flags and stale source data. The final outer
transaction rolls back ALL inserts and the reduced Locations stub.

**Not proven:** full production Locations physical parity; current, live
HR/Admin and team effective-date queries (test-only booleans are NOT trusted
authority); durable COMMIT/unknown-outcome reconciliation; geocoding/duplicate
merge/history invariants; executable 012 migration or UAT activation.
No approval API calls or application executor are added. The next independent
security review must address these before any feature can be enabled.

### 14. Lost COMMIT acknowledgment read-only reconciliation — disposable only

Seven further isolated SQL cases distinguish an externally observable
committed decision from an exactly intact rollback and a conflicting,
partial or unverifiable result. A TEST-PROJECT-ONLY read-only oracle
(`B3UnknownCommitReconciliationProbe`) reconnects to the disposable SQL
Server using a **new connection** and one Serializable read transaction.
It validates the source's seven expected values and original/new versions,
organization/team/owner/active/approved Customer scope, request terminal
state, reviewer, times and reason, matching correlation for precisely one
Approved and one Applied audit event, and a unique DecisionKey.
The oracle never updates any row, retries any request or returns API success.

Passing cases: a real committed B3Probe transaction => complete committed
**observation**; an injected post-Approved failure with a confirmed rollback
=> complete no-commit **observation**. In contrast, Approved-only history,
a changed source after commit, reused DecisionKey on another request and
a missing request are all INDETERMINATE / incident investigation.
A separate same-transaction EF metadata check explicitly proves the reduced
`dbo.Locations` stub is *not* production-parity: the official EF model
contains GeocodingStatus, duplicate/history/geocoding IDs and geographic
properties that the stub does not model.

**Restrictions:** this is a B3Probe* read-only post-commit oracle, NOT
the true `dbo.Locations` executor; the actual B3 ApproveAsync remains
DENY ALL. Even a completed observation is not authority to return HTTP
success without a separate, reviewed end-to-end protocol and verified
database state. A "no commit" observation does not authorize automatic
resubmission; revalidate every live identity/source/version and require
an independent, separately authorized retry decision. If database access
is unavailable, the oracle throws; a connection error is NEVER
converted to "not committed." Rollback-only 012 tests are distinct from
real committed B3Probe transactions. Actual unknown COMMIT transport
failure injection and UAT remain NOT EXECUTED.

### 15. Expanded EF Location columns and post-transaction acknowledgment error — WORK SQL only

A separate rollback-only `dbo.Locations` test candidate now covers **every
currently EF-mapped Location column**, including the geocoding, duplicate,
location ownership, noneditable metadata, historical timeline and seven
editable fields. The SQL Server system catalog is compared to the actual
.NET EF SQL Server relational type mapping for names/count, nullability,
Unicode width, decimal and date precision, identity and physical rowversion.
Two negative tests intentionally narrow a Unicode column or relax the
approval field's nullability and remove the duplicate-history column; the
comparison fails closed. An additional seven-field atomic update test checks
that all 21 other original values remain unchanged.

The expanded test-only DDL uses explicit defaults for mandatory metadata
fields solely to allow isolated insertion. It does NOT recreate production
FKeys, indexes, geocoding attempts, triggers, historical schema versions or
the full live target. This is **EF column mapping parity for a disposable
candidate**, NOT UAT physical schema parity or a complete 011/012 fingerprint.

On B3Probe* mock tables, two additional SQL Server tests execute two
guarded UPDATEs and a correlated Approved/Applied audit pair in one server
transaction; the SQL batch either COMMITs or ROLLBACKs and then raises the
same simulated error 51091 after the decision. A separate connection
reconciles durable data rather than trusting that error. This models the
application's ambiguous acknowledgment after server-side transaction
completion; **it does not physically drop the TCP connection, simulate a
SQL crash or exercise the real Locations/ChangeRequests executor**.
No automatic retry, approval API availability or release authorization
follows from this isolated evidence. B3 remains OFF and DENY ALL.

### 16. Five bounded Locations relationship + historical FK checks (WORK-only)

The new disposable-only `B312LocationRelationContract` creates five
explicit No-Action, enabled and trusted location relationships:
`Locations.DuplicateOfLocationId -> Locations.LocationId`,
`Locations.SelectedGeocodingAttemptId -> GeocodingAttempts.GeocodingAttemptId`,
`VisitTripStops.LocationId -> Locations.LocationId`,
`TeamLocationNotes.LocationId -> Locations.LocationId`, and
`TeamLocationNoteHistory.LocationId -> Locations.LocationId`. A filtered
`IX_Locations_SelectedGeocodingAttempt` index must contain exactly
`SelectedGeocodingAttemptId` with no narrower predicate.

Five real SQL Server 2022 fixture tests require exact FK
source/target/column shape, trust and enabled state, No-Action
delete/update action, and the expected filtered-index predicate/key.
Negative tests fail closed if an FK is enabled-but-UNTRUSTED,
if a same-named FK points at `TeamId` rather than `DuplicateOfLocationId`,
or if a same-named geocoding index adds an `IsActive=1` predicate.
An atomic seven-field location update is also rehearsed with actual
referencing trip-stop and team note/history test records; the retained
historical values are verified and attempted deletion must raise SQL
error 547. **All** schema/data changes, including failed DML, are
rolled back with the 011→012 rehearsal transaction.

**Deliberate limitation:** the test-specific `FK_B312_*` names and
minimal referencing tables are **not** production migration DDL. These
five relationships cover only a reviewed representative subset of
current EF mappings and are not evidence of full existing UAT foreign
keys, FK names, triggers, all historic references, or migration parity.
In particular, `VisitTripSnapshotStops` is not verified by one of
these direct Location foreign keys; Snapshot immutable-content rules
require separate application/database evidence. Formal IT catalog
and UAT approval remain blocked. The feature stays OFF and
`ApproveAsync` DENY ALL.
