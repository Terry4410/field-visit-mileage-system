# C1 timeout and Azure login privacy recovery — NONDEPLOY

Target: **C1_R3_NONDEPLOY_RECOVERY_READY_FOR_DELTA_AUDIT**, conditional on exact-SHA CI and log inspection. Not evidence that actual UAT timeout -2 is resolved. No UAT dispatch, SQL, merge, Azure configuration/identity change or release operation is authorized here.

## Certainty and bounded hypotheses

Protected baseline: `3f624facbb706c9d6e5ba07037ae63021a90d334`, tree `b6e9ce313481e0199b275c4f8f21d4b4fffc93ab`. Source branch: `work/c1-r3-timeout-privacy-recovery-20261019`, created directly from that baseline. The original worktree, source branch, stash/commit history and historical logs remain untouched.

C1 run 39195482465 / Issue #41 record 6259134083 showed successful OIDC, Azure target metadata and immutable source checks; IdentityVersion stopped with TimeoutOrTransient / -2 and later stages did not run. This proves neither a completed SQL connection nor a server-side statement. -2 alone cannot distinguish connection timeout from query timeout. Neither this recovery nor that result explains historical R2 UnknownSqlFailure.

The approved prior read-only UAT preflight at Protected b1a507c90c577fb2c0cdd6a98ef33481d3a5f790, `.github/workflows/azure-sql-readonly-uat-smoke.yml`, used ConnectionTimeout 120 with QueryTimeout 30 for its basic check. R3 now uses that same bounded connection allowance. QueryTimeout 30, one call per stage, order, STOP behavior, target allow-list and exact 010 guards remain unchanged. A four-stage worst-case connection/query budget is bounded by four times (120+30) seconds, with a 15-minute execution job cap. No driver/diagnostic retry is added. A real stopped-service test now exercises the 120-second bound; this is a failure-handling proof, not a slow-success UAT proof.

## Why login masking needs a job boundary

The inspected C1 action was Azure/login commit `7184910d9eb2b1c5e48f7073824a90609bb9b6d6` (v2.3.1). Runner preparation prints `with` and custom `env` values **before** a script or action executes. Therefore a naive masking step with IDs in `env`, or IDs interpolated into its `run` script, leaks before `add-mask` can register. Azure/login itself also writes federated issuer/subject/audience/job_workflow_ref via action `info()`; `AZURE_CORE_OUTPUT=none` alone cannot suppress those messages. The action masks its client ID only after the initial input rendering, and does not mask tenant/subscription values itself.

The minimum safe transport under the constraint of no stored-secret changes is:

1. The original registered dispatch-only workflow checks repository/ref/source and manifest/private-workflow pins in a credential-free guard.
2. A small `uat` environment job, with no OIDC and `contents:none`, evaluates the **same existing** Azure variable references as job outputs. It never places their values into a step's env/with/run. Runner output processing prints output names, not values. Debug mode fails before these expressions can release values; outputs are conditional on job success.
3. The same-commit reusable execution workflow receives those outputs as explicit transient `workflow_call` secret inputs. GitHub registers them for masking before step metadata is rendered. No stored secret is created, replaced or broadened; no `secrets: inherit` is used. Distinct secret parameter names avoid overriding the original Azure variable names.
4. The pinned helper explicitly registers client/tenant/subscription case variants and reviewed federation claim values before the unchanged Azure/login code runs. Only GitHub mask commands are emitted, with escaping and strict GUID/context validation; errors are generic. Missing/malformed values fail closed.
5. Azure CLI default output is `none`; required captured `--output tsv` target/token results are retained. Target metadata stderr and logout output are suppressed. The same observed Azure/login commit is pinned, avoiding new action-generated log surfaces.

