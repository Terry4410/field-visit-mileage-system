# Field Visit Path B — SELECT22 Live Owner Parent execution gate (NONDEPLOY review)

> **HOLD — contract only.** No Azure/SSH/Storage/SQL operation is authorized by this document or its GitHub commit. A security-reviewed executable that binds a real authenticated Owner session to the exact runtime instance is **not yet available**. Do not manufacture PASS flags or execute the existing lower-level handoff directly.

## Frozen source and purpose

- Draft PR: [#89](https://github.com/Terry4410/field-visit-mileage-system/pull/89), nonprotected `work/pathb-connection-only-offline-plan-20261113`.
- Source checkpoint at drafting: `0c00132b627f62d2c2395a86a725d7a56b3aecab`; verified NONDEPLOY [CI 43538439399](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/43538439399): 142/142 PASS and frozen G1 Binding mismatch 0.
- Frozen **inner** SELECT22 ZIP: SHA-256 `ff63702ae5c5fdbd63ed0ecffe609463824ccb3f96fb7441c7a231638a588b8b`, 1,627,326 bytes. Do not confuse GitHub Actions artifact wrapper digest with the inner ZIP. Previously extracted and validated official SQL-client packages: **reuse, do not download/rebuild**.
- SELECT22 query SHA-256: `e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add`. Read-only schema evidence only; no migration, installer, DDL/DML or modification of the original fourteen SQL layers.
- Only existing Azure resources: `rg-fieldvisit-uat`, `api-fieldvisit-pathb-uat`, `sql-fieldvisit-jpe-pathb-uat.database.windows.net`, `db-fieldvisit-pathb-uat`, `stfvsc261026a/schema-evidence`.
- Candidate **unused** Blob key: `operator-once/pathb-select22-schema-readonly-once-20261113.json`. Never replace, append, rename, overwrite or delete it.

## Proven; do not repeat

Portal inspection (Owner-reported): Entra user Object ID locally matched frozen Owner hash; Subscription-inherited Owner role assignment visible; App Running, VNet integration, SQL Public Network Disabled, SQL PE Approved, minimum TLS 1.2, App overview reports instance count 1. This is **not** proof of the exact live worker identifier or Cloud Shell execution-session Owner identity.

The earlier Phase A management and ARM token identity approvals were each consumed and ended HOLD. They **must not** be replayed. Portal-only Owner OID matching does not override the HOLDs.

Source-only identity/claim adapters and 142 synthetic tests do **not** attest an Azure live authenticated Owner, cryptographic token provenance, current Blob data-plane rights or the exact one-time claim state. `pathb_select22_owner_parent_nondeploy.handoff_candidate` intentionally always HOLDs.

## Authorization gates — all independent; never infer one from another

| Gate | Necessary proof | Current verdict |
| --- | --- | --- |
| A — Owner session and target | Fresh authenticated session identity = exact frozen Owner OID, Tenant and Subscription, exact App and SQL resource ID; no unsupported Graph/ARM token-shape assumptions or self-asserted booleans. Separately review and approve **the actual method and bounded execution**. | **HOLD — no live-trust method approved** |
| B — Unique worker and pinned SSH | Exactly one live App instance with its exact ID; official SSH tunnel bound to that ID; strictly pinned ED25519 host fingerprint, private path, exact stage ZIP hash and no old stage collision. | **HOLD — not observed live** |
| C — Storage authorization | **New distinct Owner grant** for at most one Storage-audience Token, **one** conditional `If-None-Match: *` Blob PUT to the fixed key and **one** exact ETag/body GET. Data-plane permission must be verified; management-plane Owner role alone is not evidence of Blob write permission. | **NOT AUTHORIZED** |
| D — SELECT22 schema query | Only after Gate C readback PASS and fresh verification that the existing once-only SQL SELECT grant is usable: at most one SQL-audience Token and one fixed SELECT22 query against the exact private Path B database; sanitized status only. | **HOLD — no current release** |
| E — Business UAT release | Interpret real schema receipt. If incomplete/empty, obtain **separate** installation approval after exact scope review. Verify API/frontend activation, safe UAT identities and Google Routes real success/fallback. Run the existing 25 cases from PR #83; do not assume deployment or UAT GO from CI. | **0/25 NOT RUN** |

### Single-chain order *after* every earlier gate is independently proven and explicitly approved

1. Authenticate current Owner and exact Azure targets; confirm exact one live instance and pinned SSH host key. **On any unknown result: HOLD, zero Token, zero Blob, zero SQL.**
2. Verify local inner stage bytes and digest; prepare isolated receiver; receive exact `READY`. Any uncertain state: HOLD; never replay stage.
3. Only with new exact Storage write authorization and independently authenticated Storage token provider: one conditional PUT. Only HTTP 201 and valid ETag permit one GET. Require HTTP 200, same ETag and exact body. 412, 403, timeout, ambiguous response or failure => **terminal HOLD**, no alternate key, retry, overwrite or cleanup.
4. Only after successful durable claim: obtain one validated SQL token and send the fixed SQL-only frame over the same pinned session; read sanitized receipt. No direct token handoff or G1 installer.
5. Close session; record exact bounded status, counts and gate outcomes in PR #89/Issue #41. **Do not** log raw OIDs, SQL access tokens, connection strings, keys, user credentials, error text or business data.

### Required before any new operator instruction

- A source-reviewed **actual** Live Parent mechanism satisfying Gate A without old consumed attempts, explicit exact source SHA and independent safety tests; **current work has not supplied this**.
- A fresh Owner decision explicitly covering a specified gate; no general “continue” or previous SQL-only consent automatically authorizes SSH, Blob or SQL.
- If the reviewed mechanism cannot safely bind session identity, **stop rather than create another diagnostic version**.
- No new Azure resources or settings; no installer/SQL DDL/deployment/merge/IT handover under the above scope.

## UAT case reuse

Frozen existing 25-case matrix: PR #83 `docs/uat/BUSINESS-UAT-TEST-CASES-v1.8.0.md` — VIS 6, LEAD 6, ADMIN 4, XLS 5, TRIP 2, SEC 2. All NOT RUN. VIS02 and VIS03 require real Google Routes success and controlled fallback; TRIP02 requires physical iPhone. No duplication of documents or replacement with synthetic tests.

**Status:** NONDEPLOY CI PASS; Live Parent HOLD; Schema UNKNOWN; Business UAT 0/25.
