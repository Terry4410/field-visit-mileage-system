# G1 Release Candidate — NONDEPLOY integration

## Boundary and exact inputs

This batch prepares ONE isolated G1 RC, not Business UAT GO. Source tuple, binary file hashes, candidate SQL hashes and actual test counts are bound by CI `artifacts/release-manifest.json`; the Draft PR records the exact final SHA/parent/tree/run/artifact digest. Canonical coordination remains Issue #41; existing machine-schema history is not rewritten.

- Protected P: `4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc`, tree `780003abb1750b36806832bf1f90745c2cd1af7c`.
- Accepted A+C A: `138cd8352ab180254ed83bce4a77acb3fdfc948f`, accepted CI `39145990902` SUCCESS / Worker real SQL13/13.
- Common ancestor: `b1a507c90c577fb2c0cdd6a98ef33481d3a5f790`. P ahead13; A ahead26. A-to-P comparison228 files is scope, not228 defects.
- Accepted C1 `39233612142` four stages PASS; consumed execution authorization. No new UAT connection is authorized.
- PR33 head `f7a167904ae2a742999ca8e3ce8e413aebad28f9`, PR37 `6d49ab337a4dfbc1def864097d0b48975e6e3a8c`; both Draft/unmerged. Their relevant accepted features already meet in A; do not merge their historical heads again.
- PR43 `e3f7f895afef73716225f550d494bbf02c02c371` Draft/unmerged, incomplete Package A policy/security acceptance. Its policy/inventory was read, not promoted. No bilingual workbook or new HR grants included.
- PR47 `8a6949bd3555f99bb42958f13ff0335513d24780` and PR48 `6c3da3bd3628424f78dca2f982c6bf9816bf73b0` remain Draft/unmerged. PR48 four-Gate strategy is read as planning input, not merged.
- Current UAT data are test/simulated only. No transfer of those rows to Production. Original1.5 provenance/independent clean build belong to G3, not a new prerequisite for this G1 nondeploy candidate.

## Semantic integration, not textual-merge acceptance

No reviewed existing candidate includes current P + A. An isolated `merge-tree` is textually clean (`e8776c763c1859589eb54da3a54e08d4037ed515`) but is NOT the accepted final tree. P's13 post-common commits change diagnostic/tooling paths, not application paths. A already contains the Program/DI, V160 repository, Visitor/Leader UI, Google, canonical Option C and EF reconciliation. These application bytes are reused except the narrow report guard and its tests below.

| Boundary | Candidate authority / resolution |
|---|---|
| Program/DI, Visitor draft CRUD/nav, Leader current scope | Accepted A bytes, including independent candidate service registrations; only add scoped report filter registration |
| Google provider/fallback/final approval, Trip/Snapshot, Option C versions/audit/reservations/merge, Safe Delete | Accepted A implementation and tests; no redesign |
| EF and candidate011/012/014–020 | Accepted A, unchanged; synthetic disposable contracts remain labelled as such |
| Protected R3, OIDC/login/privacy/readiness/runner, classifier, four SQL, deployment workflows | Exact P bytes and immutable pins preserved |
| Full Inventory semantic collision | A contains older inventory hash `c00a269a...` without Protected's51993 historical SELECT guard. Pure textual merge would silently use it and break the protected675519... pin. Keep exact P Inventory `67551978bf16266ef8f38c018be87b7aadc321ee3db84d6d94b52a1538204734` and update the inherited R6 static test to this reviewed pin. Never execute Full Inventory in G1; #47's separate syntax correction stays unmerged |
| CI/evidence | Reuse existing leader-uat010-rc-nondeploy workflow identity for this isolated branch. Replace obsolete partial-Leader/1.5-reference assertions with explicit P/A source closure; no extra privileged/new deployment workflow |
| Scope excluded | PR43 workbook redesign/grants; PR47 inventory tooling; PR48 documents; main or other active branches; G3 baseline creation |

## Narrow report security delta

Source inspection found report downloads could trust JWT role/team snapshots without fresh active-account/HR checks; ordinary query scope is not proof of live export eligibility. The added G1 API action filter applies only to `/api/v1/admin/query-exports/*` and Trip Excel/PDF downloads. It verifies current account/HR eligibility, single selected live role, organization and relevant team scope before action and before FileResult execution. Authorization drift or verification failure denies the response; provider/SQL details are not returned.

