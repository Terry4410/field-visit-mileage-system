# C1 R3 Serverless readiness and subject privacy recovery (NONDEPLOY)

Status: proposed tooling only. No Azure/UAT access, wake/resume, C1 dispatch, merge or billing/configuration change is authorized by this package.

## Evidence and certainty

Protected base: `f1f41f5611b2e844289c26b2604d201daa8501a6`, tree `e6740f48289c37bafc7689c55747762cc87df074`. Prior accepted PR45 is historical evidence, not reopened engineering.

Owner Portal observations: designated UAT database General Purpose Serverless; previously Paused; Free Database Offer applied; 100,000 monthly vCore seconds; overage enabled; min0.5/max2 vCore. These are Owner-supplied observations, not a new live Azure query or proof of current remaining credits.

Run39195482465 stopped IdentityVersion with TimeoutOrTransient/-2. Run39207007986 authenticated successfully, then stopped IdentityVersion with TimeoutOrTransient/40613; federation subject was not fully masked. Both authorizations are consumed. Their raw identity values are deliberately absent here.

The privacy defect is established: the old helper masked the legacy subject string, while the actual subject used the immutable owner/repository-ID format. The fix constructs both complete reviewed forms from validated built-in runner context before Azure/login. It preserves ID case variants, issuer, audience and parent/private workflow_ref masks, existing transient secret transport and the same Azure/login revision. Missing/malformed IDs or debug cause STOP before any value is emitted. No actual identifiers are hard-coded or added to step metadata. This covers the two reviewed subject formats, not arbitrary custom subject templates; a future format change remains a privacy HOLD.

Microsoft documents that a first connection to a paused Serverless database can trigger resume and return40613. This is consistent with the second C1 outcome and prior Paused observation; it does not establish the database's state at the exact failed invocation, prove that all40613 failures are cold-start, explain the first -2, or explain historical R2 UnknownSqlFailure.

## Minimum future path

The original registered workflow remains manual-dispatch only; the private workflow remains call-only with the same parent/repository/Protected-ref/event/environment constraints. Three explicit inputs are added:

- `approved_source_sha`: exact Owner-authorized resulting Protected SHA, required; checked before identity handoff.
- `authorize_coldstart`: boolean, defaultfalse; allows one connection-only wake ONLY if observed Paused.
- `accept_compute_charges`: boolean, defaultfalse; required together with wake permission. It is an acknowledgement, not a price cap or an Azure billing setting.

After source/pins/identity/target checks and login, readiness makes only fixed-target Azure management `az sql db show` GET calls. The returned resource ID, database name and a closed status enum are validated in memory; raw metadata/errors/IDs are never logged. Each CLI call has a10-second subprocess timeout. No `az update`, `resume`, SKU, billing, auto-pause, firewall or identity change exists.

| Observed state | Behavior |
|---|---|
| Online | No wake even when opted in. Require a second independent fresh Online GET, then allow R3. |
| Paused, either permission false | STOP WakeNotAuthorized; no SQL connection and no R3. |
| Paused, both permissions true | At most one connection-only Open/Dispose, then management polling and independent Online recheck. |
| Resuming | Do not wake. Bounded management-only waiting; PASS only after two Online observations. |
| Other/malformed/unknown, denied access, target mismatch or timeout | STOP; no R3. |

Readiness active window:300 seconds maximum, including any wake process; at most13 primary status reads plus one final recheck (14 GETs total),10-second intervals, no continuous keep-alive. Each GET is bounded10 seconds. The controller reserves time before operations and rejects an exhausted deadline; the workflow step has an additional6-minute safety timeout for process/runner cleanup.

The separately gated wake wrapper uses the existing SQL token/identity, fixed target and one `Microsoft.Data.SqlClient.SqlConnection.Open()` followed by Dispose. It executes **zero SQL commands/statements**, opens no result set, sets ConnectTimeout120, ConnectRetryCount0, Pooling=false, encrypted verified TLS and ApplicationIntent ReadOnly. The enclosing captured PowerShell process has a130-second timeout. Only Opened or the approved classifier's exact40613 signal is accepted; any other outcome STOPs. A40613 wake signal never counts as readiness PASS and never retries a connection. No new SQL probe is inserted into R3.

