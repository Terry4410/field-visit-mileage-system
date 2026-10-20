# Existing UAT recovery procedure verification — NOT EXECUTED

## Verified historical records

- API run 38334707319 (2026-10-13) used exact source db235938b8b13ca8a65ab3b525de1df10d911645 and stamped source-sha.txt. Logs confirm successful OneDeploy ID 2a2c4204-e53e-47af-8739-c00343a05fe8 to api-fieldvisit-uat. The run artifact listing contains only uat-playwright-report, not the deployed API folder/archive. The workflow source confirms no API deploy-package upload step.
- Pages run 38338108364 used exact source 0899f3996618c4b2bd02ecd659c76fdd07cc91ed. Deploy log references original Pages artifact 11785810643; build log records ZIP digest 5d786255c5b59304737788bd049ea23e40be76ecf1d3c680e4ec561ca8fc5a12. Download returned 404. That digest is log evidence only, NOT a locally recomputed verified archive hash.
- Live Pages source stamp matches 0899f39; collected hashes for HTML, config, JS and CSS are in existing-uat-observations.json. Collecting some public bytes does not recreate the original deployment ZIP or verify active Azure state.

## Why existing rollback dispatches are not sufficient

The historical deployed workflow sources pin API rollback to 68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2 and frontend rollback to 007b79dba12a1668b66164ac9f8440bbb8ac8e90. Those are older sources, not proof of the currently running API/db235 or Pages/0899 binary pair. Rebuilding either source is not equivalent to obtaining the actual retained deployment artifacts. The current unapplied workflow proposal therefore blocks rollback until prior artifact IDs/digests are independently reviewed and pinned; do not bypass that block.

## Prepared recovery sequence (requires separate execution approval)

1. Before release, obtain the active Azure deployment ID, deployment source stamp/package identity, current Pages deployment ID and complete immutable prior API+Pages archives. Independently compute archive hashes, verify file inventory/fonts/source stamps and preserve the existing public config plus a nonsecret settings fingerprint. Never copy publish profiles, tokens or person/business rows into evidence.
2. Establish target identity, ordered deployment plan, write pause/canary boundaries and explicit matched rollback pair. If any prior archive or source/config binding is missing, do not deploy the candidate.
3. Upon an approved rollback trigger, pause affected writes; restore the verified prior API and Frontend packages/configuration as the approved pair. Do not rebuild an unrelated Protected HEAD as a substitute.
4. Verify active deployment identity and exact public content/source stamps, health/live only first, then separately approved canary checks. Do not run /health/ready or other database-touching probes under this current no-UAT-SQL authorization.
5. Preserve existing draft/audit/business history. This RC has no schema migration, so database restore, audit deletion or history rewrite is not a recovery step. Any DB operation requires a separate authorization.
6. A rollback also restores older authorization behavior. Keep affected functionality paused for Control Tower review when the prior version has known guard differences. Recovery success requires operator evidence, not a workflow success badge alone.

State: **HOLD — matched prior binary pair/settings and active API provenance unavailable.** No rollback, restore, deploy or write pause was performed.
