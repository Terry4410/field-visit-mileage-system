# 2026-10-16 UAT Release Preparation — Deployment Discovery and Approval Sheet

Status: **PREPARATION ONLY — FORMAL BUSINESS UAT DELAYED; NO TARGET PREFLIGHT, MIGRATION, DEPLOYMENT OR FLAG ACTIVATION**

## Authoritative delayed-UAT checkpoint — 2026-10-15

| Field | Verified value |
|---|---|
| Recovery branch | `work/v180-optionc-final-gates-recovery-r1` |
| Frozen engineering candidate | `b91c8405593c1754ba19f1fb56af78f036e1b8cf` |
| Exact-SHA Recovery CI | `38668490973` — **SUCCESS** |
| Candidate change since prior frozen release | Readiness package and review evidence only; no runtime feature activation |
| Authenticated existing-v1.8.0 smoke | Run `38668230583`, 9/9 PASS; engineering/controlled smoke evidence only |
| Formal Business User UAT | **DELAYED / NOT RUN** |

The Owner elected to delay formal Business User UAT so that release quality is
not reduced to meet a calendar date. This SHA is a documentation-aligned
engineering candidate, not a deployment approval. Existing v1.8.0 remains
eligible only for a future separately authorized controlled smoke/review;
Restricted Trip/Mileage remains NO-GO and Full Scheme A/Option C remains
HOLD/NO-GO.

The following gates are intentionally unresolved and must not be inferred from
CI or public health responses: exact UAT target identity, target
SchemaVersion/catalog compatibility, backup/restore and rollback evidence,
deployment authorization, and feature-flag authorization.

## Read-only GitHub deployment discovery

The Recovery branch is not a deployment branch. The deployment workflows have
their own exact-branch and environment gates:

| Function | Workflow | Gate discovered | Result |
|---|---|---|---|
| Backend Azure UAT | `.github/workflows/api-azure.yml` | `environment: uat`; manual exact-SHA input; dispatch branch must be `work/v180-visitor-only-uat010-security-fix` | Recovery SHA cannot be dispatched |
| Frontend Pages | `.github/workflows/frontend-pages.yml` | manual exact-SHA input; dispatch branch must be `work/v180-visitor-only-uat010-security-fix`; `github-pages` environment | Recovery SHA cannot be dispatched |
| UAT browser smoke | `.github/workflows/uat-smoke.yml` | requires UAT secret and target health/readiness; write flow is separately controlled | Not run from Recovery |
| Recovery CI | `.github/workflows/optionc-final-gates-recovery-nondeploy.yml` | Recovery branch only; disposable SQL; explicit NO-GO | `38665061182` SUCCESS |

Repository workflow configuration records these UAT URLs, but they were not
called in this batch:

- Frontend: `https://terry4410.github.io/field-visit-mileage-system/`
- Backend: `https://api-fieldvisit-uat-cxauf4g8fzdyfsd9.eastasia-01.azurewebsites.net`
- Readiness: `/health/ready`
- Health: `/health`

These are configuration evidence, not proof that the endpoints are currently
healthy or that the database is the intended UAT target.

## Read-only runtime health evidence (owner-authorized GET only)

At Taipei time **2026/10/15 09:47 (+0800)**, the owner-authorized, non-destructive GET checks were executed against the configured public endpoints. Response bodies were discarded; no login, authorization header, SQL, mutation, migration or deployment was used.

| Endpoint | Result | Safe metadata |
|---|---|---|
| Frontend `/` | **HTTP 200** | GitHub.com; last-modified: Tue, 13 Oct 2026 02:42:21 GMT; public ETag recorded |
| Backend `/health` | **HTTP 200** | Kestrel; application/json |
| Backend `/health/ready` | **HTTP 200** | Kestrel; application/json |

