# Full Scheme A / Option C UAT Readiness Fast Track

Status: **ENGINEERING CANDIDATE / UAT HOLD / NO-GO**  
Evidence cut: 2026-10-15 UTC  
This document is a nondeploy review record. It authorizes no migration,
deployment, restore, feature activation, promotion, or Business UAT.

## 1. Authoritative current state

| Evidence | Verified value | Result |
|---|---|---|
| Protected source | `post-uat/v1.8.0` @ `ce70eddc290e85375c64311b6d96eedc09942dba` | PASS |
| Recovery source inspected | `work/v180-optionc-final-gates-recovery-r1` @ `4191e29595d019b2b492ac3659bac86049970d5f` | PASS |
| Protected UAT preflight | Run `38707441735`, source `ce70eddc…`, completed `2026-10-15T08:42:04.8304305Z` | PASS (read-only evidence only) |
| UAT database | `sql-fieldvisit-jpe-uat.database.windows.net / db-fieldvisit-uat` | PASS |
| Latest UAT marker | `1.8.0-010` | PASS / baseline only |
| Canonical 011/012 tables | absent | Expected at 010; Full Scheme A HOLD |
| Stage / PublishedVersions | absent | Expected at 010; Full Scheme A HOLD |
| MergeDetails / evidence audit extension | absent | Expected at 010; Full Scheme A HOLD |
| Migration 013 | no marker observed | PASS; remains reserved |
| Catalog fingerprint | `665C66D7BA7DE5F75AD855CDDDA10175E8AB31114B6572EC010813799BFCD975` | PASS |
| Read-only principal | all observed write/DDL permission fields `0` | PASS for observed fields |
| Existing integrity | orphan ProjectLocations `0`; orphan FavoriteLocations `0` | PASS for observed checks |
| PITR | retention 7 days; differential interval 12 hours; earliest restore `2026-10-08T08:42:05.883482Z` | Configuration PASS; restore NOT RUN |

The UAT target is internally consistent with an `1.8.0-010` baseline. It is
not partially migrated to 011/012/014. That is positive safety evidence, but
it also proves that Full Scheme A cannot be enabled or tested there yet.

## 2. Schema-to-candidate dependency matrix

| Order | Contract | Current UAT | Candidate state | Release gate |
|---:|---|---|---|---|
| 1 | Canonical 011 Request/Event ledger | absent | Preflight/Up/Verify reviewed and isolated-rehearsed | migration review required |
| 2 | Canonical 012 Approved/Applied states | absent | Preflight/Up/Verify reviewed and isolated-rehearsed | depends on exact 011 |
| 3 | Reserved 013 | absent | no source in this package | MUST remain untouched |
| 4 | Canonical 014 Stage/PublishedVersions + V1 backfill | absent | disposable-only design | UAT-safe target guard and approved backfill required |
| 5 | Stage evidence audit events | absent | disposable-only, no SchemaVersion marker | numeric marker and recovery contract required |
| 6 | LeaderReviewDetails | absent | disposable-only; symbolic marker | numeric marker, compatibility and rollback review required |
| 7 | Create/LowRisk + reservation | absent | disposable-only; symbolic marker | numeric marker and data compatibility required |
| 8 | MergeDetails | absent | disposable-only; no SchemaVersion marker | numeric marker and reference-rebinding signoff required |
| 9 | Excel canonical statuses | absent | disposable-only; symbolic marker | ImportBatch/Item data scan and numeric marker required |

Machine-readable order and stop conditions are in
`full-scheme-a/UAT_Migration_Review_Manifest.json`. The existing symbolic
markers (`LEADER-REVIEW`, `CREATE-LOWRISK`, `EXCEL-CANONICAL-STATUS`) and
unmarked additive scripts are deliberate **HOLD** evidence; they must not be
executed in UAT as-is. Assigning release identifiers requires an Owner-reviewed
migration register update. It must not consume or modify 013.

### Collision and backfill review

- 011 requires both canonical tables to be absent; UAT evidence satisfies this.
- 012 replaces exact 011 CHECK constraints; any name/definition drift is STOP.
- 014 currently enforces `OptionCIsolated_*`; removing that guard is not an
  authorized shortcut. A separately reviewed UAT target guard is required.
- 014 version-1 backfill accepts only Approved + Active Locations and refuses
  ambiguity or rerun. Its row set and expected counts must be approved before
  execution.
- Stage audit and Leader review both replace the canonical event CHECK. They
  must be consolidated or strictly sequenced so all eight event types survive.
- Create/LowRisk replaces the known-code and published-origin CHECKs. Existing
  rows must be proven compatible before `WITH CHECK`.
- Excel replaces both legacy status CHECKs. Existing distinct statuses and
  column widths require a read-only compatibility report first.
