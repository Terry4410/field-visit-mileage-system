# SCM ARM6 fixed-source management read-only channel — NONDEPLOY

## Authority and immutable boundaries

Owner approved preparing **source, isolated branches, inert registration proposal, offline tests, audit and Draft PRs only**. Source preparation does NOT authorize the new Workflow dispatch, any actual Azure token exchange, ARM GET, SCM, WSS, Shell, SQL, role change or deployment.

- Authority: Issue #41 [comment 6380535540](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6380535540) (channel feasibility), [6380448269](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6380448269) (six GET read-only scope), [6380488858](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6380488858) (WORK PRE-GATE HOLD 0 GET).
- Protected source baseline: `0e4ede50141e5d7456f84fde9000db8c01c3b59e`. Main registration baseline: `0f057d78b4b5ebaabe360e0f891f866b68c074f2`.
- Accepted Draft PR48 head: `9ee5dacceed2bf55c39310d663f3258bcb545989` unchanged and NOT merged or rebuilt. D0 v2 Run40060841541 and v3 Run40065575855 remain consumed; no workflow reuses their authorization.
- All source changes are exactly 10 new files in `guard.ALLOWED`. No preexisting source/Workflow or Azure SQL schema changes.

## Distinct future execution path — NOT USED OR AUTHORIZED YET

The reviewed `g1-scm-arm6-readonly.yml` starts at fixed Protected SHA and must see a **NEW** GitHub Owner comment whose sole machine-readable line is:

`G1_ARM6_EXECUTION_APPROVED <ACTUAL_FINAL_PROTECTED_SHA> g1-scm-arm6-readonly.yml MAX6_GET ZERO_SCM ZERO_SQL NO_RETRY`

This new line is **NOT** posted during offline preparation. An approved new comment is required AFTER independent review, separate main inert and Protected merges, and actual final Protected SHA readback. Guard rejects former six-GET management consent as a dispatch token, accepts only Terry4410 OWNER/id170161890, actor+triggering actor same, attempt1, Protected ref, no debug, hash-pinned exact 10 new files and **first-ever run** for this distinct Workflow.

`main` can contain only `docs/release/g1-arm6/main-registration.yml` installed under `.github/workflows/g1-scm-arm6-readonly.yml`; its `permissions: {}`, no Azure/OIDC/environment or checkout, and unconditional `exit 1` prevent it being an execution path. **Never test-dispatch main**.

## Network and identity contract

Uses the existing `scripts/ci/g1_bounded/arm.py:exchange_token('https://management.azure.com/.default')` GitHub Actions OIDC exchange, already proven in D0 v3 before WSS. The PRE-GATE requires same pinned tenant/sub/client fingerprint, actor+source+Owner+first-run guard and Entra-HTTPS-issued access token with `appid`/`azp`, `tid`, `oid`, `aud` bound to the pinned app. Token claim content is read only in process memory, never logged, written or accepted from workflow inputs. Do not treat a human Portal token as the GitHub OIDC enterprise application. Failure before token binding => 0 ARM GET/HOLD.

The GitHub OIDC assertion request and the Entra `/token` request are not ARM GET requests; source reuses only the previously approved federation technique. They remain **future live operations** requiring a separate precise Owner execution consent; code preparation does not authorize them. No new federated credentials, Azure roles, settings, accounts, ACL, custom SDK or Azure CLI.

The ARM client calls ONLY these six fixed ordered GET paths (all under the exact pinned site; no aliases, hidden discovery or retries):

1. `GET {site}?api-version=2024-04-01` — target/site Linux and Running shape.
2. `GET {site}/config/web?api-version=2024-11-01` — SCM use-main flag, effective default allow/deny, actions of SCM/main rules (no IPs, priorities, headers or rule names), SCM TLS category.
3. `GET {site}/providers/Microsoft.Authorization/roleAssignments?api-version=2022-04-01&$filter=atScope()` — in-memory assignment binding to same Entra service principal; unresolved group/condition means UNKNOWN.
4. `GET {site}/providers/Microsoft.Authorization/roleDefinitions?api-version=2022-04-01` — in-memory relevant permission actions/notActions; unresolved definitions UNKNOWN.
5. `GET {site}/providers/Microsoft.Authorization/denyAssignments?api-version=2022-04-01&$filter=atScope()` — possible explicit denies; ambiguous denies UNKNOWN.
6. `GET {site}/providers/Microsoft.Authorization/permissions?api-version=2022-04-01` — caller-effective permission checked only for the verified SAME federated app principal; no human token substitution.

For each GET the runner increments attempted count **before** the request, forbids redirect, retry, proxy discovery, pagination or alternate URL. Only 200 responses, exact response URL and bounded 1 MiB JSON are accepted. A 401/403, exception, missing shape, any `nextLink`, unknown group/deny, source change or request-budget violation holds and stops. The output JSON has a closed schema, with per-step PASS/ATTEMPTED/SKIPPED, bounded counts (0..6), SCM policy enums and OIDC publish YES/NO/UNKNOWN only when fully grounded. `scmNetworkFeasibility` is always UNKNOWN (management configuration does not measure GitHub runner network/WSS success). Never print raw ARM JSON, URL, IP, role/identity list, JWT/headers/secret/provider exception, tenant/client/subscription IDs, business data or connection strings.

Conservative effectivity rule: direct applicable grant definition plus caller-effective permission positive plus no relevant group/conditional/deny ambiguity is needed to say `oidcPublishAction=YES`; caller-effective negative and completely parsed lists can say NO; any incomplete facts => UNKNOWN. A PASS_METADATA_ONLY does **not** prove WebSocket HTTP101 or runtime six hashes. Role list pages >1 stop rather than follow. A 6-GET output that contains UNKNOWN stays HOLD without retry.

## Accepted offline scope and future promotion plan

Offline CI `g1-arm6-offline.yml` executes local synthetic negative tests ONLY: exact source guard, wrong Owner/run/attempt/SHA, 0 pre-auth GET, 6-request ordering and exhaustion, redirect/provider failure/pagination redaction, group/deny conditional uncertainty, wildcard permission interpretation, main inert registration and workflow source no-Azure privileges. No live Azure, Microsoft Graph, SQL or WSS.

After CI success, independent audit and explicit Owner approval must separately authorize (1) PR for inert main registration and (2) PR for additive Protected source. Normal postmerge checks do not authorize Live. Only after the final Protected SHA is verified and a **different** explicit single-run Owner authorization to exchange tokens and perform at most six fixed ARM GET is posted may the guarded Workflow be dispatched ONCE on Protected; failed, skipped or ambiguous attempt consumes the new workflow identity. The previous read-only scope authorization is preserved; do NOT reinterpret it as permission to dispatch new code.

If any workflow/job executes unexpectedly, Azure access is attempted, old code changes, or test security gates fail, the work must STOP without new commits/merges/retries until independently reviewed.

## What remains on HOLD

Azure live SCM network access, Kudu publish permission, WSS feasibility, deployed API/Frontend hashes, original rollback pair, SQL principal/schema/flags/recovery and Business UAT. Existing 53MB PR48 nondeploy candidate remains useful for IT technical prehandover only, not a deployed code proof or permission to run on populated UAT DB.