This confirms public HTTP reachability and that the readiness endpoint responded at the check time. It does **not** prove the deployed SHA, UAT database identity, SchemaVersion, backup/recovery readiness, or feature-flag state. Therefore the Existing v1.8.0 track remains **LIMITED GO for a future controlled readiness review only**, with no Business UAT execution authorization. The scheduled Business User UAT is **DELAYED** pending release-candidate gates. Restricted Trip/Mileage remains **NO-GO for UAT execution**, and Full Scheme A remains **HOLD/NO-GO**.

## Last recorded deployment evidence

Read-only Actions metadata shows successful historical runs, but not a current
runtime preflight:

| Target/function | Run | Source SHA | Status |
|---|---:|---|---|
| Existing post-UAT API | 38019868384 | `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2` | SUCCESS |
| Existing post-UAT Frontend | 38018403878 | `007b79dba12a1668b66164ac9f8440bbb8ac8e90` | SUCCESS |
| Visitor-only API candidate | 38334707319 | `db235938b8b13ca8a65ab3b525de1df10d911645` | SUCCESS |
| Visitor-only Frontend candidate | 38338108364 | `0899f3996618c4b2bd02ecd659c76fdd07cc91ed` | SUCCESS |
| Visitor-only smoke verification | 38337473605 | `0899f3996618c4b2bd02ecd659c76fdd07cc91ed` | SUCCESS |

The source pair actually serving the target, current deployment timestamp,
runtime health and database SchemaVersion remain **UNVERIFIED**. No generic
deployment record was used as a substitute for target preflight.

## Three-track release decision

| Track | Current evidence | Deployment readiness | 2026-10-16 decision |
|---|---|---|---|
| Existing v1.8.0 | Historical API/frontend deployment and successful smoke metadata exist | Requires IT confirmation of current runtime and schema; no Recovery promotion | **LIMITED GO for a controlled readiness review; no automatic UAT GO** |
| Restricted Trip/Mileage | Recovery isolated HTTP/DI/SQL PASS for Create/Submit, One-stop/Dual-stop, ManualFallback, Leader approval and history | Recovery candidate is not deployed; route-retry is isolated-only; live Google Maps not tested | **NO-GO for execution; DEMO ONLY from engineering evidence** |
| Full Scheme A/Option C | Recovery-only candidate plus disposable schema rehearsals | Requires additive schema conversion, target preflight, backup, deployment and flags | **HOLD / NO-GO** |

## Exact target and schema evidence request

| Required item | Responsible party | Status | Blocking? |
|---|---|---|---|
| SQL Server FQDN and database name | IT/DBA | Missing | Yes |
| UAT Frontend/Backend runtime identity | IT/Release | Historical configuration only | Yes |
| GitHub Environment and allow-list/change ticket | IT/Release | Missing | Yes |
| Entra read-only principal | IT/DBA | Missing | Yes |
| Current SchemaVersion and catalog fingerprint | DBA | Not run; target preflight unauthorized | Yes |
| 011/012/014 compatibility and additive contract state | DBA | Not verified against target | Yes |
| Backup timestamp, retention, restore owner and recovery point | DBA/IT | Missing | Yes |
| Deployment window and rollback contact | Owner/IT | Missing | Yes |
| Visitor/Leader/Admin test identities and effective scopes | HR/IT | Secret values not requested or printed | Yes for Business UAT |

Target preflight must be read-only with `ApplicationIntent=ReadOnly`. Any
identity, schema, checksum, dependency or backup mismatch is a STOP condition.

## Consolidated Owner authorization sheet

Each item is a separate authorization; preparing this sheet does not grant it.

- [ ] **A — Target identification/preflight:** authorize read-only access to
  the exact allow-listed UAT target using the approved Entra identity.
- [ ] **B — Migration:** authorize exact reviewed migration order and additive
  contracts after DBA checksum/preflight PASS; migration 013 remains reserved.
- [ ] **C — Backend/frontend deployment:** authorize exact artifact SHAs and
  protected deployment workflows; no Recovery branch bypass.
- [ ] **D — Feature flags:** authorize each backend/frontend flag separately;
  defaults remain OFF until this box is signed.