- MergeDetails has no Location FKs by design. The application locks both
  Location rows, but the final migration review must explicitly accept this or
  add NO ACTION FKs without changing historical references.

## 3. Recovery implementation matrix

All PASS labels below mean **implemented and isolated evidence exists**. They
do not mean runtime UAT verification.

| Area | Recovery evidence | State |
|---|---|---|
| CreateLocation | `V180OptionCCandidateService.CreateAsync`; HTTP route `POST /api/v1/optionc-candidate/locations`; reservation/duplicate SQL test | implemented + isolated PASS |
| Existing Location maintenance | `V170LocationRepository.UpdateMaintenanceAsync` and `V180OptionCLocationMaintenanceRisk` block published High/Low direct writes | implemented + isolated PASS |
| Visitor ownership / Team / Org | `V180OptionCSubmissionScope`; `V180OptionCLiveAuthorizationAdapter.AuthorizeSubmissionAsync` | implemented + isolated PASS |
| Leader managed scope | `AuthorizeLeaderReviewAsync`; `V180OptionCLeaderReviewService.DecideAsync`; independent reviewer rules | implemented + isolated PASS |
| Shared read vs write | submission scope checks ownership independently of readable Location discovery | implemented + isolated PASS |
| HR/role revocation | live HR, employment, effective role, membership, appointment/delegation reread inside Serializable decision transaction | implemented + isolated PASS |
| Admin independence | `AuthorizeDecisionAsync`; self-approval denied; JWT never sole evidence | implemented + isolated PASS |
| Stage/evidence | `V180OptionCStageEvidenceService.RecordGeocodeAsync` / `RecordDuplicateReviewAsync`; version/evidence key binding | implemented + isolated PASS |
| Atomic publication/version history | `V180OptionCAtomicPublicationWriter`; Request-first locking; DecisionKey and Unknown Commit reconciliation | implemented + isolated PASS |
| MergeFields | `V180OptionCMergeService`; dual RowVersion and `LocationMergeDetails`; HTTP/SQL approve/reject/lost-ACK tests | implemented + isolated PASS |
| Excel Create/Update | explicit review routes; preview RowVersion, per-row canonical linkage and fail-closed mixed batch | implemented + isolated PASS |
| Legacy/background bypass | reservation guard, direct published-write denials, geocode/background exclusion matrices | implemented + isolated PASS; full deployed runtime unverified |
| Trip/mileage pending exclusion | operational reservation guard and real HTTP/SQL tests; immutable snapshots | implemented + isolated PASS |
| Report/export privacy | query/export rules and regressions exist | PARTIAL: exhaustive deployed role/browser matrix not run |
| Candidate API/UI | controller registered but service fail-closed; UI routes and role screens present | implemented; flags OFF; UAT runtime unverified |

Principal isolated transaction tests live in
`V180OptionCFinalSecurityGateSqlTests`, including Create/LowRisk, Leader stage,
Admin publication, Merge approve/reject/lost ACK, revocation, concurrent
decisions, duplicate DecisionKey, rollback and historical preservation.
Trip/mileage operational and snapshot evidence lives in
`V180TripMileageHttpSqlE2ETests`.

## 4. Security and ownership acceptance matrix

| Control | Evidence | Engineering | UAT runtime |
|---|---|---|---|
| Visitor cannot self-review | server decision rules + isolated HTTP/SQL | PASS | NOT RUN |
| Leader-originated request gets independent Leader | live appointment and actor inequality | PASS | NOT RUN |
| Admin cannot self-approve | live adapter + transaction lock | PASS | NOT RUN |
| HR leave/termination revokes access | in-transaction authoritative reread | PASS | NOT RUN |
| Role/team/delegation revocation | in-transaction authoritative reread | PASS | NOT RUN |
| Cross-org/team denial | submission, review and queue scoping | PASS | NOT RUN |
| RowVersion/stale evidence | request, Stage and dual-Location checks | PASS | NOT RUN |
| Concurrent approve/reject | independent SQL connections, exactly one terminal decision | PASS | NOT RUN |
| Unknown Commit | fresh non-pooled reconciliation, no blind retry | PASS | NOT RUN |
| Missing schema/flags | fail closed | PASS | UAT schema currently missing by design |

## 5. Historical invariants and UNKNOWN COMMIT

Before an approved migration, capture the SELECT-only counts/hashes in
`HistoricalFingerprint.readonly.sql`. Re-run after each Verify step. The three
evidence sets must match exactly:

1. `VisitTripStops`: identity, Trip, order, LocationId, frozen name/address.
2. `VisitTripSnapshots`: identity/version/type, approved distance/amount/status.
3. `VisitTripSnapshotStops`: identity, Snapshot, order, LocationId and frozen fields.

Any missing capture, count/hash difference, partial marker/object mismatch, or
lost migration acknowledgement is **UNKNOWN COMMIT / STOP**. Do not rerun a
forward migration. Reconnect with a fresh session, run SchemaVersions/catalog,
Verify, and historical fingerprints, then choose exactly one of:

