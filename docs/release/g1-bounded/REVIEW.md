# G1 bounded v2 — separate NONDEPLOY review source

Preparation authority: Issue #41 comment 6376598983. No Protected merge, registration on main, dispatch, Azure write or feature activation is approved by this preparation.

## Actual Portal evidence, 2026-10-24 Asia/Taipei

Owner completed account selection/MFA in the WORK browser. Verified account/tenant and subscription matched the existing UAT context; identifiers are suppressed here. Fixed resource group rg-fieldvisit-uat contains api-fieldvisit-uat and sql-fieldvisit-jpe-uat/db-fieldvisit-uat. API is Running, Linux/.NET 8. Deployment Center lists 2026-10-13 02:00:27 (displayed time), deployment ID 2a2c420, OneDeploy, Succeeded (Active). This is not a binary hash or candidate-source proof. System-assigned identity is On; actual SQL login/effective permissions remain UNKNOWN.

DB is Paused, General Purpose Serverless Gen5/2 vCores, fixed FQDN verified. Earliest restore point displayed: 2026-10-16 23:21 UTC. Free allowance exhausted and overage enabled; bounded one-connection cost had already been accepted. No SQL connection was opened. Backups navigation timed out at the browser tool's 300-second boundary; one state read also timed out. Inspection stopped, with no refresh/retry/restore/configuration action. Retention/latest restore point and API+Frontend+DB recovery pairing remain UNKNOWN. Portal background ARM request count is not observable through the available browser interface; this record does not certify the 20-GET cap. Do not repeat that management inspection without a new exact execution approval.

Portal Query Editor documents a five-minute query timeout (Microsoft Learn, Azure SQL query editor, updated 2026-09-01). It cannot be relied on to enforce this authorization's 30-second command boundary. No Query Editor login or query was attempted. No Cloud Shell/storage provisioning, credential readback or firewall change was attempted.

Frontend source indicates GitHub Pages hosting via frontend-pages.yml. Actual published site hash/deployment identity was not established. No claim that accepted CI proves a live version is made.

## Frozen candidate

PR48 source 9ee5dacceed2bf55c39310d663f3258bcb545989; Run39998237406; Job120567275591; Artifact12520693555; digest88a6bb4a9d88da064da577f9439500d929ec2810581d5982c08665fd1141c324. No rebuild/retest/deploy.

The catalog query is copied byte-for-byte from tools/uat-release-candidate/runtime/SchemaFingerprint.sql at that accepted commit, except removal of its first SET NOCOUNT ON line. It contains only SELECT over metadata. The full private catalog hash is compared to accepted schema SHA742f2a95f7f58889d1609aceefa656030cf1f1365f10cc9c89968e89cfff0c63. Raw catalog/definitions/hashes/principal names are not logged or uploaded. Three aggregate candidate capability ledger markers are checked without enforcing old PRE010 history. Metadata equality and ledger presence are separately reported; no synthetic historical rows are generated.

## Source and execution boundary

New path only: .github/workflows/g1-bounded-readonly-v2.yml plus scripts/ci/g1_bounded and this document. Parent is Protected d919f8a107e3dbaee919b8c4e3b463240674eaf4. PR48, main, Protected and old workflows are untouched. Workflow is dispatch-only, so this preparation opens no automatic Azure/SQL job or accepted-CI rerun.

Before execution, an independent review must validate this exact delta, PowerShell parser/client behavior, the expected candidate schema digest against the accepted artifact, API versions, pinned identity-context digest and Azure-login request behavior. Local tests cover actor/ref/attempt denials, budget/deadline rejection before transport, no redirects, error request accounting/no retries and exact new Owner-consent matching; no local PowerShell/.NET runtime is available, so PowerShell parsing/live behavior are NOT certified by local tests.

Execution requires a separately approved final Protected SHA, a new Issue41 Owner authorization comment with the exact line:

`G1_V2_DISPATCH_APPROVED <final-protected-40-hex-sha> g1-bounded-readonly-v2.yml ONE_CONNECTION`

Owner need only approve the concrete operation in chat; the engineer prepares the exact technical approval record. The preparation comments 6376257812/6376423455/6376598983 are explicitly rejected as dispatch approval. Actor and triggering actor must both be Terry4410; repository/ref/source/run_attempt are checked before OIDC. The first dispatch consumes this workflow's allowance even on failure; retry/rerun is rejected. No alternate branches or environment allowlist changes.

GitHub dispatch registration on default main is also a prerequisite because this is a new workflow file; do not assume a Protected-only merge registers it. The engineer must first propose the exact workflow-only default-branch registration and Protected promotion SHAs for separate approval. No push trigger, old registered-workflow substitution or branch-guard bypass is provided.

The inventory job uses existing environment uat and existing federation only. Permissions: contents:read/id-token:write; no Azure grants. Pre-source guard uses actions:read/issues:read only for exact consent/first-dispatch checks. Existing identity context is hash-pinned; raw identifiers are masked before azure/login. The ARM collector makes five fixed GETs, no redirects/pagination/retry, 20-second per-request/120-second collector deadline. Azure login/context discovery is outside the five collector calls; its ARM GET behavior must be independently confirmed to keep the entire operation below20. Until proven, dispatch remains HOLD.

SQL runs only after the fixed management target passes. Pinned SqlServer22.4.5.1; exactly one Microsoft.Data.SqlClient.Open, pooling off, ConnectRetryCount0, TLS certificate validation, connect120s, each command30s, supervisor240s, job10min. No readiness/wake retry. A cold-start failure stops. Queries: fixed target/engine/metadata visibility and diagnostic grants; accepted schema catalog; three aggregate schema-ledger markers. No HR/business rows, DDL/DML/EXEC or Azure write calls. No secrets, raw metadata or exceptions in evidence. A privileged diagnostic principal is reported honestly and never substituted for the API's actual SQL principal.

## Deliberate remaining HOLDs

This collector cannot prove actual API/Frontend binary hashes, runtime SQL identity/effective object grants, nonsecret feature/trust settings, or a matched restoration pair. These remain UNKNOWN even if the collector succeeds; the job prints LiveG1=HOLD and never activates anything. Completing these facts needs a separately reviewed read-only evidence channel, not secret/environment dumps or an inferred CI-to-live equivalence. This change set is review material, not G1 READY or dispatch authorization.

Preparation is published as a new non-executing source branch. Opening a PR against Protected currently triggers the broad existing uat-schema-fidelity-proposal-nondeploy workflow (including old disposable recovery tests) without a path filter. To honor the instruction not to repeat accepted CI, no PR is opened in this preparation. Independent review can inspect the exact commit first. A later explicitly approved Protected promotion must also account for that automatic NONDEPLOY CI side effect; no workflow trigger or protection rule is changed to suppress it.

## UAT installation/deployment conditions

Before a separately approved deployment: fixed target identity, actual running API/Frontend/source hashes, complete installed schema parity/capabilities, actual least-privilege API principal, safe flags/trust and a concrete PITR+API+Frontend rollback pair must be evidenced. Current existing DB is not proven empty; accepted new-empty installer therefore cannot run on it. No reset/upgrade/new DB strategy is implicitly approved. An exact-target installation strategy must be reviewed first. Candidate artifact12520693555 remains the proposed release. Installation, deployment, principal grants, signed UAT attestation/activation and physical-iPhone three-role UAT each retain their separate explicit approval boundaries.
