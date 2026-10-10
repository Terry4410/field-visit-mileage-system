# Package B3 — 1800_011 Schema IA (NONEXECUTABLE / REVIEW ONLY)

**Issue #24 | WORK branch only | No SQL statements to run | No migration authorization.**
This is NOT an `Up.sql` or `Verify.sql` and must not be executed. B3 feature flag stays OFF. Production and full Business UAT remain HOLD.

## Exact predecessor and preflight
- Target: `dbo.SchemaVersions` latest **exactly** `1.8.0-010`. Source migration files and `appsettings` do not prove the live UAT schema.
- IT read-only preflight: DB identity/version, `SchemaVersions`, baseline Locations/Teams/Users/Organizations, existing indexes/FKs/constraints and current permissions, partial artifacts and drift.
- Stop if predecessor is absent/not latest, if 011 already recorded, if ChangeRequests/ChangeRequestEvents objects partly exist, or if conflicting schema or missing FK targets. Never silently repair business data.
- Owner separate IA review, separate migration authorization and separate IT manager-grant attestation. Never elevate legacy TeamLeaderAssignments merely because a membership backfill created them.

## Proposed database shapes — field specifications, NOT executable DDL
### ChangeRequests
| Field | Type | Contract |
|---|---|---|
| ChangeRequestId | BIGINT IDENTITY | PK |
| RequestPublicId | UNIQUEIDENTIFIER | NOT NULL; unique |
| OrganizationId | INT | NOT NULL; FK Organizations |
| TeamId | INT | NULL; FK Teams; not authority |
| EntityKind / EntityId | NVARCHAR(40) / NVARCHAR(80) | NOT NULL; entity kind/status constrained; dynamic entity reference requires service revalidation |
| OperationCode / RiskCode | NVARCHAR(80) / NVARCHAR(20) | NOT NULL; enums/known codes |
| ExpectedEntityRowVersion | VARBINARY(8) | Required for Location changes |
| BeforeJson / ProposedJson / EvidenceJson | NVARCHAR(MAX) | Before/Evidence optional; Proposed required valid JSON; whitelist and redact sensitive data |
| RequestedByUserId | INT | NOT NULL; FK Users |
| SubmittedAt | DATETIME2(3) | UTC; NOT NULL |
| Status | NVARCHAR(30) | Pending, Rejected, Returned, Applied, Cancelled; never confuse approval decision with applied transaction |
| ReviewedByUserId / ReviewedAt / ReviewReason | INT / DATETIME2(3) / NVARCHAR(1000) | nullable, FK Users; reason required for rejection |
| AppliedAt | DATETIME2(3) | NULL until successful atomic application |
| RowVersion | ROWVERSION | NOT NULL, EF concurrency token |

Constraints/indexes (proposed exact names; NOT deployed): unique RequestPublicId `UX_B3_ChangeRequests_RequestPublicId`; unique filtered (OrganizationId, EntityKind, EntityId) `UX_B3_ChangeRequests_Org_Entity_Pending` **WHERE Status='Pending'**; queue lookup (OrganizationId, Status, SubmittedAt); Mine (RequestedByUserId, SubmittedAt); check status/risk/entity strings, JSON, and review status consistency. No cascading history delete.

### ChangeRequestEvents
| Field | Type | Contract |
|---|---|---|
| ChangeRequestEventId | BIGINT IDENTITY | PK |
| ChangeRequestId | BIGINT | NOT NULL, FK ChangeRequests, no cascade |
| EventType | NVARCHAR(40) | NOT NULL; event-code check |
| ActorUserId | INT | NULL; FK Users; no cascade |
| OccurredAt | DATETIME2(3) | UTC NOT NULL |
| CorrelationId | UNIQUEIDENTIFIER | NOT NULL |
| DecisionKey | UNIQUEIDENTIFIER | NULL; unique filtered nonnull for replay safety |
| DetailsJson | NVARCHAR(MAX) | nullable, auditable JSON, no secrets |

Indexes: (ChangeRequestId, OccurredAt, ChangeRequestEventId); CorrelationId; `UX_B3_ChangeRequestEvents_DecisionKey` unique filtered DecisionKey NOT NULL. Events append-only. EF mapping must match actual indexed filter and SQL column lengths exactly.

