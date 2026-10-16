# UAT Schema Inventory R2 Unknown Failure — Nondeploy Recovery

Status: **DIAGNOSTICS-ONLY PROPOSAL / NO R3 UAT SQL AUTHORIZATION**

Protected baseline: `7f0cf0cb55467916fb0f57239301a8787fd5cb69`.
R2 run `38859687847` stopped inside the `Invoke-Sqlcmd` catch with
`UnknownSqlFailure / ProviderCode=Suppressed`. No normalized artifact exists, and
the result does not prove that any SQL statement reached UAT.

## Supported exception surface

SqlServer PowerShell 22.4.5.1 can surface a SQL engine failure through
`Microsoft.SqlServer.Management.PowerShell.SqlPowerShellSqlExecutionException`,
whose inner exception contains the SQL client provider exception. PowerShell can
add runtime, cmdlet, provider, method, action-preference, parent-error-record, or
reflection wrappers.

The proposed classifier performs a breadth-first inspection limited to three
wrapper edges. It traverses only exact allow-listed wrapper types and recognizes
only these terminal types:

- `Microsoft.Data.SqlClient.SqlException`
- `System.Data.SqlClient.SqlException`
- `System.TimeoutException`
- `System.Net.Sockets.SocketException`

An arbitrary wrapper is never traversed. A terminal SQL number outside the
existing allow-list remains `UnclassifiedSqlFailure / Suppressed`; an unknown
wrapper, an over-depth provider, a cycle, or a non-exception remains
`UnknownSqlFailure / Suppressed`. No exception/wrapper type, message, inner text,
stack trace, command, target, identity, token, connection string, or business data
is emitted.

## Isolated verification

Nondeploy CI uses a disposable SQL Server 2022 service on runner-local
`localhost,1433`, the pinned SqlServer 22.4.5.1 module, and SELECT/THROW-only cases.
It verifies the real `Invoke-Sqlcmd -AbortOnError -OutputSqlErrors $false -OutputAs
DataSet` surface for:

1. allow-listed custom `THROW` classification;
2. missing-object classification;
3. unsupported provider number suppression.

Separate synthetic tests cover approved wrapper chains, an `ErrorRecord`, an
arbitrary unknown wrapper, an over-depth chain, and raw detail suppression. The CI
workflow has `contents: read` only and no Azure login, OIDC, UAT environment, UAT
hostname, access token, artifact upload, migration, or deployment capability.

## Evaluated stepwise SELECT-only design

A future separately approved UAT diagnostic should replace one opaque full retry
with four fail-closed stages. Each stage is a distinct `Invoke-Sqlcmd` call and may
run only if the preceding stage passes. Logs and any artifact contain only the
allow-listed stage name, PASS/STOP, sanitized classification, reviewed source SHA,
and reviewed SQL SHA-256; query rows are validated in memory and not printed.

| Stage | Read-only scope | Safe conclusion on PASS | Stop meaning |
|---|---|---|---|
| `IdentityVersion` | `DB_NAME`, `SERVERPROPERTY`, `SchemaVersions` aggregate | Data-plane connection, fixed target and exact 010 marker are readable | Connection, target, object or version remains unresolved |
| `Catalog` | `sys.tables`, columns, types, defaults, indexes, FKs and CHECKs | Required catalog metadata is readable | Catalog visibility/query compatibility remains unresolved |
| `HistoricalAccess` | Four `HAS_PERMS_BY_NAME` checks | All four securables report SELECT available | SELECT or object/metadata availability remains unresolved; no per-table result |
| `HistoricalAggregates` | Four `COUNT_BIG`/key `MAX` aggregates | Historical aggregate query completes | Historical table shape/access remains unresolved |

The full seven-result-set inventory may run only after all four stages pass and
only when that full inventory is explicitly included in the new Owner
authorization. A failed stage stops immediately; no later probe and no full query
runs. This design needs separately reviewed, hash-pinned stage SQL before any UAT
dispatch.

## Independent approvals required

1. Control Tower reviews the exact diagnostics PR head, allow-list, wrapper-depth
   contract, hashes, and Exact-SHA nondeploy CI.
2. Owner separately approves merging that exact head to Protected.
3. Post-merge review verifies the resulting Protected SHA and immutable pins.
4. Owner separately approves one exact-SHA R3 SELECT-only diagnostic, explicitly
   listing permitted stages and whether the full inventory is included.

The R2 authorization is consumed and cannot be reused. No approval here includes
permission changes, UAT DDL/DML, migration, restore, deployment, feature flags,
Business UAT, or blind retries.
