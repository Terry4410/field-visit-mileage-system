# D0 transport audit and publication boundary — 2026-10-24

Authority: Issue41 comment6378379443 and current Owner WORK instruction. Source/offline only. No Azure request, container session, SQL, dispatch, merge, grant, installation or deployment is authorized/performed here. Accepted PR48 9ee5dacceed2bf55c39310d663f3258bcb545989 / Run39998237406 / Artifact12520693555 remains unchanged.

## Independent primary-source comparison

Azure CLI official source snapshot: **0fb575456532726e3c83aadb300957424dde8e9c**. This is the reviewed implementation, not a claim that a dev snapshot is installed in UAT. Platform documentation requires CLI>=2.90.0 for the preview command. The CLI's existence alone does not verify this adapter or the actual UAT instance.

| Contract | Official evidence at pinned CLI source | D0 result |
|---|---|---|
| SCM URL | `custom.py:_get_scm_url` selects the fixed site's Repository hostNameSslState | Same selection, plus fixed app/hostname allowlist, fixed subscription/RG/site resource ID; no supplied host |
| Main container | `webapp_exec.py:_start_shell_session` builds `/exec/shell`; only `target=kudu` changes default app target | Same `/exec/shell?shell=/bin/sh`, no target=kudu/sidecar/slot or arbitrary command input |
| SCM audience | `custom.py:get_bearer_token` uses `cloud.endpoints.app_service_resource_id`; AzureCloud `cloud.py` sets **https://appservice.azure.com** | **Corrected**: separate App Service /.default token for WSS; ARM /.default used only by management GETs; no SQL token |
| Authentication | Official helper supports AAD; if SCM Basic is enabled it can instead retrieve publishing credentials | D0 deliberately uses only existing Entra federation; never queries/retrieves Basic credentials, enables Basic or changes a role |
| Permissions/network | Microsoft Kudu docs require application-scope publish/Action and reachable SCM | Required but **NOT OBSERVED** for this runner identity; HTTP401/403/network failure -> stop; no grant/fallback/retry |
| Instance | CLI validates requested instance then sends ARRAffinity cookie | Exactly one instance from exact site instances GET; fixed resource ID/name; same cookie; >1/unknown -> stop |
| Framing | Official CLI sends raw UTF8 shell bytes using binary opcode2; receives text/binary; terminal resize is optional JSON text | Same binary input, accepts text/binary, CRLF aware, no resize needed for fixed commands; no automatic reconnection |
| Upgrade/redirect | Pinned websocket-client1.8 can return a 3xx handshake without following it when redirect_limit=0 | **Corrected**: require getstatus()==101 before ANY shell bytes; TLS certificate+hostname required; 3xx rejected |
| Real PTY behavior | Interactive shell emits PS1/PS2; multiline quoted Python creates continuation prompts | **Corrected after local reproduction**: session-local PS1/PS2 cleared, echo/canonical mode disabled; one physical base64-encoded source command; newline before result |
| Completion | Microsoft says execute-mode202 is acceptance, not completion/stdout/exit proof | Interactive route only. After valid nonce/hash record require same-session G1_DONE_nonce_0. Nonzero/timeout/disconnect/partial result -> HOLD |
| CLI alternative | `az webapp exec --shell /bin/sh --instance <fixed>` requires interactive stdin and forwards all terminal output; helpers may do Basic discovery/credential reads and extra ARM calls | Not invoked. Wrapping it does not remove these scope/privacy concerns or install Python. Direct bounded adapter is source-proven equivalent for the selected protocol, not an official support guarantee for custom code |

