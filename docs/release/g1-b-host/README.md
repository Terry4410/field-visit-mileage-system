# G1-B offline adapter candidate — HOLD / NOT LIVE AUTHORIZATION

Accepted base c12451700b619897afe0e4c5dc9ae2862e259d2f; RC
f29affb3299828f45b1f131321779e8bea2f86a8. Original G1 package remains byte-identical.
No accepted workflow, original migration, application, protected or main changes.
This branch adds NO live workflow. Its push job has contents:read, no environment,
no id-token, no Azure actions, no credentials and no SQL Open. Only the existing
pinned SqlServer module is installed for unopened-object compatibility tests.
No PR is required to run this branch's narrowly scoped push job.

## Files and boundary

- adapter.ps1: future dedicated-worker library accepts a CLOSED actual SqlClient
  object by reference in a local runspace. No serialization, token resolver,
  management login, warm-up or substitute identity. Import is inert. Explicit
  future authority and worker/context attestation are required; these parameters
  ARE NOT independent proof of Owner approval or actual SQL identity.
- supervisor.py: POSIX subprocess-group supervisor, hard ceiling240s, TERM before
  KILL, finite reap budget; discards worker stdout/stderr and accepts only four
  result enums. No retries. A future reviewed host must create the connection
  INSIDE that worker; this code cannot transfer a pre-created object across OS
  process boundaries. No such live worker is provided or approved in this delta.
- verify_inputs.py: verifies every accepted G1 file byte-for-byte against base,
  all manifest pins and the original Collector's single Open call site.
- test_supervisor.py:12 synthetic subprocess checks. Short bounds exercise the
  production supervisor; no mocks of subprocess termination or SQL/network calls.
- test_adapter.ps1:10 actual unopened SqlClient guard checks in push CI. No valid
  execution path or SQL connection is attempted; synthetic token is invalid and
  exists only in memory. Exceptions/logs are suppressed, fixed totals only.

## Safety and limitations — never label offline A complete from partial tests

Collector alone owns its one Open and four reviewed SQL stages. It preserves
120s connection/30s command limits, ConnectRetryCount0 and strict results. A hash
or guard STOP never falls through to Collector. Connection ownership transfers
only after type/context acceptance; adapter then attempts Dispose even if Open
fails. Invalid/unaccepted caller objects remain caller-owned. Runspace output and
errors are not forwarded. Successful evidence still means G1 HOLD, never G1 GO.

Cooperative BeginStop at220s is NOT proof a synchronous SQL call stops. A future
host must actually wire the adapter within the supervisor's reviewed worker.
The supervisor sends TERM before240s, then KILL and bounded reap; OS scheduling
is not a real-time guarantee. Worker finally may never run; connection/SQL server
termination and Azure billable tail are UNKNOWN. External forced kill is not
successful disposal. Cleanup failure is STOP. Untrusted code escaping a process
group is outside this supervisor's security model. No live host should be
accepted without its immutable script/interpreter/identity/environment binding.

A binding digest refers to independently reviewed nonsecret evidence, NEVER a
hash of a credential/token/low-entropy identity value. The future trusted host
must authenticate and verify the receipt; matching caller-supplied booleans and
digests alone do NOT prove trust. Actual current binding and permissions UNKNOWN.
The supervisor does not provide a token environment allowlist, consume an Owner
authorization, or prove at-most-once distributed dispatch. These remain host
integration prerequisites, not features silently inferred from this library.

## Verification coverage

| Required boundary | Evidence in this delta |
|---|---|
| Wrong source, server/database; missing/mismatched context or token |10 unopened-client guard cases in focused CI, result must be read back |
| Accepted SQL/Collector/validator hash preservation |Real byte/hash comparison; all accepted package files |
| Sensitive provider exception, forbidden output, malformed/oversized status |Synthetic actual child process; no raw output forwarded |
| Overall deadline, TERM ignored/forced KILL |Synthetic actual processes with shortened bound; no finally claim |
| Child exit/cleanup status, nonzero/missing result |Synthetic subprocess contract; not real connection disposal proof |
| Unexpected connection state, second Open, hidden provider retry |NOT EXECUTED as runtime fault injection; only accepted Collector static binding reused |
| Real Open failure/connection timeout/command timeout/Dispose failure |NOT EXECUTED; requires separately bounded disposable-provider tests and reviewed worker integration |
| SQL result-shape rejection |Accepted prior Collector evidence reused; no new adapter-through-Collector real execution proof |
| Same-object runspace execution, cancellation during provider calls |NOT EXECUTED; must be proven before A can PASS |

Only supervisor safety and tested unopened guards can pass. Overall A remains
HOLD until the above unexecuted adapter/provider boundaries and host wiring are
proved. Do not add a live dispatch or claim synthetic process tests prove them.

## Independent review and future authorization proposal

Review exact base/branch/head/tree and minimal diff; all accepted byte/hash pins;
absence of new live triggers/permissions; object ownership and local runspace exit
behavior; deadline/termination output suppression; actual focused CI cases and
NOT EXECUTED rows. Preserve accepted PR49/50 evidence. No broad suites rerun.

A offline safety: PARTIAL / HOLD. B existing OIDC trust: UNKNOWN. C actual
schema-diagnostic SQL principal/permissions: UNKNOWN. D API principal: UNKNOWN.
E wake/free-compute/overage/tail acceptance: NOT AUTHORIZED.

DRAFT — NOT AUTHORIZED: Only after A is independently complete and B/C privately
bound, fresh target/PITR metadata and secure evidence handling are verified,
approve exactly one fixed-target G1 schema-only run from exact reviewed source,
manifest and four SQL hashes. One Open,120/30/240s, no warm-up/retry/redispatch.
If cold start is not explicitly approved, require Online or STOP. Any approved
cold start may consume free credits, overage and residual Online billing until
existing auto-pause, not capped by process deadline. API identity remains separate.
No R3/Full Inventory/migration/deploy/restore/grant/flag changes. Any unexpected
target/source/result/provider/privacy event STOPs; known Catalog FAIL/UNKNOWN may
only proceed to accepted connected-principal permission collection; final HOLD.
No live authorization can be issued from this partial delta. Return to CHAT;
no automatic follow-on work.