The registered parent workflow identity remains unchanged, so main's inert registration stub needs no change. The child is **workflow_call only**, fixed to the original parent workflow_ref, dispatch event, repository and Protected ref. The existing `uat` environment subject and actual client/tenant/subscription values are retained. GitHub's `job_workflow_ref` claim now names the reusable file; the mask set includes both old parent and new child references. Compatibility with any external custom trust policy involving that claim remains unproven until a separately authorized future C1 run. No Entra/federation configuration change is made or authorized.

The execution body is moved, not redesigned. All four SQL files, full Inventory SQL and PR #38 classifier remain byte-for-byte unchanged. Full Inventory is still hash-checked but has no execution path. Auth remains Demo; Entra SSO stays deferred.

## Changed-file impact

| File | Delta |
|---|---|
| azure-sql-uat-schema-fidelity-readonly.yml | Keep registered manual entry; exact-source/private-workflow guard; existing-variable output to transient-secret routing |
| r3-private-uat-execution.yml | Same UAT body moved behind guarded call-only boundary; masks before pinned login; CLI quiet defaults |
| uat-schema-fidelity-proposal-nondeploy.yml | New isolated branch trigger and synthetic-only output producer; call isolated worker |
| r3-recovery-nondeploy-worker.yml | Existing real SQL service/test body moved; all disposable password/IDs arrive as transient secrets; actual runner log fixture added |
| uat_schema_r3.ps1 | Only operational line changes ConnectionTimeout 10 → 120 |
| uat_schema_r3.hashes.json | Updated runner hash and new privacy helper pin; SQL/classifier hashes preserved |
| r3_login_privacy.py | Closed mask registration, validation, escaped commands and generic failure |
| test_r3_login_privacy.py | Ten synthetic/static regressions plus actual GitHub runner fixture producer/consumer |
| test_uat_schema_r3.ps1 | Assert 120/30 on every real invocation; pre-mask restricted disposable fixture label/password |
| test_uat_schema_r3_contract.py | Follow guarded parent/child wiring, private-workflow/manifest pins; assert all accepted SQL bytes unchanged |
| test_uat_schema_fidelity_proposal.py | Follow moved execution/nondeploy worker while preserving original eleven test cases and SQL/classifier contracts |
| This document | Root-cause limits, impact, verification, audit checklist and separate authorization drafts |

Exact parent/tree/head, final CI IDs and artifact archive digest are bound in the Draft PR after successful CI. `scripts/ci/uat_schema_r3.hashes.json` is authoritative for reviewed file hashes; the parent workflow pins the manifest and private execution workflow. No circular hash pin is used.

## Verification and limits

Integrated nondeploy CI contains no Azure login/OIDC grant, no UAT environment, and no real Azure variable/secret reference. Its only secret inputs are generated synthetic fixture values. The SQL Server 2022 password is also carried as a transient secret so the runner's service/step rendering does not print it. Legacy public fixture definitions remain preserved in historical source, not logged in the current run.

Expected gates: original Python 11 + R3 contracts 6 + privacy tests 10 = 27; accepted PowerShell/DataSet 11; accepted real provider negatives 3; real disposable R3 21 (one four-stage positive and 20 negatives). Actual GitHub runner fixture prints synthetic `with`, `env` and action-like federation messages after initial secret registration and explicit masks. Delivery must inspect **actual completed job logs**, not merely the synthetic masker model, and confirm fixture IDs/password/claim values are absent. Unit output includes only fixed test names, never rejected synthetic values. The artifact contains source, hashes and safe test summaries; no SQL results or raw login logs.

These checks prove unchanged SQL semantics, preserved execution bounds and synthetic masking behavior. They do not prove Azure OIDC login success under the new reusable workflow, actual UAT connectivity, 120-second sufficiency, all possible third-party error formats, or resolution of -2. Known action info surfaces are covered at the pinned action revision; future action changes require review. Historical C1 logs are neither edited nor deleted.

## Primary source inspection