Pinned CLI files and Git blob IDs:
- [webapp_exec.py](https://github.com/Azure/azure-cli/blob/0fb575456532726e3c83aadb300957424dde8e9c/src/azure-cli/azure/cli/command_modules/appservice/webapp_exec.py), blob32c6c990d954777912a87b4855b2ccb3e054d9de.
- [custom.py](https://github.com/Azure/azure-cli/blob/0fb575456532726e3c83aadb300957424dde8e9c/src/azure-cli/azure/cli/command_modules/appservice/custom.py), _get_scm_url/get_scm_site_headers/get_bearer_token examined together.
- [cloud.py](https://github.com/Azure/azure-cli/blob/0fb575456532726e3c83aadb300957424dde8e9c/src/azure-cli-core/azure/cli/core/cloud.py), blob4149c14f198422e3f9b221338a048f5ace9beea4.
- [websocket-client1.8 _core.py](https://github.com/websocket-client/websocket-client/blob/v1.8.0/websocket/_core.py), blobf940ed0573d2f7756a53f6bc4f77c8cf2b33ec48; [_handshake.py](https://github.com/websocket-client/websocket-client/blob/v1.8.0/websocket/_handshake.py), blob7bd61b82f44d5862e16a9cb56850ef595eec61fd.
- [Microsoft App Service container access](https://learn.microsoft.com/en-us/azure/app-service/configure-linux-open-ssh-session), [SCM permissions](https://learn.microsoft.com/en-us/azure/app-service/resources-kudu).

## What is proved and what remains HOLD

Source protocol comparison PASS; local actual Linux shell/PTY transport and negative completion/redirect tests PASS. The local fixture uses a real PTY with a source-sized payload, not a canned JSON socket alone. No actual Azure WSS endpoint was contacted; no claim of live interoperability, working RBAC/network or installed binaries follows from these tests.

Required target prerequisites (NOT inferred from Linux/.NET8): running Linux main container, existing `/bin/sh`, working `stty`, executable `/usr/bin/python3` with compatible standard library, visible/readable `/proc` PID/stat/maps/cwd/cmdline, exactly one API launched as `dotnet /home/site/wwwroot/FieldVisit.Api.dll` from that fixed root, four mapped DLL paths with matching inodes, and two extra fixed files. Python/proc absence or a different .NET invocation returns no measurement. The current source deliberately does not broaden process matching or install a runtime to force success. An image/source attestation can establish these prerequisites without Live execution only if it is tied to the actual deployed image; no such attestation is available in this source audit.

**Actual UAT container compatibility: HOLD / NOT EXECUTED.** Safe source-publication readiness and an application for a bounded attempt do not mean D0 has succeeded. Do not announce D0 PASS before a valid record plus exit0 are obtained. If the Owner requires all on-instance prerequisites proved before authorizing any shell, execution readiness remains HOLD; do not silently use shell as a smoke test.

## Exact bounded D0 operation to review

- Existing uat federation/context pin only. Two token exchanges: one ARM audience and one App Service audience, each one GitHub OIDC GET + one normal Entra OAuth POST. Authentication POSTs are not Azure resource writes. No SQL audience, credential extraction or token output.
- TWO ARM GET attempts: fixed API site and its fixed instances collection, API2024-04-01. Requests<=20s, aggregate reader120s, every attempt consumed, no pagination/redirect/duplicate path/retry.
- ONE WSS upgrade to the verified SCM host, one selected main instance. Handshake30s; readiness30s; D0 measurement+completion within remaining90s after readiness; outer300s includes authentication/ARM. Every stage uses the same session. No reconnect, second instance or second command attempt.
- Source-fixed Python from sealed main_probe.py, followed only by discover_main(nonce). Session-local terminal settings only. No container file write/upload, package installation, API startup, environment/configuration/connection string/token read, Basic credential, shell-supplied user script, or SQL connection. The dormant full-G1 functions present in the historical source are not called. D0 has no SQL_GO method and no probe binary is sent.
- Six file hashes only: FieldVisit.Api.dll, FieldVisit.Application.dll, FieldVisit.Domain.dll, FieldVisit.Infrastructure.dll, Microsoft.Data.SqlClient.dll, FieldVisit.Api.runtimeconfig.json. Read only PID identity/mapping metadata needed to bind the four API files to the actual process. No directory export or business rows. Sanitized record, source SHA, hash composite, zero-SQL labels and exit proof only.
- Failure consumes the D0 workflow attempt. No rerun regardless of successful earlier stages. Full-G1 SQL allowance is distinct and untouched. No restore/cleanup is needed for read-only application state; close the session and report HOLD. Container loss, timeout or uncertainty is not permission to deploy or reinstall.

## PR74 and Protected promotion

PR74 HEAD5dec1d80c2ed354fddbaad683fe9c1b4b0e36ede, main base d737c1d0e3b569b9794994261770cb77bfb5b764: exactly one 25-line added workflow, permissions:{}, no checkout/environment/secrets/id-token/worker, timeout1min, only fixed message+exit1. **Safe inert registration source PASS; merge NOT AUTHORIZED.** Registering does not prove transport or give Azure access. Never dispatch the stub: it performs no D0 and would consume the strict D0 workflow-dispatch counter.

Protected source targets post-uat/v1.8.0 at d919f8a107e3dbaee919b8c4e3b463240674eaf4 and the unchanged G1 branch, not PR48. The whole nineteen-added-path G1 source delta is explicit, including prior inert full-G1 preparation; no application/accepted RC/base workflow is edited. Full-G1 source guard still refuses before OIDC while its machine-readable completion map is missing. Shared worker requires mode=D0_VERSION_ONLY for D0 and false SQL cost; all FULL_G1 SQL/module steps are skipped. No branch allowlist change.

All existing Protected workflow triggers were read before PR creation. Opening/synchronizing this Draft PR triggers **uat-schema-fidelity-proposal-nondeploy.yml** (no path filter), with synthetic-handoff and an existing disposable/static R3 worker. This is the unavoidable existing PR check, not PR48 Clean Install/business CI. Its source uses synthetic credentials; no live Azure calls. It is neither disabled, skipped nor manually rerun. Protected merge later triggers **post-uat-v180-verify.yml**; its backend/frontend checks are an existing source-promotion side effect and require naming in merge approval. main registration only matches neither api-azure.yml backend path nor frontend-pages.yml frontend path; no automatic deployment follows either proposal. No pull_request_target/workflow_run escalation was found in the reviewed base workflow inventory.

The new focused push CI runs the Python contracts once. Run40046654388/Job120729820147/Artifact12541796831 native/PowerShell acceptance is reused only after six exact source inputs match accepted4046c655 byte-for-byte. This is asserted by git show, not a claimed rerun. Artifact and summary distinguish current D0 tests from reused native evidence and retain full Live completionCoverage=HOLD.

## Approval sequence, with no guessed merge SHA

1. Review the final Protected source PR HEAD/tree/checksums/current focused CI and PR74 exact head/base/one-file diff. Both remain Draft and unmerged here. Source publication can be considered separately from actual UAT compatibility; do not give unconditional Live PASS.
2. Any future merge authorization must explicitly name BOTH exact PR heads and bases above/current final Protected PR tuple, the normal merge operation, automatic existing checks, no unrelated commits, no PR48 merge, no environment modification. Do not merge as part of this WORK. If head/base changes, stop and re-review.
3. A merge result SHA cannot be honestly supplied before GitHub creates it. After separately approved normal merges, read back real main and Protected merge SHAs; assert the registration stub and all nineteen G1 file bytes/checksums equal the reviewed trees and no additional delta exists. Do not guess a merge SHA or replace normal Protected merge with a direct ref update.
4. Only then request fresh Owner privileged shell consent for that **actual final Protected SHA**, one fixed instance, one WSS session, the two fixed ARM GETs, selected six-file source payload, terminal-only settings, explicit unknown-prerequisite fail-closed boundary, ZERO SQL and300s/no retry. Exact line: `G1_D0_OBSERVATION_APPROVED <ACTUAL_FINAL_PROTECTED_SHA> g1-version-observation-v2.yml ONE_WSS ZERO_SQL`. This document is not that consent; do not post this as if Owner approved.
5. Guard source+worker checks exact Owner identity/actor/triggering actor/attempt1, ref, whole nineteen-added-path delta, fresh Issue41 exact-source consent, and a total of exactly ONE dispatch of the D0 workflow (the current run). A failed first source check or accidental stub dispatch still blocks reuse. Checks run again before token exchange. Dispatch only on Protected, never on main or a borrowed branch. No such dispatch is authorized here.

These rules require a post-merge exact-SHA consent record. A single pre-merge approval cannot truthfully contain a not-yet-existing merge SHA; do not fabricate one or weaken the guard to avoid a necessary final Owner decision. Owner is not asked to run commands, inspect SQL or choose implementation details.

Remaining actual P0: on-instance SCM identity/network/Python/proc/API invocation not attested; actual D0 execution under exact new privileged approval not done. These cannot be removed by more local tests. Subsequent Frontend/old-binary/PITR/SQL/flags evidence belongs to full G1, not this D0-only engineering delta. Business UAT stays HOLD; accepted technical RC/IT candidate stays READY.
