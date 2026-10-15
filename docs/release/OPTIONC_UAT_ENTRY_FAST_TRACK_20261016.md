# Option C UAT Entry Fast Track — 2026-10-16

Status: **NONDEPLOY READINESS PACKAGE — NO UAT EXECUTION AUTHORIZATION**

This is the single meeting package for the 2026-10-16 afternoon Business User discussion. It freezes the Recovery candidate and separates what is already usable from what requires approved schema/deployment work.

## Frozen release candidate

| Item | Evidence |
|---|---|
| Repository | `Terry4410/field-visit-mileage-system` |
| Branch | `work/v180-optionc-final-gates-recovery-r1` |
| Candidate SHA | `67bd8eb34da6b4735964bd8ab6b64b848ab8dba6` |
| Exact-SHA CI | `38663584025` — SUCCESS |
| Backend | 758/758 PASS; Release build PASS |
| Frontend | 25 test files and build PASS |
| Disposable Option C HTTP/DI/SQL | 1/1 PASS |
| Canonical SQL rehearsal | 011/012/014: 2/2 PASS |
| Release gate | Explicit NO-GO PASS |
| Browser Business UAT | NOT RUN |

The candidate contains the controlled test-only route `POST /api/v1/leader/trips/{tripId}/route-retry`. It is guarded by `OptionC:Candidate:Enabled`, a loopback `OptionCIsolated_*` SQL target and the isolated execution policy. It is **not UAT-deployable** and must not be represented as a production route.

The four Recovery commits after the previous engineering checkpoint are:

1. `93cbcb298f8e7df7fb6945b18290e29a98abdbe4` — provider approval and isolated route endpoint.
2. `ee8c9f4bdf61419a66d2ce14700691b773c3facf` — precise governance event assertion.
3. `d4431854f8f535e3d140f23325b93d21d5950e26` — Return/Correction/Resubmit lifecycle E2E.
4. `67bd8eb34da6b4735964bd8ab6b64b848ab8dba6` — align correction state with the authoritative `Returned` contract.

## Three UAT scopes

| Scope | Can deploy/test now? | Schema/deployment dependencies | Flags | Decision |
|---|---|---|---|---|
| Existing v1.8.0 Business UAT | Existing non-Option-C behavior may be demonstrated only in an approved environment; this package did not deploy or preflight it | Existing v1.8.0 schema, target identity, backup, deployment approval | Candidate/approval UI and backend flags OFF | **LIMITED GO for review; NO execution GO** |
| Restricted Trip/Mileage UAT | Isolated evidence exists for draft/submit, One-stop/Dual-stop, controlled provider stub/manual fallback, Leader approval and snapshots | Target preflight, deployment artifact approval and target schema; no live Google API test | `OptionC:Candidate:Enabled=false` in deployed defaults | **NO-GO for UAT execution** |
| Full Scheme A / Option C UAT | Not deployable from this candidate | Canonical 011/012/014 plus additive Stage/evidence, Leader Review, Create/LowRisk, Excel status and MergeDetails contracts; DBA conversion/checksum/backfill and target preflight required | All Option C/B3/frontend flags OFF | **NO-GO** |

“Limited GO” means meeting/readiness review only. It does not authorize target access, migration, deployment, flag activation or Business User execution.

## Functionality and security matrix

| Area | Evidence status | UAT meaning |
|---|---|---|
| Trip Create → Submit → Leader manual fallback approval | PASS in disposable HTTP/DI/SQL | Candidate evidence only |
| One-stop and dual-stop | PASS in disposable HTTP/DI/SQL | Candidate evidence only |
| Controlled provider success route preview | PASS with `GoogleMapsRoutes-TestStub` | Stubbed provider, not live Google Maps |
| Return → Correction → Resubmit → Reapprove | PASS for submitted/returned history assertions | Full negative/revocation matrix not claimed |
| Submitted/Approved snapshots and LocationId preservation | PASS in isolated tests | Historical invariant evidence only |
| Pending/abandoned operational Location guard | PASS where covered by CI | Full report/export/trip read-path matrix HOLD |
| Excel canonical import | Partial; reviewed-create candidate exists, mixed Create/Update remains fail-closed | Not a full UAT workflow |
| Legacy/background canonical routing | Partial/fail-closed guards | Exhaustive route/race matrix HOLD |
| Browser role smoke | NOT RUN | Business UAT must not be inferred |

## Migration and target gate

