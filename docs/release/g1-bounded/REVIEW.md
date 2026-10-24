# G1 v2 audit correction and precise publication plan

Authority: Owner's current WORK instruction and independent Issue41 audit6376978699 of source44cd3fc264d8d43c716613ba966cf0fa0a859c38. NONDEPLOY. Same branch work/g1-bounded-readonly-entry-20261024. No Protected/main ref update, Live dispatch, Azure/SQL call, deploy, flag/grant change or PR48 rebuild is authorized.

## Accepted source stays frozen

PR48 9ee5dacceed2bf55c39310d663f3258bcb545989; Run39998237406; Job120567275591; Artifact12520693555; digest88a6bb4a9d88da064da577f9439500d929ec2810581d5982c08665fd1141c324. Full schema SHA742f2a95f7f58889d1609aceefa656030cf1f1365f10cc9c89968e89cfff0c63. catalog.sql remains the accepted SchemaFingerprint.sql with only its leading SET NOCOUNT ON removed. No Clean Install/recovery/business tests are repeated.

Prior Portal observations remain partial: MFA/tenant/subscription/fixed rg-fieldvisit-uat/api-fieldvisit-uat/sql-fieldvisit-jpe-uat/db-fieldvisit-uat verified; API Running Linux/.NET8, Active OneDeploy10/13 ID2a2c420; system identity On; DB Paused, earliest restore2026-10-16 23:21UTC. No runtime hash, API SQL permission, safe flag or matched rollback proof was obtained. Portal background GET count was unobservable and Backups/state calls timed out. These observations are historical, not fresh execution receipts.

## H1: complete the evidence route BEFORE one-shot use

A successful catalog read is Inventory=COLLECTED, not LiveG1 PASS. Unconditional final LiveG1=HOLD output was removed from successful collectors. final_gate is a conditional acceptance contract with positive and negative tests: all six target/runtime/schema/API-principal/flags/recovery measurements must match, be fresh <=600s, and bind the exact reviewed collector/source, fixed target context and producer. It is NOT a receipt generator or a method of trusting an arbitrary PASS JSON. No live worker calls this contract with invented receipts.

The source guard now blocks BEFORE identity handoff/OIDC unless ALL four missing producers are implemented, sealed and independently reviewed. Current coverage intentionally remains HOLD. Changing route labels is insufficient: the source path and exact bytes must exist and match the sealed implementation. New producer paths and adapters must receive a fresh exact-source review before updating the source allowlist. No one-shot is to be dispatched in this source state.

<!-- G1_COMPLETION_ROUTES -->
{
 "runtime":{"state":"MISSING_SAFE_CHANNEL","sourcePath":null,"sourceSha256":null},
 "apiPrincipal":{"state":"MISSING_MAIN_APP_IDENTITY_CHANNEL","sourcePath":null,"sourceSha256":null},
 "flags":{"state":"MISSING_MAIN_APP_SELECTED_FACT_CHANNEL","sourcePath":null,"sourceSha256":null},
 "recovery":{"state":"MISSING_IDENTIFIED_OLD_BINARY_PAIR","sourcePath":null,"sourceSha256":null}
}
<!-- G1_COMPLETION_ROUTES -->

### Four exact evidence routes and their current blockers