Existing Admin-only report layout/columns are retained as the accepted baseline. No HR role/capability/column expansion is granted, and PR43 is not certified. Visitor/Leader/HR cannot acquire Admin exports through a shared implementation. Unknown Visit Type ownership denies that export for every role. Unbounded/past Leader trip exports deny until the approved historical-and-current scope rule is implemented and independently verified. This is a deny boundary, not an invented historical grant. Broader per-report sensitive-column policy and Package A acceptance remain HOLD; do not describe baseline Admin access as a new HR approval.

The accepted real-SQL R9 Admin-export test remains byte-identical, including its positive five-sheet baseline. A separate G1 HTTP/SQL fixture tests disabled-account stale-JWT refusal, role/organization drift, unauthorized Leader/HR, response-time revocation and Visit Type policy denial. Necessary further tests/failures are reported by exact CI, not inferred from source. Human Business UAT still must exercise permitted reports with controlled test accounts; unapproved downloads remain unavailable.

## Verification scope and interpretation

One combined exact-SHA CI reuses frontend/backend suites, existing Worker13, real Visitor/Leader HTTP/SQL + two actual Chromium scenarios with independent SQL persistence/audit probe. A mandatory step-local E1 SQL context executes Option C security/full-candidate-chain HTTP writers and Trip/Google-success/failure/manual fallback tests against disposable SQL Server2022. E1 context is not job-global, preserving accepted recovery isolation. Build evidence includes API+Frontend source stamps, per-file digests and bundled CJK font. SQL fixture credentials are ephemeral, masked before use and absent from packages; local Docker is restricted to127.0.0.1. There is no Azure login, environment credential, shared SQL, privileged dispatch or deploy step.

R3/Protected contract tests may also run automatically under existing PR checks; they are not a C1 rerun. No accepted suite is repeated merely to inflate counts: this one combined run verifies integration and the changed export boundary. `g1-critical-sql.trx` must be nonzero and successful; early-return tests without SQL in the general backend run are not advertised as real SQL proof. Worker has its own required13-case result. Browser tests use real API, no route mocks; their scope is two draft workflows, not full Business UAT or all candidate UI paths. Other browser/human paths remain NOT EXECUTED.

Any missing/failed TRX, required method, browser evidence, source/ref/byte pin or build stamp causes STOP. Release-manifest decision is added only after final evidence verification. Actual run counts/digests are in the Draft PR and immutable manifest. Before exact CI completion all new results are NOT EXECUTED, not PASS.

## G1 release matrix

| Requirement | State before actual CI / deployment | Missing evidence; Business UAT blocker? | Minimum next action / authorization / lane |
|---|---|---|---|
| Accepted engineering and source integration | Accepted inputs reused; combined CI pending | Exact candidate build/security/HTTP/SQL/browser result until CI completes; YES | This authorized WORK completes exact evidence, then independent CHAT audit |
| R3/privacy/source pins | P bytes preserved; static CI verifies | No new C1 run needed for this batch; current runtime state not inferred | Reuse accepted C1; no dispatch |
| Actual runtime source/binary/schema alignment | HOLD | Active API/Frontend IDs+package digests/settings; required actual schema/permissions; YES | CHAT bound evidence plan; separate exact authorization before DB-touching or Azure inspection not already authorized |
| Required A+C runtime functions/flags | HOLD | UAT010 is not proof of011–020 or enabled functionality; YES for the promised full scope | Independently reviewed migration/deployment/activation package in later WORK, NEW Owner authorization; no activation now |
| Forward-only candidate migration promotion | HOLD | Predecessor/already-applied/partial states, actual required objects/constraints/indexes/triggers/permissions, quiescence, Up→Verify, recovery; YES where required runtime depends on it | Future narrow migration-package WORK and CHAT audit, then NEW execution authorization. Never remove disposable guards here |
| Prior matched rollback pair | HOLD | Historical API archive unavailable, Pages11785810643 reported404, current settings/active deployment unverified; YES before changing runtime | CHAT inventory of existing artifacts; later separately authorized retrieval/operational evidence. Rebuilding old source is not identical prior binaries |
| Safe test accounts / notifications / providers | HOLD | Exact live role/Org/Team/owner/HR and intended provider/allowlist state; YES | CHAT prepares bounded checks and test plan; NEW authorization for runtime writes/config/cost, no secrets in chat |
| Relevant exports | HOLD for unapproved history/Visit Type/HR expansion; narrow denial tests pending | Historical Leader entitlement implementation, Visit Type ownership, exact HR/sensitive-column catalog; blocks those download cases, not unrelated trip/location tests | CHAT identify minimum approved report scope/field rules; retain403 until reviewed. PR43 remains Draft. No inferred grants or silent waiver of required UAT cases |
| Current human/mobile Business UAT | HOLD | Real-user/iPhone results and signoff; YES for acceptance, not this NONDEPLOY preparation | After G1 review + explicit Owner start authority, Business users run approved cases; CI is not signoff |
| Full clean schema / original1.5 recreation | DEFER G3 | Clean baseline missing, not a G1 engineering proof | PR48 G3 plan, independent future scope; do not synthesize baseline here |
| Entra/Production/handover/import/retirement | DEFER G4/Production | IT acceptance, Production identity, approved inputs, recovery and separate retirement scope | Future distinct authority; Demo remains UAT-only; no automatic deletion |
| UAT test-row transfer / historical1.5 provenance as data-retention prerequisite | SUPERSEDED | Owner-approved test-data decision | Record honestly; no delete/import authority inferred |