## EF model / proposed 011 DDL alignment (REVIEW ONLY)
- This WORK candidate now defines explicit max lengths for EntityKind 40, EntityId 80, OperationCode 80, RiskCode 20, Status 30, ReviewReason 1000, EventType 40, and 8-byte ExpectedEntityRowVersion, together with DATETIME2(3) precision for SubmittedAt / ReviewedAt / AppliedAt / OccurredAt. JSON payloads remain NVARCHAR(MAX), with ProposedJson required.
- Proposed foreign keys: ChangeRequests.OrganizationId → Organizations, TeamId → Teams, RequestedByUserId / ReviewedByUserId → Users, ChangeRequestEvents.ChangeRequestId → ChangeRequests, and ActorUserId → Users; every delete behavior is NO ACTION (never cascade-delete audit history).
- Proposed supporting index names: `IX_B3_ChangeRequests_Org_Status_SubmittedAt`, `IX_B3_ChangeRequests_Requester_SubmittedAt`, `IX_B3_ChangeRequestEvents_Request_OccurredAt`, and `IX_B3_ChangeRequestEvents_CorrelationId`.
- EF metadata tests validate this **candidate model only** without executing SQL Server commands. The read-only runtime catalog probe now checks the three unique indexes are enabled/non-hypothetical, ROWVERSION, and all six required trusted, enabled, NO ACTION foreign keys. It now additionally inspects the required 28 column types, UTF-16 storage byte widths, nullability and datetime2 scales; it **still does not verify CHECK constraint definitions, SQL Server-executed catalog-query correctness or real SQL Server transaction/concurrency behavior**; independently authorized integration tests and IT-approved 011 DDL remain HOLD gates.
- Do not infer that changing EF mappings creates a table or authorizes a migration. No UAT database object has been created or altered by this change.

## Candidate 011 CHECK constraints (DRAFT — not executable)

The candidate EF Model declares eight named SQL Server CHECK constraints for defense in depth. **These are not installed.** Names and draft expressions require IT review of SQL Server CHECK semantics and future workflow statuses before an executable migration can be authorized.

| Constraint | Intended fail-closed check |
| --- | --- |
| `CK_B3_ChangeRequests_KnownCodes` | Only Location / UpdatePublishedLocation / High |
| `CK_B3_ChangeRequests_KnownStatus` | Status from Pending, Rejected, Returned, Applied, Cancelled |
| `CK_B3_ChangeRequests_ExpectedLocationVersion` | Source Location ROWVERSION evidence is non-null and exactly 8 bytes |
| `CK_B3_ChangeRequests_ProposedJson` | ProposedJson must be valid JSON |
| `CK_B3_ChangeRequests_ReviewState` | Pending has no review/applied fields; Rejected has independent decision identity, timestamp, explicitly NON-NULL nonblank reason and no AppliedAt (avoid SQL CHECK UNKNOWN passing on NULL) |
| `CK_B3_ChangeRequestEvents_EventType` | Submitted or Rejected audit event types only until future apply is authorized |
| `CK_B3_ChangeRequestEvents_DetailsJson` | Optional event DetailsJson must be valid JSON |
| `CK_B3_ChangeRequestEvents_DecisionState` | Submitted event requires no DecisionKey; Rejected requires a non-null DecisionKey and real actor |

Runtime read-only catalog gate checks all eight named constraints exist, are enabled/trusted, and mention key expression tokens. The catalog gate also requires no enabled DML triggers on either B3 table: B3 stage/rejection events must not have implicit trigger-based publication, cleanup, notifications or hidden side effects. Any trigger proposal requires independent IT/Owner impact analysis; do not silently disable one in UAT. **Token checks cannot prove semantic equivalence to the approved DDL**. Actual SQL Server compile, normalized constraint definitions, effective behavior, 011 migration and UAT concurrency remain HOLD until separately approved. Future applied-event types or status transitions must undergo a new IA rather than silently changing these gates.