| Evidence | Required real measurement | Approved-scope route / current blocker |
|---|---|---|
| API/Frontend runtime | Read deployed four application DLL bytes and source/contract; read published Frontend files against a source-bound per-file package manifest; bind the actual main-app process/release, not just hosted disk files or deployment labels. Preserve hashes for the current old release and separately proposed candidate. | Existing-federation HTTPS GET of four allowlisted Kudu VFS DLL paths and accepted release metadata can establish hosted bytes if the existing identity already has access. Public Frontend GET can establish served bytes after the actual host is independently confirmed. Main-process identity still needs a separately reviewed main-app read-only observation channel. No publish credential readback, Basic auth, appsettings.json read, arbitrary VFS listing, ZIP deploy or writes. Kudu permission failure stops; no grant. |
| Actual API SQL principal/effective grants | Prove the identity actually selected by the running API; execute the ONE metadata-only connection as that identity, with own effective permissions including required DML/view-definition and forbidden ledger/DDL/control/impersonation permissions. Bind SID hash privately. | GitHub OIDC diagnostic user's permissions do NOT establish these facts. A connection string, Entra admin label or system-identity On does not prove actual use. Existing deployed health/live endpoints expose version/status only. No suitable safe main-app principal channel has been proven. Do not use EXECUTE AS, broaden grants or reuse the diagnostic login as API proof. |
| Feature/trust flags | Read only selected nonsecret effective main-process facts: candidate/mode/activation state, signature validity/expiry boolean, source/schema/API/Frontend matches and configured fixed SQL target; separately read source-matched public Frontend runtime switch. Never output trust key, signature payload, token or full connection string. | ARM appsettings/list is POST and returns broad configuration; Kudu environment/process/environment dumps may contain secrets. Neither is allowed. SCM and the main Linux app are separate environments, so SCM printenv cannot prove main-app effective flags. A separately reviewed main-app selective-fact channel is required. |
| Recovery pairing | Fresh DB earliest/latest restorable interval and retention, exact selected UTC point, old API+Frontend package file hashes matching current deployed bytes, complete schema/source pair, artifact availability/expiry, operator and restore-to-separate-target/no-overwrite boundary. | Five ARM GET inventory supplies DB/retention metadata only. Old binaries must first be identified; a candidate ZIP or old disposable recovery exercise is insufficient. No restore/export/backup deletion or storage-container enumeration. Failure to prove the old pair remains HOLD. |

The missing main-app channel cannot be manufactured by the current GET-only diagnostic identity. Proposed least next extension is a separately reviewed, exact-source, bounded read-only main-container observation session using existing access, with no upload/install/app-code change and only selected facts emitted. Its actual transport/command, identity-token handling, availability and every request must first be established and reviewed; it is NOT implemented or authorized in this source. If that cannot be done without new access or a runtime change, STOP and return the precise additional operation for Owner decision. Do not quietly turn this into a deployment or add a diagnostic API to PR48.

This is the evidence-grounded staged completion route required by auditH1. It explicitly refuses to spend the SQL allowance for a known partial result. G1 can only pass after the real producers/adapters exist and the joined real measurements pass. Runtime mismatch or nonempty incompatible DB remains a genuine HOLD, not a missing test to be worked around.

## H3: bounded existing federation and identifier privacy

azure/login and Azure CLI/SDK discovery were removed. Direct normal workload federation uses the existing uat trust: one GitHub OIDC HTTPS GET and one Microsoft Entra token-endpoint POST per requested ARM/SQL token. These authentication requests are NOT ARM resource writes. Only two fixed scopes are accepted. No redirect, retry, subscription enumeration or account discovery. Context is hash-pinned before exchange. The sole ARM reader issues five exact GETs (resource group, API site, SQL server, DB, retention), counts errors before transport, refuses nextLink, caps each request20s and the reader120s. Aggregate inspection supervisor480s <600s; ARM child180s and SQL child240s. All current resource-plane ARM GETs are visible in this one reader: total5 <20. Future completion routes must add their exact request budget and preserve the aggregate bound; current count does not pretend to cover unimplemented routes.

Added one call-only private worker, making NINE files relative to Protected. This minimal extra workflow fixes pre-mask identifier echo: a no-env identity handoff uses existing uat variables as job outputs, then passes them as declared reusable-workflow secrets. Only the private worker exposes them in env, already protected by GitHub secret masking. Identifiers are never interpolated into run scripts. No new GitHub/Azure secrets, role or environment settings are created. Debug is denied. SQL token stays in a redirected private process pipe, never a file, Actions output or log.

## H4: focused OFFLINE executable contract

Only the push to this G1 branch triggers the new offline job. It has contents:read, NO environment, NO id-token and NO Azure credentials. It validates sealed source, Python positive/negative receipt/ref/actor/timeout/URL/GET/no-retry contracts, parses the actual PowerShell file with the runner parser, imports SqlServer22.4.5.1 from PSGallery, and invokes the SAME SqlClient constructor used by Live. It checks closed connection, fixed endpoint/DB, TLS validation, poolingOFF, retry0, connect120s and command30s. Open is never called. The sanitized offline artifact records exact source, contracts, coverageHOLD and Azure/SQL NOT_EXECUTED. It does not certify Azure API compatibility or actual connectivity; those remain bounded Live observations.