PASS in final handoff is limited to source/CI evidence actually verified. These HOLDs do not convert G3/G4 into extra G1 prerequisites. This preparation never grants Business UAT GO.

## Runtime release and rollback evidence plan — nonexecuting

- Freeze exact reviewed candidate source and immutable API/Frontend file digests/archive digests. Record build toolchain/lockfiles, font package, public config, source stamps and required contracts. The manifest's `1.8.0-010` appsetting is a LABEL, not observed schema compatibility.
- Backend PackageB.B3, OptionC.Approval/Candidate and six frontend candidate flags remain OFF. Future activation needs an explicit matching server/client/schema/identity/provider plan; compiled OFF flags cannot be enabled merely by changing Azure variables.
- Existing api-azure/frontend-pages are main-push/dispatch deployment paths; they are unchanged and are not automatically safe exact-candidate promotion mechanisms. No deployment job is added or activated.
- Prepare future fixed UAT target, actual active deployment/package identity, required objects/permissions, nonsecret settings fingerprint and rollback-matched binary pair before any release action. Protect secret values and identities; no employee/provider payloads in public artifacts.
- Old observations remain dated: API source db235938.../run38334707319 is historical, Pages source0899f399.../run38338108364 is historical. No new live endpoint, /health/ready, Azure or UAT SQL inspection was performed. Missing archives are HOLD, not recreated by assumption.
- For a failed future release: stop affected writes, preserve evidence, assess a reviewed forward fix or matched application rollback against actual schema. No automatic database downgrade/restore; backup/PITR evidence and authorization are separate. Do not claim recovery PASS from an unexecuted SOP.

## Independent READ-ONLY CHAT AUDIT

1. Re-read exact P/A/candidate SHA/tree/parents, PR states and Hub; verify no other branch was changed.
2. Compare accepted A application/model/candidate SQL bytes with candidate; review only Program/filter/test exceptions. Verify Visitor/Leader/Google/Option C/history/reservation behavior remained accepted.
3. Recompute all Protected diagnostic bytes/pins, especially the silent Full Inventory guard collision; four R3 SQL/classifier/privacy/readiness/workflows must equal P. No Inventory or C1 execution.
4. Review report guard before/after-response checks, HR/account/role/team/Org denial and policy HOLDs; verify real SQL negatives, no new grants or PR43 promotion.
5. Inspect exact CI workflow isolation, local-only ephemeral SQL credentials, no job-global E1 coupling, no privileged trigger or deployment consequence.
6. Download artifact, verify registered/downloaded ZIP SHA-256, source tuple, binaries/file digests/CJK font, SQL pins and required nonzero TRX/browser/SQL-probe evidence. Separate real versus mock/human/unexecuted scope.
7. Check G1 HOLD matrix exact missing evidence, blocker impact, CHAT/WORK owner and NEW-authorization requirements; G3/G4 deferred.
8. Verify no Protected/main merge, UAT/Production SQL, wake, grants, migration, deploy, flags, shared data import/delete, restore or change to stfieldvisitbackup2608 / fieldvisit-backup / $logs.

Next minimum action after successful NONDEPLOY evidence: return to OWNER CONTROL TOWER CHAT for independent delta audit, then select the smallest remaining runtime/schema/rollback evidence batch. Do not automatically start further WORK or dispatch/deploy.