The existing R3 runner remains byte-identical:120-second connection,30-second query, exactly IdentityVersion → Catalog → HistoricalAccess → HistoricalAggregates, one Invoke-Sqlcmd per stage, next only on PASS, no R3 retry, unchanged010/schema guards and hashes. Full Inventory is hash-checked only and never executed. A database can become unavailable between a fresh Online GET and R3; a subsequent SQL failure still STOPs with no retry. Management status is not schema fidelity evidence.

## Financial exposure / future Owner decision

The free offer and current billing/compute/auto-pause settings are preserved. A future approved wake, or R3 against an already Online database, can consume remaining free compute. Overage is already enabled per Owner evidence: exhausted allowance can lead to charges. Remaining allowance, region-specific unit price, other sessions and actual billed compute are not established here; no exact dollar estimate or zero-cost guarantee is made.

The authorization can bound this tool's active work (readiness300s; four R3 calls at120/30 each; private job20min hard wall), **not total Azure billable Online time**. After closing the connection, the database may remain Online until the existing auto-pause criteria/delay are met; other clients may prevent auto-pause. This residual billing tail is not capped by stopping Actions. No keep-alive or forced pause/configuration change is added. At the stated max2vCore,300s of readiness alone corresponds to at most600 vCore-seconds of that compute ceiling, an illustrative active-window arithmetic bound only, not measured billing or a total-cost cap (excludes R3, auto-pause tail and other clients).

Future Owner approval must explicitly accept possible free-credit consumption, possible overage and the residual auto-pause tail. If a strict currency/total-billing cap is required, HOLD: this tool cannot enforce that cap with frozen Azure settings. Do not silently change Free Offer or billing behavior. No unattended retry, recurring dispatch or schedule.

## Verification and honest limits

NONDEPLOY CI has contents:read only, synthetic fixture secrets, runner-local SQL Server2022; no Azure login, OIDC permission, UAT environment or shared Azure credentials. It runs the preserved DataSet/classifier11 cases, real Invoke-Sqlcmd3 negatives and R3 real SQL21 cases. New synthetic state-machine tests cover Online/no-wake, Paused permissions, Resuming/time/read bounds, wrong target, permissions, unknown status, race, source/debug and40613 no-retry. The existing negative privacy suite is retained and extended for immutable IDs; real runner output emits both synthetic subject formats after mask registration, then completed logs must be independently checked for no unmasked fixture values.

A new disposable provider test parses the production wake wrapper and validates one Open/no-command/bounds plus the pinned provider's real encrypted local connection Open/Dispose. Its local certificate trust exception is test-only. It does not execute the fixed Azure wrapper or prove Azure resume/billing behavior. State tests are synthetic, while the R3 SQL21 suite and provider connection proof use the actual SQL Server2022 service. No mocks are presented as Azure/UAT or real SQL proof.

Pins include the unchanged four SQL/classifier/runner, updated privacy helper, new readiness controller and connection-only wrapper. Parent separately pins private workflow and manifest. The hash verifier's reviewed file set is extended; the old proposal test's no-input assumption changes only to enforce the new explicit SHA/wake/billing inputs. Original accepted SQL/classifier tests and R3 real SQL suite are untouched.

## Independent READ-ONLY Delta AUDIT checklist