- [ ] **E — Controlled smoke:** authorize non-destructive Visitor/Leader/Admin
  smoke against the verified target.
- [ ] **F — Business User UAT:** authorize the chosen track, date, roles,
  evidence capture and sign-off process.

Current package status: **A–F unchecked; no execution authorization granted.**

## Meeting-ready scope

### Included for discussion/demo

- Existing v1.8.0 functions only after IT confirms the current deployment.
- Engineering evidence for Trip Create/Submit, One-stop/Dual-stop,
  ManualFallback, Leader approval and historical snapshots.
- Server-side denial examples for pending/cross-scope locations.

### Excluded

- Full Scheme A/Option C approval UI/API.
- Isolated `route-retry` as a UAT feature.
- Live Google Maps validation.
- Excel canonical publication, complete Legacy/Background routing and full
  report/export matrix.
- Any SQL migration, deployment, flag activation or Business UAT PASS.

## Business User role scripts

**Visitor:** login; create draft; select approved active location; test
one-stop; submit; view status; attempt pending/cross-team location and record
server denial. Do not alter SQL or classify engineering evidence as UAT.

**Leader:** login; verify team scope; calculate/ManualFallback; return with
reason; inspect correction/resubmit; approve only an authorized request; test
self-review, revoked appointment and stale RowVersion denial.

**Admin:** login; inspect authorized queue and master-data/history views;
attempt self-approval and cross-scope approval; verify audit/version evidence;
explain Unknown Commit fresh-read reconciliation. Candidate approval flags stay
OFF.

For every case record actor, target, organization/team, Taipei time, expected
result, actual result, evidence link, RowVersion and sign-off. Label each result
`DEMO ONLY`, `ENGINEERING EVIDENCE` or `FORMALLY AUTHORIZED BUSINESS UAT`.

## Rollback prerequisites

Before any future action: verified backup, restore owner, tested recovery point,
rollback contact, exact artifact SHA and deployment window. Disable sequence is
frontend flags OFF → backend flags OFF → preserve audit/request/stage/history →
fresh-read Unknown Commit reconciliation → restore only under separate approval.

Full Scheme A remains **HOLD / NO-GO** until the target, schema, backup,
deployment and Owner approvals are independently complete.


## 2026-10-16 Business User meeting package — existing v1.8.0 only

This section is a meeting/review aid only. It is not Business UAT authorization and does not convert engineering evidence into UAT acceptance.

### Evidence classification

- Authenticated smoke Run **38668230583** on deployed source **68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2**: **ENGINEERING / CONTROLLED SMOKE EVIDENCE**.
- Business User execution, sign-off and defect acceptance: **NOT RUN**.
- SchemaVersion, backup/restore, rollback and formal UAT authorization: **HOLD**.

### Included for review

- Login and role-page access for existing Visitor, Leader and Admin accounts.
- Existing published/active master-data lookup within the account scope.
- Existing Trip draft/create/submit screens only where already deployed and approved for demonstration.
- Existing one-stop and approved mileage display as a controlled demonstration, without candidate route-retry or Scheme A features.
- Existing historical record visibility demonstration, without changing historical data.
- Server-side denial examples using already available negative test accounts or pre-existing safe records; no SQL fixture creation.

### Excluded from the meeting scope

- Restricted Trip/Mileage candidate routes and route-retry.
- Full Scheme A / Option C approval, Leader first-level candidate review, Excel canonical publication, MergeFields and candidate evidence workflows.
- Any migration, schema change, deployment, restore, flag activation, direct SQL change or data cleanup.
- New Location publication, destructive maintenance, production-like write testing and any Business UAT sign-off.

### Visitor SOP — review/demo only

1. Open the existing frontend and sign in through the approved account mechanism.
2. Confirm the Visitor role pages load and only the assigned organization/team data is visible.
3. Open an existing approved workflow or draft demonstration record; do not create new persistent UAT data unless separately authorized.
4. Verify status text and expected validation messages.
5. Record evidence as ENGINEERING / CONTROLLED SMOKE EVIDENCE, not BUSINESS UAT PASS.

