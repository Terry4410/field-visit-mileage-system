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

Constraints/indexes: unique RequestPublicId; unique filtered (OrganizationId, EntityKind, EntityId) **WHERE Status='Pending'**; queue lookup (OrganizationId, Status, SubmittedAt); Mine (RequestedByUserId, SubmittedAt); check status/risk/entity strings, JSON, and review status consistency. No cascading history delete.

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

Indexes: (ChangeRequestId, OccurredAt, ChangeRequestEventId); CorrelationId; unique filtered DecisionKey NOT NULL. Events append-only. EF mapping must match actual indexed filter and SQL column lengths exactly.

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