- [Runner handler metadata rendering](https://github.com/actions/runner/blob/main/src/Runner.Worker/Handlers/Handler.cs) and [script handler](https://github.com/actions/runner/blob/main/src/Runner.Worker/Handlers/ScriptHandler.cs): rendering precedes execution.
- [Runner job outputs](https://github.com/actions/runner/blob/main/src/Runner.Worker/JobExtension.cs): outputs are evaluated separately and names are logged; values matching existing secrets are suppressed, which intentionally fails the downstream handoff closed if a value cannot be transmitted.
- [Pinned Azure login configuration and federation info output](https://github.com/Azure/login/blob/7184910d9eb2b1c5e48f7073824a90609bb9b6d6/src/common/LoginConfig.ts).
- [GitHub reuse-workflow secret routing and caller context](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).

## Independent READ-ONLY Delta AUDIT checklist

- Verify branch began at exact Protected SHA/tree; enumerate this 12-file delta, no accepted SQL/classifier/application/migration/auth changes.
- Inspect only runner 120-second delta and real invocation assertions; ensure query 30, one call, no retries, wrong target/source/hash STOP, unsupported error suppression and all 21 real cases.
- Inspect identity handoff: original environment-scoped vars evaluated only as conditional job outputs, no raw value interpolation into logged step metadata, debug fail-closed, explicit transient secrets only, no stored-secret changes.
- Inspect call-only UAT child restrictions, original dispatch identity, environment subject, immutable pins and same observed Azure/login revision. Note changed job_workflow_ref and remaining future-runtime limitation.
- Inspect mask helper: validate before commands, escaping, upper/lower variants and four known federation output claims; no secret fetch or custom identity change; `none` is supplemental only.
- Inspect nondeploy producer/worker: no Azure/OIDC/UAT capability; only synthetic secret input allow-list. Inspect completed job logs for raw fixture IDs/passwords/claims, including pre-step/service metadata, and bind exact SHA.
- Verify all original suites still pass, artifact digest against downloaded bytes, source/parent/tree and safe summaries; original C1 remains failure/attempt 1.
- Report PASS/FAIL/HOLD for tooling only. Do not infer actual UAT timeout resolution or authorize merge/dispatch.

## Separate Protected merge approval draft — NOT AUTHORIZED

> Owner may separately authorize ONE normal merge of the independently accepted recovery Draft PR at [EXACT REVIEWED HEAD] into post-uat/v1.8.0, only if Protected remains 3f624facbb706c9d6e5ba07037ae63021a90d334; exact CI/artifact/scope and workflow-trigger safety all pass. Do not bypass policies. Post-merge verify exact parents/tree, file hashes, unchanged main/A+C and NONDEPLOY CI. No UAT SQL, R3 dispatch, grants, migration or deployment is included. Publish/read back one sanitized human Hub completion record. AI fills exact PR/head/evidence before presenting this draft for approval.

## Separate future C1 draft — NOT AUTHORIZED

> Only after independent audit, separately authorized merge and post-merge verification, Owner may authorize ONE new run of the original registered R3 workflow from [NEW EXACT PROTECTED SHA]. Verify unchanged SHA/tree, reviewed manifest/private-workflow hashes, existing approved UAT environment identity routing, no debug logging and no duplicate run before dispatch. Target only sql-fieldvisit-jpe-uat.database.windows.net / db-fieldvisit-uat, exact predecessor 1.8.0-010. Permit the same four stages in order, 120-second connection and 30-second query bounds, at most one invocation each, next only after PASS. No retry/redispatch. Full Inventory excluded. Stop on any failure or incomplete evidence; inspect whole-workflow privacy without reproducing values. No Azure/Entra identity/configuration or stored-secret changes, SQL privileges, DDL/DML, migrations, deployment, flags or Business UAT GO. Prior run 39195482465 must not be rerun. AI fills the reviewed post-merge evidence before Owner's separate submission.

Remaining HOLDs: independent delta audit; separate merge approval and postmerge validation; new one-run C1 authorization; actual UAT timeout/login/privacy outcome; full schema fidelity and historical baseline/data compatibility; release readiness and Business UAT NO-GO.
