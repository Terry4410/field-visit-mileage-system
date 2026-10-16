# UAT Schema Inventory Failure — Privacy-Safe Diagnostic Proposal

Status: **NONDEPLOY PROPOSAL / UAT RE-RUN NOT AUTHORIZED**

Baseline: protected `post-uat/v1.8.0` SHA
`d691fba430ce80dd2048f84863b4628e1e6713d8`. Run `38845007030`
failed inside `Invoke-Sqlcmd` before normalization. The consumed dispatch is not
reusable, and the suppressed error does not establish a root cause.

## Static compatibility findings

The protected 001–010 `Up.sql` chain and the actual inventory SQL were reviewed,
not only the synthetic 010 fixture.

- `SchemaVersions.VersionNumber` and `AppliedAt` are used by the protected chain;
  the inventory's latest-version expression matches that ordering contract.
- `VisitTripId`, `VisitTripSnapshotId`, and `VisitTripSnapshotStopId` are used by
  protected Migration 001 preservation logic. `VisitTripStopId` is present in the
  protected EF model and domain model. The four inventory aggregate key names are
  therefore consistent with the protected source tree.
- Protected Migration 010 changes
  `CK_RouteCalculationAttempts_Reason`, records `1.8.0-010`, and requires the
  constraint to be enabled and trusted. The inventory catalog surfaces contain the
  facets needed by the existing comparator.
- The catalog fields used by the inventory belong to the SQL Server/Azure SQL
  catalog views named in the query. The script uses only declarations, target
  guards, metadata functions, `SELECT`, and `THROW`; no DDL or DML is introduced.
- The fixed server/database guard accepts the reviewed short Azure logical-server
  identity or its FQDN form. `Invoke-Sqlcmd -OutputAs DataSet`, access-token auth,
  and `ApplicationIntent ReadOnly` remain unchanged.

These checks found no repository-level column, syntax, target-guard, or invocation
incompatibility. They cannot prove the live UAT permission set, live object shape,
authentication state, connectivity, or which SQL provider number occurred in the
failed run.

## Access-surface comparison

| Surface | Earlier protected preflight | Schema inventory |
|---|---|---|
| `dbo.SchemaVersions` | Direct row read | Direct aggregate read |
| Historical trip tables | No direct read of the four-table set | `COUNT_BIG` and key `MAX` on all four |
| Optional business checks | Project/favorite/canonical tables when present | None |
| Catalog metadata | Selected scoped catalog rows | Full non-system table/column/index/FK/CHECK inventory |

The new inventory therefore requires direct `SELECT` on `VisitTrips`,
`VisitTripStops`, `VisitTripSnapshots`, and `VisitTripSnapshotStops`; success of the
earlier preflight does not prove that access. This is a narrowed hypothesis, not a
finding about the live identity.

## Minimal fail-closed correction

Before reading the four historical tables, the SQL now calls
`HAS_PERMS_BY_NAME(..., 'OBJECT', 'SELECT')` for each and emits custom error 51993
if any answer is false or unknown. No principal name or per-table denial is
reported.

The PowerShell catch path reads only the SQL exception's numeric provider code and
maps a strict allow-list to these stable categories:

| Category | Allow-listed signals |
|---|---|
| `TargetGuardRejected` | 51990, 51991 |
| `RequiredObjectMissing` | 51992 |
| `HistoricalSelectPermissionMissing` | 51993 |
| `SqlPermissionDenied` | 229, 230, 262 |
| `MissingObjectOrColumn` | 207, 208 |
| `SqlSyntaxOrCompatibility` | 102, 156, 319 |
| `AuthenticationOrDatabaseAccess` | 916, 18456, 4060 |
| `TimeoutOrTransient` | -2, 1205, 40197, 40501, 40613, 49918–49920 |
| `ConnectivityFailure` | bounded provider/network code allow-list |

Recognized codes may be printed only as the numeric `ProviderCode`. Every other SQL
number becomes `UnclassifiedSqlFailure` with `ProviderCode=Suppressed`. Raw message,
inner exception, SQL text, connection string, token, login identity, table-specific
denial, and business data remain suppressed.

## Approval boundary for any next execution

1. Independently review and approve the minimal Protected PR at its exact head and
   its new SQL/validator SHA-256 pins.
2. Separately approve merging that exact head to `post-uat/v1.8.0`.
3. After verifying the resulting Protected SHA and pins, separately approve one new
   SELECT-only dispatch. The authorization consumed by run `38845007030` cannot be
   reused.
4. If the next result is `UnclassifiedSqlFailure`, stop. Request a separately
   reviewed stepwise probe (target identity and `HAS_PERMS_BY_NAME` only, then
   catalog-only, then historical aggregates) instead of a blind retry.

None of these steps authorizes a UAT permission change, migration, restore,
deployment, feature flag, business UAT, or direct Protected/main/Staging update.