1. Read exact remote PR Head/Parent/Tree/base; verify exact-SHA push/PR CI and downloaded artifact digest/source tuple.
2. Confirm one isolated branch from required Protected and only documented tooling/tests/workflows/documentation delta. main/A+C/Protected/historical PR commits unchanged.
3. Byte-compare runner, all4 SQL, full Inventory, classifier and original SQL test suites to base. Recompute manifest and private-workflow pins.
4. Inspect legacy+immutable complete masks, validated built-in IDs, no identifier step mappings, ordering before pinned login and preserved transient-secret/uat/OIDC restrictions. Inspect actual completed synthetic runner logs, not unit tests alone.
5. Verify the exact SHA input is enforced before identity, both opt-ins defaultfalse, Online never calls wake, Paused/Resuming/unknown states fail closed, 14-GET/300s/process bounds, no SQL probe/command and single wake Open, fresh independent Online recheck before R3.
6. Verify no retry after wake's unsupported error or any R3 failure; no Full Inventory, Azure mutation, migration or deployment path. Check Protected push and unrelated privileged workflow filters.
7. Check financial acknowledgement discloses remaining-free-credit uncertainty, enabled overage and uncapped residual auto-pause tail; no inaccurate fixed-cost claim.
8. Check real SQL21/provider proof and synthetic state tests are clearly distinguished. No new real UAT run or privacy/cold-start PASS is claimed.
9. Return ACCEPT_C1_R3_COLDSTART_PRIVACY_NONDEPLOY or bounded HOLD findings. Do not merge, dispatch, query Azure/UAT, change identities or grant privileges.

## Separate Protected merge approval draft — NOT AUTHORIZED

After exact-head independent acceptance, Owner may separately authorize one normal merge of this Draft PR's exact accepted Head into `post-uat/v1.8.0` ONLY if Protected remains `f1f41f5611b2e844289c26b2604d201daa8501a6`, exact-SHA checks/artifact/scope/pins/workflow-trigger/policy gates all pass. AI must fill the actual PR/head/CI/artifact values from the final delivery. Verify resulting parents/tree/pins and automatic NONDEPLOY CI. This draft grants no merge now and no Azure/UAT execution.

## Separate future C1 + optional wake authorization draft — NOT AUTHORIZED

Only AFTER separate merge approval and verified postmerge CI, AI must fill the exact resulting Protected SHA/Tree, reviewed pins and CI before Owner submits a NEW WORK instruction. No placeholder or this document constitutes authorization.

Proposed scope of that future prompt: authorize ONE new dispatch of the original registered workflow at `post-uat/v1.8.0`, exact postmerge SHA in `approved_source_sha`, fixed UAT target and existing identity. Explicitly set `authorize_coldstart=true` and `accept_compute_charges=true` ONLY if Owner separately accepts ONE connection-only wake when Paused, possible free-credit consumption/overage and residual uncapped auto-pause billing tail. If Owner approves R3 without wake, both inputs false and Paused must STOP. Never rerun39195482465 or39207007986.

Before dispatch: directly verify exact SHA/Tree/merge/CI/pins/source/identity/target/debug/trigger/duplicate-run gates. Readiness only uses the bounded fixed-target management GETs above; Online skips wake; Paused without explicit wake+cost authority STOPs; Resuming waits bounded; unknown/unavailable/permissions/timeouts STOP. Authorized wake is one Open/Dispose, no SQL commands,120-second connect, no retry; even expected40613 requires independently observed Online before R3. Readiness300s max/14 GETs; no keep-alive/repeated dispatch. R3 remains the four unchanged SELECT-only stages,120/30, at most one invocation per stage, stop-after-failure/no retry/Full Inventory excluded.

Inspect actual OIDC/privacy/stage evidence without reproducing identifiers, tokens, raw errors, counts or business data. Any privacy gap, failure or incomplete evidence means HOLD_C1_R3_UAT_DIAGNOSTIC. Four valid PASS results plus privacy integrity may yield C1_R3_UAT_PRECONDITION_DIAGNOSTIC_PASS only. Publish one sanitized human-readable result to Hub41 and read it back. Authorization consumed after one dispatch. No Production, DDL/DML, configuration/billing/Free Offer/auto-pause/identity/federation change, migration, grant/revoke, restore, deployment, flags, authentication or Business UAT GO.

## Primary references (read-only research)

- Microsoft Serverless auto-pause/auto-resume: https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-auto-pause-resume?view=azuresql
- Microsoft Free Database Offer: https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql
- Microsoft Free Offer FAQ: https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql
- Microsoft SqlClient ConnectRetryCount: https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnectionstringbuilder.connectretrycount
- GitHub OIDC immutable subjects: https://docs.github.com/en/actions/reference/security/oidc
