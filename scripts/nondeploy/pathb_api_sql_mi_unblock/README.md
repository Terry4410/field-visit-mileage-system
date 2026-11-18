# Path B API Managed Identity SQL access — NONDEPLOY remediation candidate

Canonical hub: Issue #41. **No Azure/SQL/Token/Blob access is performed by this folder.** It does not reuse any one-use G1/Recovery approval, Blob Claim, legacy launcher, or frozen SQL installation layer.

## Evidence retained

- 2026-11-17 original G1: 14/14 SQL layers, 19/19 SQL calls, 69 tables, three schema versions as observed at postcheck; final durable acceptance receipt HOLD, never replay.
- Owner Path B App setting: `ConnectionStrings__DefaultConnection` points to exact Path B SQL DB with `Authentication=Active Directory Managed Identity`.
- Owner's existing Azure SQL Database `connection_failed_user_error`, **2026-11-17 21:45–22:00 Taipei**, split on `Error`: **18456 State 5 = 10**, **State 113 = 219**. These counts are DB-wide, not identity-attributed. State 113 fits Azure SQL DoSGuard. Stop repeated SQL/readiness connection attempts until the invalid identity is located.
- Current exact App system-assigned MI's SQL contained principal and group membership remain **UNKNOWN**. Pre-G1 `NO_MATCH` cannot be treated as current absence.

## Files

1. `catalog_readonly.sql` — exact-target, catalog-only, one batch. Requires both `VIEW DEFINITION` and `ALTER ANY USER` to avoid treating a restricted catalog as a complete inventory. Rejects unsupported external SID shapes (16-byte contained or 18-byte login-based with `AADE` suffix), and includes G1 constraint/trigger checks. Requires privately supplied exact current App system-assigned MI **Object ID and Client ID** in session context, Owner SQL metadata visibility, and fresh 69-table / 3-version evidence. It emits only safe categories. The expanded receipt also reports direct CONNECT GRANT and first-level database-role CONNECT GRANT/DENY catalog entries; these are observations, not an effective-permission computation. Nested database roles, transitive Entra groups, public-role permissions and actual MI login remain UNKNOWN. No group SID supplied by a caller is accepted as independent membership proof. A `NO_DIRECT_MATCH_VISIBLE` result **is not proof** that no Entra group grants access or that the State 5 client was this API.
2. `conditional_create_connect.sql` — **not authorized for live execution**. Uses the same complete-principal visibility/SID and constraint/trigger guards, and checks for duplicate SID mappings after the write before committing. A potential single contained-user `CREATE USER ... FROM EXTERNAL PROVIDER WITH OBJECT_ID` and `GRANT CONNECT` only, no business DML, role membership, schema edits or migration rights. It is deliberately fail-closed unless a separately checked authorization sets exact session gates. Its existence does not mean current missing principal was proven, and `CONNECT` alone is not enough for application CRUD.
3. `test_offline_contract.py` — no-network static security assertions and negative mutations; not a SQL2022 compiler, private connection test or evidence of a working Owner execution channel.

## Before any live action (outside this branch)

1. Proven **new** Owner Entra → existing App VNet → exact private Path B SQL connection, not consumed G1 launcher/Claim. Verify exact release identity, server, DB, TLS, Owner Entra admin, and fresh current App system-assigned identity (Object ID **and** Client ID). Token stays private. If channel not available: **HOLD**.
2. Separately approve one new, exact-SHA **SELECT-only** read. Verify current installed schema, direct user bindings and Entra group membership/rights. The SQL query cannot independently prove group membership; the Owner execution wrapper would need an independently authorized Microsoft Graph check. If any ambiguity: **HOLD**.
3. Independently review and explicitly approve the **specific** SQL DDL/grant before it can run. Do not infer permission from generic `continue` or a PR merge. The required session-context labels are *tripwires*, **not** proof of authorization or a substitute for identity/source checks.
4. If direct contained principal missing and no effective group path independently proven, execute **at most once**, fail closed. A timeout or lost acknowledgement leaves outcome **UNKNOWN**: never replay; do a separate read-only reconciliation. A transaction is attempted, but is **not** a guarantee of rollback after connection failure.
5. Recheck current principal metadata. Only **after bad-login cause addressed** and DoSGuard has cleared should a separately authorized **single** `/health/ready` check occur. After ready, separately review Path B object-scoped runtime CRUD permissions and actual Pilot UAT identities before 25 Business UAT cases.

**Excluded:** Azure/Token/SQL/Kudu/Blob operations; App Service settings; networking; public SQL; `db_owner`, `db_datawriter`, `db_ddladmin`; any frozen install SQL; GitHub merge/deploy. No blind broad GRANT; no retries under a previous one-shot grant. This is a source candidate only, not an executable channel. The official SQL Server/Azure SQL docs describe multiple SID models; external identity mapping should be verified live, not inferred from display name.

References: https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql ; https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview ; https://techcommunity.microsoft.com/blog/azuredbsupport/connections-rejected-by-dosguard-error-18456-state-113/3775487