## H2: precise publication proposal — NOT approved for execution

1. Review this exact G1 branch SHA/tree/delta and focused offline Run/Job/artifact. All H1 producers/adapters must be implemented and their coverage gate READY before requesting Live publication. Do not open a Protected PR while coverage remains HOLD.
2. After that review, normal Protected promotion targets post-uat/v1.8.0 at pinned base d919f8a107e3dbaee919b8c4e3b463240674eaf4, with source/head SHA, tree and all sealed paths explicitly approved. If base moves, re-diff without replaying PR48 engineering. No direct Protected ref update or allowlist bypass.
3. Register ONLY the workflow path on main using this inert stub, through a separately approved normal source-promotion operation. No worker, app, backend or frontend is promoted to main. Pin current main parent, exact stub bytes/SHA and expected triggers at approval time. No main ref is changed by this preparation:

---BEGIN INERT MAIN REGISTRATION---
name: G1 bounded read-only v2 (inert registration)
on:
  workflow_dispatch:
    inputs:
      approved_source_sha:
        type: string
        required: true
      authorization_comment:
        type: string
        required: true
      accept_one_connection_cost:
        type: boolean
        default: false
        required: true
permissions: {}
jobs:
  registration_only:
    runs-on: ubuntu-latest
    steps:
      - run: |
          echo 'REGISTRATION_ONLY; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;'
          exit 1
---END INERT MAIN REGISTRATION---

Expected triggers: new source push -> focused offline job only; inert main registration -> no push trigger, neither existing API/backend path nor Frontend path changes, hence no deployment. Normal Protected PR opening currently triggers existing broad uat-schema-fidelity-proposal-nondeploy CI; Protected merge triggers post-uat-v180-verify. These side effects must be named in the exact promotion approval; do not disable workflows or use skip-ci to bypass required review.
4. Read back the final Protected merge SHA and require byte-identical approved G1 files, unchanged environment restrictions and fresh focused offline evidence for that source. Bind the fresh Issue41 Owner approval to that final SHA:
G1_V2_DISPATCH_APPROVED <final-protected-40-hex-sha> g1-bounded-readonly-v2.yml ONE_CONNECTION
Earlier authorizations/audit comments are not dispatch approval. Check Owner actor+triggering actor, attempt1 and no prior dispatch of this workflow. Input accept_one_connection_cost=true. A failed first dispatch is still consumed; no rerun.
5. Execute only AFTER coverageREADY, precise Owner approval and all fixed-source preflight checks. Use existing uat federation, exact target and 480s supervisor, <=20 aggregate ARM GETs and the ONE API-identity SELECT-only session. Current diagnostic inventory connection is not that API session; its adapter must be replaced/verified before coverage can become READY. Do not open two sessions to get both diagnostic and API evidence.
6. Join all six verified real measurements with freshness/source/target checks. Only then LiveG1=PASS. No business rows, DDL/DML/EXEC, Azure resource mutation, installation, deployment, restore, principal grant or activation follows automatically.

## H5: UAT installation and activation conditions

An existing Paused DB is not an empty DB. Do not run the accepted new-empty installer there, reset it, infer an upgrade path or fabricate migration history. Choose an exact compatible target/install strategy only after actual catalog/recovery facts, then obtain separate approval naming the target, artifact12520693555, exact installer manifest, least-privilege operator, API+Frontend pairing and restore boundary. G1PASS authorizes none of those writes. Candidate flags stayOFF until separately approved signed activation. Real iPhone/Visitor-Leader-Admin Business UAT and 25-case signoff follow controlled deployment; offline CI is not Business UAT.

## Primary references

- Microsoft workload identity federation/token exchange: https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow
- GitHub OIDC request variables and audience: https://docs.github.com/en/actions/reference/security/oidc
- Kudu/main Linux container separation and existing publish/Action permission requirement: https://learn.microsoft.com/en-us/azure/app-service/resources-kudu
- Manual registration requirement: https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax

Technical RC=READY; IT candidate package=READY; this G1 Live publication=HOLD; Business UAT=HOLD. The unresolved main-app evidence channel is an explicit P0, not an app-code defect or a reason to rebuild the accepted RC.
