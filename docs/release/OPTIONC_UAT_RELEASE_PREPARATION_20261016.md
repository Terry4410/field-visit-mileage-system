# 2026-10-16 UAT Release Preparation — Deployment Discovery and Approval Sheet

Status: **PREPARATION ONLY — NO TARGET PREFLIGHT, MIGRATION, DEPLOYMENT OR FLAG ACTIVATION**

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
