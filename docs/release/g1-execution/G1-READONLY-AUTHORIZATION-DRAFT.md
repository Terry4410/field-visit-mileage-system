# G1 bounded read-only evidence — authorization draft (NOT READY / NOT AUTHORIZED)

**Purpose:** collect evidence needed to decide whether a later UAT migration/deployment may be proposed. This document authorizes nothing. The approved RC and the accepted PR #50 43+5 SQL matrix are reused unchanged.

## Source and evidence boundary

- Frozen RC source: `9d431ea318fcec34f04f3cc36a8fb6d01d00c48a`.
- PR #50 source parent reviewed by this delta: `b18ced16aec934dedc6dcb8cdc90b5679c0e6f68`; the future exact candidate is the child SHA recorded by its focused CI.
- Existing synthetic Promotion reference remains labelled synthetic and is rejected as an observed UAT010 baseline. This offline source gate does not turn it into a live comparison.
- The accepted RC ZIP and Promotion artifact are retained and are not rebuilt or rewritten here.

## What a later Owner authorization would cover

**A — Management-plane READ only:** fixed display targets `rg-fieldvisit-uat`, `api-fieldvisit-uat`, `sql-fieldvisit-jpe-uat`, `db-fieldvisit-uat`, plus the GitHub Pages deployment record. Resolve and verify full resource IDs in-session; do not print subscription/client/tenant identifiers or secret-bearing settings. Up to 20 fixed GETs including redirects, 10 minutes, no retries, no pagination. Read active package/deployment metadata, nonsecret configuration source references, SQL operational/PITR metadata, and available deployment/backup package identities. No Kudu, files, health/ready endpoint, config export, write, restart, restore, or deployment.

**B — SQL READ only, separately approved after the source and privacy audit:** one connection to the verified fixed UAT server/database, 120-second connect timeout, 30 seconds per command, at most 10 minutes overall, no retries, fixed SELECT-only command allowlist, no dynamic discovery, no row values or raw catalog output in logs/artifacts. Any serverless wake is a separate explicit choice. If allowed, the single approved connection may cause compute charges and online tail billing; command/process timeouts do not cap billing. Unknown target, identity, metadata visibility, malformed output, timeout or privacy failure consumes the one attempt and stops.

SQL diagnostic identity results must be labelled as connected-principal evidence. They do not establish the deployed API principal. API proof requires the effective deployed `DefaultConnection` authentication context and principal comparison on that same application connection without exporting its value, token, name, or SID. Migration principal is a separate identity and permission matrix.

## Required sanitized outcomes

The collector may emit only fixed enums and booleans: `PASS`, `FAIL`, `HOLD`, `UNKNOWN`, exact reviewed source/package digests, and `NOT_EXECUTED`. Raw object names/definitions, schema JSON, SQL rows/counts, identity values, connection data, secrets, and exception text remain process-local and are not uploaded. No encrypted-evidence store is proposed.

## Current readiness and stops

| Gate | State | Reason |
|---|---|---|
| Source/hash closure | Offline candidate gate only | Must pass the exact-SHA CI; not a live-source approval. |
| Necessary UAT010 compatibility | HOLD | No accepted source-bound observed UAT010 catalog; full required facets and actual trigger event parity are not collected by this offline delta. |
| Actual API SQL principal | UNKNOWN | App Service `SQLAzure` type and source wiring do not prove selected auth mode or effective SQL principal/permissions. |
| Active API/Pages provenance | UNKNOWN | Prior deployment record/source label is not an exact active binary/package digest. |
| Compatible recovery | HOLD | No verified previous API+Pages binary pair and compatible database recovery procedure. |
| Live operation | NOT AUTHORIZED | No management request, SQL connection/wake, migration, grant, restore, deployment, or activation in this batch. |

**Decision:** do not approve or dispatch the SQL operation yet. A is not executable until independently reviewed source/output controls are available on a trusted existing management path. B is blocked until the candidate collector and principal boundary are reviewed and target IDs are resolved by an authorized management read. Migration and deployment need their own later decisions after evidence demonstrates need and recovery compatibility. Existing Visitor/Leader/Admin test login remains unchanged; Entra is deferred to IT.
