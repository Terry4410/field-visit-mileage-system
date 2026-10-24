# D0 v3 minimal offline diagnostic repair — NONDEPLOY

This source is a diagnostic proposal, not new Live approval. D0 v2 Run
40060841541 attempt1 is consumed forever. Do not rerun it or edit its guard.
Protected baseline is `0f81cc0878b3e29e90512c5a58ad2ee5afa8713e`.
Main registration baseline observed is `8db9d7e809e2314f0e8dbc269d3332f8bc5279b5`.
PR48 remains `9ee5dacceed2bf55c39310d663f3258bcb545989`, accepted unchanged.
Authority: Issue41 independent audit comment6379672061 and Owner's offline work instruction.

## Demonstrated defect and delta

The v2 failure returned only HOLD/SUPPRESSED after about5.2s, not the300s
supervisor timeout. Its failing substage remains UNKNOWN. Artifact12547490950
ZIP SHA256 is `99b848c052d4fde9fcdf80e2ddff3ef472f7ad20d556ccd1dd043523d387328c`.
Nothing in this offline repair diagnoses an Azure provider cause.

Only the12 new paths in `scripts/ci/g1_d0_v3/guard.py:ALLOWED` may differ from
the Protected baseline. All existing tracked files are byte-identical, including
v2 workflow/worker/guard, SQL, PowerShell, C#, API, Frontend and migrations.
The v3 runner reuses the unchanged, checksum-pinned v2 fixed ARM/OIDC primitives
and SCM target validator. It never calls their CLI, SQL or full-G1 entry points.
Only the new standalone `remote.py` is sent to the container: /proc process
binding and the same six fixed file hashes, with no startup configuration,
process environment, appsettings, tokens, SQL, .NET launch or file writes.