- complete evidence: record committed step and continue only under the same window;
- confirmed rollback: correct the cause and obtain a new execution decision;
- incomplete/conflicting evidence: disable candidate flags and restore to the
  approved PITR point under separate restore authorization.

Forward SQL rollback is not the primary recovery for applied/backfilled schema.
The safe boundary is flags OFF plus PITR/restore. A restore has not been tested,
so migration execution remains HOLD.

## 6. Deployment and controlled activation design

No step below is currently authorized.

1. Freeze exact backend/frontend artifacts and checksums; confirm flags OFF.
2. Capture fresh target identity, schema/catalog fingerprint, row counts,
   historical fingerprints and an approved PITR recovery point.
3. Execute each migration as its own controlled step:
   Preflight -> Up -> Backfill where applicable -> Verify -> historical compare.
4. Deploy backend with Option C/B3/Candidate OFF; run health and existing v1.8.0 smoke.
5. Deploy frontend with approval UI OFF; verify no candidate navigation.
6. Run authenticated Visitor/Leader/Admin smoke against disabled routes.
7. Activate backend candidate only in the separately approved UAT window.
8. Run API security smoke; then activate frontend role entry points.
9. Execute role-based Business UAT; record the external signoff independently.
10. On P0/P1 failure: flags OFF first, stop writes, reconcile unknown commits,
    rollback artifacts; restore only with separate authorization.

## 7. Business UAT package

Use the 22 role cases in `SCHEME_A_BUSINESS_UAT_ROLE_CASES_NONDEPLOY.md`, plus:

- Visitor: own Create/Update, My Requests, Pending/Rejected/Applied, stale version.
- Leader: scoped queue, independent decision, return/reject, revocation.
- Admin: Stage/geocode/duplicate evidence, final decision, reconciliation.
- Excel: Create and published Update batches; mixed batch remains fail-closed.
- Merge: preview, rejection, atomic approval, current-reference policy and history.
- Trip/report/export: Pending/Abandoned candidates excluded; historical records visible.

Required accounts/data: one active Visitor, two independent Leaders (including
revocation case), one independent Admin, two organizations, at least two teams,
published Locations with stable V1 history, Project/Favorite/current references,
historical Trip/Snapshot fixtures, and Excel Create/Update files. No password or
token belongs in this package.

## 8. External 14-Gate traceability

The repository contains the original Gate 1 design-signoff ZIP. It does not
contain an independently verifiable completed external 14-Gate signoff record.
Internal CI, isolated SQL, this matrix, and future Business UAT must not be
substituted for that external signoff. Current state: **NOT OBSERVABLE / HOLD**.
The Owner acceptance record must link each external gate to its original
reviewer, timestamp and artifact before production release review.

## 9. Owner decision form

These are separate decisions; approving one does not approve the others.

| Decision | Prerequisites | Current recommendation |
|---|---|---|
| Assign immutable post-014 migration identifiers and approve migration review | consolidated additive scripts, checksums, compatibility and recovery review | HOLD |
| Authorize one UAT migration window | fresh preflight, recovery point, restore procedure, artifacts frozen | HOLD |
| Authorize backend/frontend deployment with flags OFF | migration PASS and compatible exact artifacts | HOLD |
| Authorize candidate backend/frontend flags | disabled-route smoke + role accounts + rollback operator | HOLD |
| Authorize Business UAT | deployed candidate, role/browser smoke and test data PASS | HOLD / NOT RUN |

## 10. Critical blockers and estimate

| Priority | Blocker | Evidence | Work / decision | Estimate after authorization |
|---|---|---|---|---|
| P0 | additive migrations are disposable-only, symbolic or unversioned | manifest steps 4-9 | consolidate UAT-safe scripts and assign immutable IDs without 013 | 1-2 engineering days + review |
| P0 | restore not tested | PITR config only | approve isolated restore rehearsal or documented recovery acceptance | environment dependent |
| P0 | no UAT migration/deployment/flags authorization | explicit boundary | separate Owner decisions | decision dependent |
| P1 | deployed role/browser smoke absent | Business UAT not run | deploy flags OFF, smoke, then controlled activation | 0.5-1 day after deployment |
| P1 | external 14-Gate record unavailable | repository search | attach original signoff artifact; do not substitute CI | Owner evidence |

### Final gate

- Engineering implementation: **PASS at prior Recovery exact-SHA evidence; new package requires its own CI**.
- UAT schema readiness: **HOLD**.
- Backup configuration: **PASS**; restore-tested: **NOT RUN**.
- Full Scheme A isolated E2E: **PASS for covered workflows; not UAT runtime proof**.
- Business UAT: **NOT RUN**.
- Deployment / release: **NO-GO**.
