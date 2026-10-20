# G1 minimal preflight — bounded delivery / HOLD

CURRENT DELTA: GAP-CLOSURE.md supersedes collection-phase and reference-coverage
statements below. Known catalog findings now permit independent connected-context
permissions, after per-result validation; prerequisite failures and errors STOP.
necessary-reference.json adds source-derived scoped facets/FK/index/CHECK inputs.

NOT AUTHORIZED FOR LIVE EXECUTION. `collect.ps1` is an UNEXECUTED future provider
candidate; its default only verifies files without SQL. V2's strict
allowlist principle is reused, not its presence-only compatibility conclusion.

## Exact source

Repository Terry4410/field-visit-mileage-system. PR50 remote HEAD verified
88c21921860888f531a0962867f916c307452bbc, Open/Draft; PR49 base is exactly
f29affb3299828f45b1f131321779e8bea2f86a8. Protected checkpoint remains the Owner's
4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc; not re-certified as today's runtime.
These are local review files, not a new commit or a claim of a changed PR50 tree.
Existing dirty preparation worktree is preserved. No original migration edits.
Remote PR50 parent: dbf5f2d1fa1bf7182918540f992c58629eac78ba.
Remote PR50 tree: 36e5e1b1359641e2dea2608de9fa25fc53245e86.

## Files / budget / contract

01-target.sql validates logical server, Azure SQL edition, exact DB, metadata
visibility and version-table readability prerequisites. 02-marker.sql reads only
VersionNumber/AppliedAt and requires exact numeric010 latest marker, unique010,
and no011–020 numeric marker. No proposed columns, aliases or migration ledger.
03-catalog.sql accepts pinned requirements.json; checks minimum column presence,
untrusted/disabled FK/CHECK and disabled/hypothetical indexes on scoped tables.
Missing relationships, wrong column facets, wrong definitions/index keys are NOT
certified by those flags. COMPLETE_FACET_EQUIVALENCE is always UNKNOWN.
04-permissions.sql checks current connection's effective sensitive write/DDL
permissions through HAS_PERMS_BY_NAME; NULL is UNKNOWN. It never performs a write.
Principal binding and complete operation permission matrix remain UNKNOWN.

Only `(code,status)` rows may leave each SQL command. Status is PASS/FAIL/UNKNOWN.
No raw metadata, definitions, historical counts/keys, identities or errors.
Historical compatibility remains UNKNOWN: this package deliberately does not read
business rows or claim an accepted snapshot invariant from table presence.
The validator rejects unexpected keys/codes/status, duplicates, extra groups,
wrong source/pins and any attempt to turn an unproven condition into PASS.
FAIL/UNKNOWN closes the overall G1 gate. This version cannot output G1 PASS.

Future reviewed provider budget: ONE connection, connect120s, four commands at
most30s each, total240s; no retry, pooling or connection resiliency. Check
host/db before Open, encrypted connection with certificate validation, no
redirected server override. Execute 01 first, require every identity prerequisite
PASS before02; marker must PASS before any remaining command. Current validator
stops after prerequisite non-PASS groups; known catalog FAIL/UNKNOWN permits only
the reviewed independent permission phase, with final HOLD. The collector requires a future
secure host-supplied CLOSED Microsoft.Data.SqlClient connection; no credential
resolver, Azure login or identity substitution is implemented. Manifest digest
must be supplied from independent review, not read as its own trust anchor.
Provider/time-bound tests and secure runtime-context binding are still missing.

## Case/dependency mapping (necessary conditions, not Business UAT execution)

| Frozen cases | Necessary SQL contracts | Dependency |
|---|---|---|
| VIS01–05 | Requests/Events, stage/publication, Create reservation, status/evidence CHECKs, unique pending/decision indexes |011/012/014/015/017/018|
| VIS06; LEAD06; SEC01 | Users/HR/team/leader tables and effective application access, revocation runtime guards |010 plus app authorization; schema alone insufficient|
| LEAD01–05; ADMIN01–04 | Independent Leader review FK/checks/decision key, stage evidence and publication |014/015/016/017/018|
| XLS01–05 | Import batches/items, requests, reservation, current publication RowVersion and canonical statuses |014/017/018/020|
| TRIP01 | Trips/stops, published location filtering and mileage runtime contracts |010 plus014/017/018 and runtime flags|
| TRIP02 | Snapshot/stops immutable projection and FK contracts |010 history; row consistency not inferred|
| SEC02 | Atomic publication/reservation, unique decision/index contracts |011/012/014/016/017/018; real concurrency evidence reused, not rerun|

019 Merge is not automatically mandatory for these25 frozen cases; broader UAT
may require it.013 remains reserved. Dependencies describe required semantics,
not Owner-approved registry IDs or permission to run candidates.