## Readiness and conflict behavior (NONEXECUTABLE contract)
- B3 must remain disabled until the live SQL catalog proves both tables, 8-byte ROWVERSION concurrency token, enabled/non-hypothetical unique RequestPublicId, the exact three-key Pending filtered unique, and DecisionKey filtered unique indexes, plus six trusted, enabled, NO ACTION foreign keys. A version row without these safety structures is NOT ready.
- Query the latest applied SchemaVersions entry ordered by AppliedAt DESC, VersionNumber DESC; it must equal 1.8.0-011, not merely contain an old 011 row.
- Draft index names are required by the current candidate EF mappings and read-only catalog gate; IT must reconcile exact names, key order, SQL filter definitions and constraints during a separately authorized IA review.
- The read-only catalog probe also requires 28 exact candidate SQL column definitions (types, NVARCHAR length measured in bytes, required/nullable and DATETIME2(3)). Catalog SQL is statically tested but has NOT been executed against UAT or SQL Server; Type/Width/Nullable drift requires later independently authorized IT runtime verification.
- DB duplicate key (2601/2627) is translated into a conflict only when the named B3 Pending or DecisionKey index is identified. Any unknown constraint/error remains fail-closed, with no false success or automatic retry. B3 HTTP error mapping uses 409 for these recognized conflicts.
- A precheck for an existing DecisionKey runs inside the Reject transaction, but concurrent correctness still requires the physical unique index and approved SQL Server integration testing; local InMemory unit tests do not establish this.
- Nothing in these checks creates schema objects, approves, applies, promotes, or publishes data.
- SQL Server translation for requester-scoped and Admin Pending B3 queue queries is validated offline through EF `ToQueryString()` with a dummy, unopened SQL Server connection; this does **not** prove real SQL Server execution or concurrency.
- B3 queue reads use server-side whitelist predicates for EntityKind=Location / OperationCode=UpdatePublishedLocation / RiskCode=High and a non-null TeamId. Mine is requester + organization + supported Pending/Rejected states; Admin Pending is organization-scoped Pending only. Unsupported or future operation types cannot be surfaced by the currently authorized B3 queue. These filters do not elevate Leader or grant approval.

## Atomic application and security gates — NOT IMPLEMENTED
- B3 submission requires valid effective HR/role/org/team/current ownership, active approved Customer record, and expected 8-byte entity RowVersion. No-op or malformed proposals rejected; unique pending index protects concurrent requests.
- Reject requires independent current Admin, requester != reviewer, live Pending request and matching request RowVersion; decision event written in one transaction. DecisionKey's DB unique index still required for concurrent replay.
- **ApproveAsync is deliberately DENY ALL**, not a working approval endpoint. Future apply must recheck requester and approver at execution, lock request+location, check expected Location RowVersion, duplicate/site/history constraints, derive effective owner scope, apply only whitelisted fields, write event/history and transition to Applied atomically. No geocoding success auto-publish.
- Notification integration must use NotificationEnvironmentPolicies, UAT allowlist and MailOutbox after atomic commit; never external email by default.
- B4 missing: API/SQL integration race, duplicate-pending conflict, DecisionKey replay, stale entity/request version, IDOR, cross-org, peer ownership, revoked/Leave/Terminated, disabled team, historical invariants, queued job bypass, official site/HR/Project/bulk.

## Proposed future sequence — REQUIRES SEPARATE AUTHORIZATION
1. IT read-only exact-predecessor/schema inventory and manager-grant provenance attestation.
2. Owner approves final IA/SQL/FK/index/rollback design and separate migration execution window.
3. IT creates restore/backup evidence, and only then drafts executable Up/Verify/Down procedures with applock, XACT_ABORT, transactional catalog checks, object absence checks; register 011 last.
4. Approved UAT migration only, verify indexes/FK/trust and latest SchemaVersion, no auto-seeding.
5. Backend/frontend controlled UAT deployment feature OFF, B4 negative and runtime transaction verification, Owner Smoke Test.
6. Separate flag enablement and full Business UAT GO decision after complete Package A+B; no Production without release authorization.
7. Recovery: keep flags OFF; if any B3 historical rows exist, do NOT drop tables or erase audit. Use IT-approved forward fix or point-in-time restore after reconciling history.

**STATUS:** schema design only; 011 not executed or verified; Issue #24 OPEN; Production HARD HOLD; full Business UAT HOLD.