### Leader SOP — review/demo only

1. Sign in with an existing Leader account and verify team scope.
2. Open the existing trip/mileage review view available in v1.8.0.
3. Confirm approved mileage/status visibility and that out-of-scope records are not exposed.
4. Demonstrate validation or authorization denial using an existing safe scenario only.
5. Do not execute candidate approval, return/correction, route-retry or new publication flows.

### Admin SOP — review/demo only

1. Sign in with an existing Admin account.
2. Confirm authorized read-only master-data, project and historical views.
3. Confirm candidate Option C approval/evidence functions remain unavailable or OFF.
4. Verify no migration, flag activation or publication action is offered in this scope.
5. Record any issue in the issue register without altering data.

### Meeting test checklist

| ID | Role | Check | Expected result | Classification |
|---|---|---|---|---|
| V180-01 | Visitor | Login and role pages | Successful login and scoped pages | ENGINEERING / CONTROLLED SMOKE EVIDENCE |
| V180-02 | Visitor | Organization/team visibility | Only authorized scope is shown | Review only |
| V180-03 | Visitor | Existing Trip screen | Existing deployed screen loads | Review only |
| V180-04 | Visitor | Validation/denial message | Server/UI explains denial without data mutation | Review only |
| V180-05 | Leader | Login and team scope | Leader pages load with assigned scope | ENGINEERING / CONTROLLED SMOKE EVIDENCE |
| V180-06 | Leader | Existing mileage/status view | Existing approved result is visible | Review only |
| V180-07 | Leader | Out-of-scope record | Access is denied or record is omitted | Review only |
| V180-08 | Admin | Login and read-only views | Authorized views load | ENGINEERING / CONTROLLED SMOKE EVIDENCE |
| V180-09 | Admin | Candidate feature visibility | Option C/Scheme A actions remain OFF | Review only |
| V180-10 | All | Historical display | Existing history remains readable and unchanged | Review only |
| V180-11 | All | Health/readiness | Existing public health/readiness responds | Controlled smoke |
| V180-12 | Owner | Gate review | No Business UAT sign-off without separate authorization | Mandatory gate |

For every item record actor role, target URL, expected/actual result, timestamp in Taipei time, screenshot or run link, and whether the result is ENGINEERING / CONTROLLED SMOKE EVIDENCE, DEMO ONLY, or FORMALLY AUTHORIZED BUSINESS UAT. The last label is unavailable in this batch.

### Issue handling

- P0: security exposure, unauthorized data access, wrong organization/team visibility, or destructive side effect. Stop the review.
- P1: core existing v1.8.0 function unusable or materially incorrect. Do not broaden scope; record and hold.
- P2: cosmetic, wording or nonblocking usability issue. Record for later triage.
- No issue may be fixed by direct SQL or by enabling a candidate feature during this review.

### Gate status after this preparation

Existing v1.8.0: **LIMITED GO for future controlled review only**. Formal Business UAT: **DELAYED / NO-GO until release-candidate gates and separate authorization are complete**. Restricted Trip/Mileage: **NO-GO**. Full Scheme A/Option C: **HOLD / NO-GO**.

## Delayed-UAT completion gates

Formal Business UAT may be reconsidered only after all of the following have
actual evidence, not assumptions:

- frozen backend/frontend artifact SHAs with matching Exact-SHA CI;
- approved target identity and read-only schema/catalog preflight;
- compatible SchemaVersion and reviewed migration dependency/checksum matrix;
- verified backup timestamp, retention, restore owner, recovery point and
  rollback contact;
- approved deployment sequence and post-deployment role smoke;
- explicit feature-flag state and activation authorization;
- required real HTTP/DI/SQL security, concurrency and historical-preservation
  evidence for the selected scope;
- a separately authorized Business User UAT plan and sign-off boundary.

Until then, meeting materials are review/demo material only. No result may be
recorded as Business UAT PASS.