MIGRATION_REQUIRED only when trusted visibility proves a required contract absent
or incompatible AND independently reviewed promotion mapping supplies the remedy.
MIGRATION_NOT_REQUIRED requires complete independently reviewed facets/history/
permissions to pass, not markers or presence alone. Partial/unexpected objects,
unknown visibility, absent reference or unproven principal => UNKNOWN/STOP, never
automatic migration, skip, repair, checksum insertion or retry.

## Actual API principal — UNKNOWN

RC DependencyInjection.cs uses GetConnectionString("DefaultConnection"). This
does not prove the currently deployed binary uses those bytes or that its
effective runtime configuration selects a specific SQL identity.

Exact missing restricted evidence, obtainable only under later live authority:
1. Slot/deployment/binary identity and effective configuration provider precedence
   resolving ConnectionStrings:DefaultConnection (including App Service override
   and any Key Vault reference resolution), without exporting the secret value.
2. Authentication mode and credential source: SQL login, service principal or
   selected managed identity. App Service system identity merely being enabled
   is not evidence it is selected. GitHub OIDC management identity and migration
   identity are distinct and must never be substituted.
3. An independently reviewed in-memory binding between that effective connection
   and the database principal SID/current identity using the SAME application
   authentication context. Do not print SID/name/token/string; compare internally
   and emit only match=true/false. No EXECUTE AS, grants or identity modification.
4. A minimum operation-to-object permission matrix traced to the actually
   deployed application's relevant25 paths (SELECT/INSERT/UPDATE/DELETE and
   column/object grants/ownership chaining as applicable). The scalar database
   permission checks alone cannot establish every effective DDL escalation path.

Use the approved secure runtime credential resolver/context if available. No new
API endpoint, credential extraction or management-login impersonation is proposed.
If that context cannot be safely reused, principal remains UNKNOWN; no claim that
management metadata alone proves effective SQL permissions. Owner need not debug
SQL or disclose credentials. Existing test-account authentication is unchanged.

## Exact missing preparation / verification

requirements.json is an EXPLICIT MINIMUM subset, extracted from RC candidate
CREATE column declarations plus named010 key columns, with provenance.json.
It is not a complete EF schema reference. Full column facets, required defaults,
exact FK mappings/actions, index keys/filter/uniqueness, CHECK/trigger definitions
and historical invariant projections still need a reviewed scoped reference
before any compatibility PASS can be possible. No historical1.5 baseline is
invented. No Full Inventory or ledger is needed to construct that scoped reference.

Local Python contract tests run; PowerShell/provider/SQL syntax and permissions
NOT EXECUTED (no pwsh, dotnet or disposable SQL/container tool available). No CI
dispatch or push: existing PR50 push trigger would run its migration suite, not
this minimum offline validation. No passing SQL evidence is invented.

Remaining non-SQL G1 blockers: deployed API is not certified as PR49; exact RC
binary/source alignment, frontend compiled flags/activation artifact, usable
matched recovery path, test accounts/provider restrictions and independent runtime
review remain separate. Migration PR50 remains HOLD. No Business UAT GO.

## ONE future Owner decision sheet — DRAFT / NOT AUTHORIZED

Prerequisites: independent audit of exact package hashes; complete scoped facet/
history and permission reference; real disposable provider tests; reviewed live
collector; proven secure API context binding. Missing prerequisite => no dispatch.

Proposed one-time operation: exact pinned source/SQL/collector, server
sql-fieldvisit-jpe-uat.database.windows.net and db-fieldvisit-uat only, one
bounded application-context connection and at most four read-only commands with
the120/30/240s bounds above. No retries, substitutes or redispatch. Owner separately
selects whether this one connection may attempt cold start of Paused serverless
compute. If not selected, require management-plane Online evidence or STOP.
If selected, that SAME single Open may wake compute; no extra warm-up connection.
40613/timeout consumes the attempt and STOPs. No auto-pause/SKU/billing changes.
May consume remaining free credits and incur overage plus Online tail until
existing auto-pause; exact cost unproven and not capped by command timeout.
Owner must accept that exposure explicitly; a240s process cap cannot cap billing.

No migration, write/grant/restore, deploy, feature activation or Full Inventory.
Sanitized allowlisted flags/source/hashes only in logs and restricted evidence;
no public raw metadata/permissions/identity payloads. Retain only sanitized dossier
7days pending Owner retention decision; do not alter existing logs/backups.
Any target/hash/binding/permission/schema mismatch, UNKNOWN, provider error or
privacy failure => STOP. No further action without a new authorization.

## Independent read-only audit

Recompute every SHA256; verify source tuple and original candidate bytes; inspect
SQL token/data-read scope and exact target; reject minimum-presence flags as full
compatibility; inspect principal proof boundary, output allowlist and negative
tests. Confirm collector default is nonexecuting and provider tests NOT EXECUTED. No live
execution approval follows from this package. Final: HOLD_WITH_EXACT_MISSING_EVIDENCE.