The executable migration register currently ends at `1800_010`. Reviewed candidate dependency order is `1800_010 → canonical 011 → canonical 012 → canonical 014 → additive Stage/Evidence and Leader Review/Create/LowRisk/Excel/MergeDetails contracts`.

Candidate SQL under `docs/release/optionc-canonical-sql-draft/` is nondeploy/review material rehearsed only in `OptionCIsolated_*` SQL Server 2022. Migration `013` remains reserved and unchanged. No UAT target preflight was run because exact server, database, allow-list ticket, read-only identity and backup evidence were not supplied or authorized.

Required IT/DBA evidence before a separate execution request:

- exact SQL Server FQDN and database name;
- approved GitHub Environment and Entra read-only identity;
- target allow-list/change ticket and deployment window;
- read-only `ApplicationIntent=ReadOnly` schema fingerprint, dependency and constraint report;
- backup timestamp/retention, restore owner, tested recovery point and rollback contact;
- DBA-reviewed checksums, idempotency/partial-execution guards and migration rollback plan.

Any identity, schema, checksum, backup or dependency mismatch is STOP. No repair, restore or migration is to be performed during preflight.

## Frozen deployment/rollback prerequisites

1. Owner/IT/DBA approve the exact target and read-only preflight.
2. Backup and restore evidence is reviewed and the recovery owner is present.
3. Apply only approved migrations in exact dependency order; never touch 013.
4. Verify schema fingerprints and audit constraints.
5. Deploy backend, run health/authorization smoke with flags OFF.
6. Deploy frontend, keep candidate UI flags OFF during smoke.
7. Activate any flag only by a separate explicit Owner authorization.
8. To disable: frontend flags OFF, backend flags OFF, preserve audit/request/stage/history, reconcile uncertain commits, and restore only under an approved recovery procedure.

## 2026-10-16 Business User meeting script

### Opening (Owner/Release lead)

State that the meeting is a readiness review. No migration, deployment, flag activation or Business UAT PASS is being declared. Show the frozen SHA and CI, then explain the three scopes and explicit HOLD/NO-GO boundaries.

### Visitor

1. Sign in as an active Visitor in the agreed organization/team.
2. Create a draft with an Approved/Active Location and confirm normal v1.8.0 behavior.
3. For restricted candidate demonstration only, show submit/status and explain that Pending/Abandoned reservations are not operational locations.
4. Attempt a cross-team Location and a pending reservation; expected result is server-side denial with no Stop/Snapshot/Audit mutation.
5. Show the controlled provider-stub success/manual-fallback distinction and label it engineering evidence, not a live Google API result.

### Leader

1. Review only a request in the authorized organization/team.
2. Verify current requester HR/team state and RowVersion before action.
3. Exercise calculate, return-with-reason and correction/resubmit discussion.
4. Attempt self-review, revoked appointment and stale RowVersion; each must fail closed and leave status/history unchanged.
5. Confirm final mileage approval is Leader-owned and duplicate decisions are replay-safe.

### Admin

1. Confirm review queue scope is limited to the authorized organization/team.
2. Attempt self-approval and cross-scope approval; both must be denied.
3. For a ready canonical request, inspect Stage/evidence, Request, audit and Location/version history without manual SQL edits.
4. Explain Unknown Commit: fresh non-pooled read, evidence-based classification, never blind retry.
5. Confirm all approval UI/backend flags are OFF in the frozen candidate.

For each demonstration record actor, target organization/team, Taipei/UTC time, expected and actual result, HTTP/SQL evidence link, RowVersion, Request/Stage/audit identifiers and sign-off. Mark each item `DEMO ONLY`, `ENGINEERING EVIDENCE`, or `BUSINESS UAT` only after separately authorized deployment.

## Owner decision

- Existing v1.8.0: **LIMITED GO for readiness review; NO execution GO**.
- Restricted Trip/Mileage: **NO-GO for UAT execution**; isolated evidence may be shown.
- Full Scheme A/Option C: **NO-GO / HOLD**.
- UAT target preflight: **NOT RUN**.
- Backup/restore gate: **NOT VERIFIED**.
- Browser Business UAT: **NOT RUN**.
- Business UAT PASS/GO: **not declared**.

The smallest next action is an IT/DBA target-and-backup evidence package, followed by separate Owner authorization for preflight, migration, deployment and flags. Until then Scheme A remains HOLD/NO-GO.