One necessary transport delta uses **one short shell-command envelope**, then
one fixed Python **stdin-data** frame after the nonce READY marker. The command
is <=1024 bytes; the source payload is not interpolated into the first terminal
line and is read to its fixed byte count without a new remote dependency. The
fixed setup short-circuits Python on failure. Exactly two binary sends are used
(command + data), not two shell commands or another probe/session. This avoids
the Linux canonical PTY input-line truncation limit before stty can execute:
[Linux N_TTY documentation](https://kernel.org/doc/html/v5.19/driver-api/tty/n_tty.html).
A real local canonical PTY test executes this one-command + >16KiB synthetic
stdin protocol with /bin/sh and /usr/bin/python3; no original API or Azure. WSS endpoint, instance affinity, AppService audience, TLS checks,
no redirects/retries and fixed target remain unchanged. Linux /bin/sh, stty,
/usr/bin/python3, /proc visibility, exactly one main dotnet/API process and
mapped stable assemblies remain live prerequisites, not offline-proven facts.

## Closed stage and completion semantics

`stage` identifies the attempted local operation or expected remote boundary.
`lastCompletedStage` is the last successfully completed local boundary.
`remoteStageAttempted` and `remoteStageCompleted` remain UNKNOWN until strict
nonce-bound progress frames from the reviewed remote Python are validated.
An attempted MAIN_PROCESS wait alone is not evidence that Python started.

| Stage | Boundary / completion requirement |
| --- | --- |
| PRECONDITION | Exact context, mode, guard, pinned identity, source bytes and pinned runner client |
| ARM_TOKEN | Existing federation exchange for ARM audience returned |
| ARM_SITE_GET | Fixed site GET returned and unchanged Reader contract validated |
| ARM_INSTANCE_GET | Fixed instances GET returned and unchanged one-instance Reader contract validated |
| TARGET_SHAPE | Running Linux API, exactly one instance and allowlisted SCM host validated |
| SCM_TOKEN | Existing federation exchange for AppService audience returned |
| WSS_CONNECT | Single connection factory returned; this does not prove101 |
| WSS_101 | getstatus returned101 before any shell command |
| SHELL_READY | One command envelope send returned and exact nonce READY read |
| MAIN_PROCESS | Remote ATTEMPT/COMPLETE pair for unique process/start binding validated |
| SIX_HASHES | Remote ATTEMPT/COMPLETE pair for six fixed hashes and repeated stable process/maps validated |
| REMOTE_EXIT | Same session exact nonce DONE_0 required; early DONE/nonzero or missing DONE cannot pass |
| RECEIPT | Exact observation shape, six SHA256 values, composite, processStable and zeroSQL validated |

Remote progress is a source-bound protocol receipt, not independent hash
verification. Full hash/schema projection is checked at RECEIPT, after exit0.
No partial hash object escapes on HOLD. Unsupported provider exceptions map
to UNKNOWN; exception text/repr and HTTP/PTY bodies are never projected.
Closed error classes are in `d0.py:ERRORS`; remote errors accept only the
smaller fixed subset. A 403 returned as a status can be AUTH_DENIED; an opaque
library exception remains UNKNOWN, never guessed from its message.

## Counter semantics, including failures

| Field | Range | Evidence meaning |
| --- | --- | --- |
| armGetAttempts | 0..2 | Reader counter incremented before each fixed request; failure consumes the request |
| armGetCompleted | 0..2 | GET returned AND Reader shape/identity checks passed |
| wssConnectAttempts | 0..1 | Set immediately before the sole factory call |
| wssHttp101 | 0/1 | Number of upgrades positively observed with getstatus()==101 |
| wssHttp101State | closed enum | NOT_ATTEMPTED / UNKNOWN / CONFIRMED_NON101 / CONFIRMED_101 |
| remoteCommandAttempts | 0/1 | Set before the short shell-command send; never resent |
| remoteCommandSent | 0/1 | Command send API returned; not proof of complete delivery or execution |
| sourcePayloadAttempts / sourcePayloadSent | 0/1 each | Fixed source stdin frame attempted / send returned after READY; never another shell command |
| sqlOpenAttempts / sqlConnections | 0 | No SQL token request, library, module, connection or SQL GO route |

An unobserved101 is not proof that the server never upgraded; UNKNOWN preserves
that distinction. A failed send may have partially reached the server; remote
execution remains UNKNOWN until reviewed nonce progress/receipt exists.
No counter is a claim about internal platform network retries outside this
client. The client performs no retry/redirect/reconnect. Fixed ARM/SCM timeouts
remain20s/30s; shell READY30s, record45s, exit15s, WSS session120s ceiling and
overall supervisor300s +5s kill grace. PTY total2MiB / buffered1MiB caps remain.
SIGTERM raises a closed TIMEOUT, retaining in-memory stage/counters. SIGKILL,
runner loss or a missing/empty artifact cannot supply exact counts: HOLD/UNKNOWN;
never rerun to fill the gap. Closing WSS is bounded and sends no second command; the one additional frame
is fixed Python stdin data, separately accounted.

## Workflow, exact-source and first-run controls

Live identity is **g1-version-observation-v3.yml**, separate from consumed v2
and full G1. Only workflow_dispatch on post-uat/v1.8.0, Terry4410 as actor AND
triggering actor, attempt1 and input SHA==GITHUB_SHA can proceed. Source guard
checks baseline ancestry and exactly12 added allowed paths (no modified/deleted
old paths), then the precise new Owner comment, then exactly one v3 dispatch
whose Run ID, SHA, branch, event, path, actor and attempt match this run. A main
stub dispatch would consume this new identity too; **never test-dispatch main**.
The previous v2 Owner comment6378991235 is explicitly rejected. New prefix:

```text
G1_D0_V3_DIAGNOSTIC_APPROVED <FINAL_PROTECTED_SHA> g1-version-observation-v3.yml ONE_WSS ZERO_SQL NO_RETRY
```

The line must be posted by Owner Terry4410/id170161890 on Issue41 and explicitly
approve the reviewed privileged one-command main-container session. It is not
created by engineering and is not replaced by generic GET approval. No current
offline instruction or v2 consent is new Live permission.

Source job has no environment/OIDC. Only after it passes does the existing uat
environment provide identity variables; they cross to the private reusable v3
worker as secrets to mask logs, using the already verified v2 handoff pattern.
Worker repeats checksums/consent/history and pinned prelogin before federation.
No environment allowlist, RBAC, Basic Authentication or secrets are changed.
The inert main file has permissions{}, no checkout, environment, variables,
secrets, OIDC, shell download or Azure command; it prints NOT_EXECUTED and exits1.
Its exact bytes are the proposal `main-registration.yml`, installed at the
**same filename** on an isolated main-registration branch/PR, never mixed with
Protected executable source. The private worker is workflow_call-only.

## Offline acceptance / reused evidence

Synthetic tests exercise all13 failure boundaries, redaction canaries, opaque
and malformed errors, OAuth deny, GET failure/redirect, invalid instances/SCM,
factory failure, non101/403, send/PTY timeout, Python absent/early exit, ambiguous
process, missing hashes, unsafe JSON, missing/nonzero DONE, byte caps and TLS.
The local canonical PTY test specifically prevents a long-first-line regression.
Mock transports cannot connect sockets; remote fixtures do not read live /proc
or start the original API. Guard tests cover wrong Owner/SHA/main/ref/mode,
consumed v2 consent, extra/wrong v3 runs, reruns, additive-only source boundary,
inert registration, no environment/OIDC in offline CI and execution checksums.

Reused, NOT rerun: accepted PR48 CI39998237406/artifact12520693555; v2 offline
CI40046654388/artifact12541796831 and PowerShell/.NET8 zero-Open evidence.
No new SDK installation, clean build, application business test or SQL test is
needed because those source bytes are unchanged. Offline PASS proves the new
diagnostic contracts, not Azure access/platform compatibility or G1 completion.

## Automatic CI effects and promotion plan (NOT EXECUTED)

1. Push this isolated work branch: only the focused offline push workflow
   triggers. It has contents:read, no environment/OIDC, no dispatch trigger,
   no Azure/SQL/SDK commands; produces one exact-source test artifact.
2. Draft Protected source PR: the existing broad
   uat-schema-fidelity-proposal-nondeploy.yml also auto-runs its two offline
   jobs. That existing PR check cannot be suppressed without changing CI.
   No old v2/PR48 clean-install push branch matches this new branch.
3. Draft main-registration PR based on observed main: only one inert workflow
   file added. Current main has no matching PR trigger for that path. A future
   merge will not match api-azure/backend or frontend-pages/frontend push paths.
4. Independent audit and Owner approval of exact source-PR HEAD/base, inert
   registration-PR HEAD/base and source checksum manifest are required before
   either merge. No merge or dispatch is part of this offline work.
5. A future Protected merge triggers existing post-uat-v180-verify.yml (full
   application/frontend/offline-browser verification by its current policy).
   This is an unavoidable future automatic check, not a requested PR48 rebuild.
   No run17/ARM/SQL/deployment workflow path is changed. If the base drifts or
   policy adds a new trigger, stop and recheck the concrete diff/effects.

## One future authorization package — clear stop conditions

Present together: the exact two PR HEAD/base SHAs, focused CI Run/Job/artifact
digest and12-path checksum manifest; merge scope is inert main registration and
additive Protected v3 source only. Preserve uat restrictions. After approved
merges, record **actual final Protected SHA** and prove its full tree equals the
reviewed source tree. A source-PR SHA is not silently used as a merge SHA.
Only then is the exact line above populated with that final SHA and a **new**
Owner comment ID. Until those values and independent audit exist, execution
authorization is NOT READY; this document is a concrete proposal, not consent.

Future inputs: approved_source_sha=<that final Protected SHA> and
authorization_comment=<new exact Owner v3 comment ID>. Inspect global workflow
history first: v3 zero prior dispatches; v2 exactly consumed Run40060841541.
Use ONE channel to dispatch v3 ONCE from Protected only, attempt1. Never run main,
v2 or a full-G1 workflow. If a submission times out, query runs before any action;
never repeat. Resource scope stays rg-fieldvisit-uat/api-fieldvisit-uat, exactly
one verified main instance, at most2 fixed ARM GET attempts,1 WSS attempt and
1 source-pinned shell-command envelope, ZERO SQL. Failure/uncertainty stops HOLD
without retries, source changes, new targets, permission discovery or deployment.
Validate actual stage/completion/counters, six hashes and exit/receipt, artifact
digest and consumed history; update Issue41. No rollback action is authorized.

## Recovery inventory and remaining P0

`RECOVERY-INVENTORY.json` records GitHub-only evidence. Latest successful manual
API deployment: Run38334707319/source db235938b8b13ca8a65ab3b525de1df10d911645;
Frontend: Run38338108364/source0899f3996618c4b2bd02ecd659c76fdd07cc91ed.
API publish bytes were not uploaded by that workflow. Frontend original
github-pages Artifact11785810643 has a logged digest but currently returns404.
The surviving artifacts are Playwright reports, not deployable rollback bytes.
Pinned10/10 rollback sources are only source leads; their artifacts list empty.
The existing private archive README describes RC candidates, not active old
binaries; no new artifact download/rebuild can establish a live matching pair.

Remaining Live P0: (1) actual failed stage/provider cause, API process and six
hashes remain UNKNOWN until separately approved v3 evidence; (2) immutable old
API/Frontend rollback bytes and live pairing still unavailable. Full-G1 runtime,
API SQL principal/schema/flags and DB/application recovery gates remain outside
D0. Existing UAT DB is not confirmed empty: no reset/new-empty installer.
D0 PASS, if obtained later, is not full G1 PASS or Business UAT GO.
