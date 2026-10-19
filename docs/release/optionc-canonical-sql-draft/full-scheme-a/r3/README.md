# R3 staged UAT diagnostic — NONDEPLOY audit package

Authorization: NONDEPLOY_ONLY. No UAT SQL, dispatch, merge, deployment, identity change or Business UAT GO is authorized. Authentication remains Demo for controlled Owner-led UAT; Entra migration remains deferred.

## Root cause and minimum correction

R2 run 38859687847 reported UnknownSqlFailure / ProviderCode=Suppressed. The underlying SQL fault is **unresolved**; neither connection success nor server-side execution is proven. PR #38 already fixed bounded classification and suppression. It is retained byte-for-byte, alongside its PowerShell/DataSet and real-provider negative tests, inventory SQL and validator pins. R3 fills the missing staged tooling; it does not claim to fix a known UAT schema defect.

Branch: `work/r3-staged-diagnostic-20261019`, created directly from exact Protected `b1a507c90c577fb2c0cdd6a98ef33481d3a5f790` (PR #38 merge, head `8b6ed98a8e92b100df09eeb78da27b6a4c899070`). Accepted A+C `138cd8352ab180254ed83bce4a77acb3fdfc948f` is not merged or modified by this proposal. The registration stub on main `7264d8d7ed15d10e9294325e2416bbc08fdc314b` was inspected and remains unchanged.

## Execution contract

| Stage | Query | PASS means | Does not prove |
|---|---|---|---|
| IdentityVersion | Exact SERVERPROPERTY and DB_NAME, unique 010 marker, latest-by-AppliedAt marker | Reviewed target and predecessor readable | Historical migration provenance |
| Catalog | Historical key column visibility; aggregate access to tables, columns/types, defaults, indexes, FK columns and checks | Required diagnostic catalog queries work and required keys are visible | Full schema correctness/completeness or comparison |
| HistoricalAccess | Four HAS_PERMS_BY_NAME SELECT checks | Historical SELECT available at this instant | Immutable permissions or data compatibility |
| HistoricalAggregates | Four COUNT_BIG/MAX queries reduced to one PASS bit | Historical aggregate reads work at this instant | Historical row equivalence, subsidy or business correctness |

Every stage is a distinct Invoke-Sqlcmd call; target and version guards repeat in every file. Connection timeout 10 seconds; query timeout 30 seconds per stage; no diagnostic retries. A failure emits one sanitized STOP and terminates the loop. Catalog visibility is a limited prerequisite, not a full VIEW DEFINITION/fidelity certification. Independent connections are not a cross-stage consistent snapshot; concurrent schema/permission changes remain a risk and repeated guards only bound it.

Production wrapper accepts only fixed UAT connection keys and fixed server/database. Expected logical server is exactly `sql-fieldvisit-jpe-uat`; FQDN connection target is exactly `sql-fieldvisit-jpe-uat.database.windows.net`; database exactly `db-fieldvisit-uat`. SQLCMD substitutions are restricted identifier characters, generated internally; no user SQL inputs. Disposable mode requires GitHub Actions and localhost,1433/R3Fixture and accepts no AccessToken. It is invoked only by nondeploy CI. No UAT invocation uses fixture mode.

Only stage, PASS/STOP, approved classification/provider code, source SHA and SQL SHA-256 are emitted. No target identity, aggregate counts/keys, row values, raw errors, stack, credential or query text is logged/exported. Preflight rejection uses zero hashes when reviewed provenance is unavailable; zero hashes never mean executed reviewed SQL. The existing classifier remains authoritative and unsupported errors remain suppressed.

Workflow keeps the existing identity and protected ref/repository guard. OIDC permission is restricted to the UAT job after guard; hash checks run before Azure login. Manifest pins all four SQL files, runner and unchanged classifier; its digest is pinned in the workflow. Existing full inventory/validator hash checks remain, but the workflow no longer invokes or exports the full inventory. There is no path from four PASS results to full inventory. Any future full inventory needs a new reviewed change and separate explicit Owner authorization, including successful prerequisites.

## Changed files and impact

| Files | Impact |
|---|---|
| Four SQL files in this directory | SELECT/DECLARE/IF/THROW only; no DDL/DML, identities or personnel rows |
| scripts/ci/uat_schema_r3.ps1 and .hashes.json | Sequential fail-closed orchestration and immutable file contract |
| scripts/ci/test_uat_schema_r3.ps1 | Runner-local SQL 2022 fixture, real-provider negatives and call instrumentation |
| scripts/ci/test_uat_schema_r3_contract.py | Static safety, pins, original-byte preservation |
| azure-sql-uat-schema-fidelity-readonly.yml | Replace automatic full inventory invocation with staged-only runner; keep workflow identity |
| uat-schema-fidelity-proposal-nondeploy.yml | Isolated branch trigger, integrated tests and exact-source artifact; contents:read only |
| test_uat_schema_fidelity_proposal.py | Adapt workflow-facing wiring assertions to new staged runner; retain all 11 accepted test cases |

No application, RBAC, database migrations, main, Protected, accepted A/C, authentication or privileges are changed. Test DDL/DML and restricted principal creation occur only in disposable localhost SQL Server, never Azure. Test-only mutated/rehashed SQL copies exercise syntax, timeout and unsupported provider behavior; their results are not substitutes for the first all-four-stages positive case executing original reviewed bytes. The final connection case stops only the uniquely identified runner-local SQL 2022 container. Fixture errors are generically suppressed.

## Integrated validation and evidence binding

CI runs 11 existing Python contracts + 6 new contracts; 11 existing DataSet/classifier cases; 3 existing real Invoke-Sqlcmd negatives; and 20 R3 real disposable cases (one four-stage positive, 19 negatives). Counts are expectations until successful exact-SHA CI and logs are independently checked. Instrumentation records each real-provider stage invocation and requires exact expected count/order and PASS prefix; all actual results/errors come from SqlServer/Invoke-Sqlcmd. No later stage or inventory can execute after STOP. Unknown wrapper tests remain in the accepted suite.

The artifact `r3-nondeploy-<exact SHA>-<run>-<attempt>` includes exact source archive, manifest, commit/parent/tree and impact. GitHub job logs hold the test results. Delivery PR binds exact SHA, run, totals, artifact ID and GitHub archive SHA-256 after CI; these are not pre-invented in source. Independent audit must retrieve both logs and archive, checking GitHub artifact digest rather than substituting the inner source ZIP digest. Unchanged Protected/main refs are checked at delivery.

## UAT 010 normalized metadata alternative — NONEXECUTING assessment

A future separately authorized normalized 010 inventory can provide an **observed current UAT schema reference**, captured from a specific UAT/time/source/SQL hash. It can support comparison of disposable rehearsal output with current UAT, including columns, constraints, indexes, foreign keys and schema markers. R3 itself exports no normalized inventory.

Protected migration 001–010 requirements remain independent normative inputs, including intended changes and backward compatibility. Independent historical 1.5 reconstruction requires provenance of an approved original schema/backup/migration baseline and reproduction evidence. No such approved baseline is created or asserted here. Observed UAT 010 does not independently prove reconstruction of 1.5: UAT may contain manual changes, omitted history, grants, renamed/legacy objects, incomplete reference data, or applied markers that do not prove all prior steps. Metadata cannot prove row lineage, snapshots, ownership/team authorization at historical dates, preservation of historical references, or business calculations. These remain HOLD until separately evidenced. Do not relabel observed 010 as approved historical 1.5.

## Independent READ-ONLY delta audit checklist

- Verify exact Protected baseline, proposal parent chain/tree, changed-file scope and unmodified accepted classifier/inventory/tests; no A/C or auth changes.
- Read all four SQL files; verify SELECT-only scope, exact target/010 guards, required keys and aggregate-only access. Check catalog PASS limitations.
- Verify manifest digest in workflow and each reviewed file digest, exact checkout SHA, isolated CI credentials/permissions, Protected guard and job-scoped OIDC.
- Inspect first real positive case and all 19 negatives: wrong targets, version/columns/tables, SELECT denial, syntax, unknown provider, timeout/connection, tampering, extra/empty results. Verify call counts/order stop at each failing stage.
- Confirm original 11 DataSet and 3 real-provider negative tests still pass; bounded classifier and unsupported suppression unchanged.
- Inspect output contract and absence of identities, counts, keys, secrets/raw SQL errors; artifact contains only repository source/provenance, no SQL results.
- Check no automatic inventory invocation or UAT dispatch, and no main/Protected/other-lane changes. Inspect live CI exact SHA/artifact digest and remaining HOLDs.
- Do not issue UAT_SCHEMA_FIDELITY_PASS, historical-1.5 reconstruction PASS or Business UAT GO from these tests.

## Future C1 execution authorization draft — NOT AUTHORIZED

This draft is not a request to execute now. Only after independent audit, separately authorized merge, and post-merge exact-SHA/pin verification, Owner may issue:

> Authorize ONE R3 diagnostic run from post-uat/v1.8.0 at [POST-MERGE EXACT SHA], workflow azure-sql-uat-schema-fidelity-readonly.yml, reviewed manifest SHA-256 [EXACT MANIFEST HASH], reviewed four per-stage SQL hashes [LIST FROM MANIFEST]. Target only sql-fieldvisit-jpe-uat.database.windows.net / db-fieldvisit-uat, predecessor 1.8.0-010. Permit IdentityVersion → Catalog → HistoricalAccess → HistoricalAggregates only, one invocation per stage, next only after PASS. Stop on any failure; no retries. Full Inventory is EXCLUDED. No DDL/DML, grants, migrations, restore, deploy, flags or Business UAT GO. Return sanitized stage evidence bound to source/run/hash only. This is one-run C1 authorization, not standing approval.

Remaining HOLDs: independent audit; separate exact-head Protected merge approval; post-merge validation; separate C1 authorization; actual UAT diagnostic outcome; full inventory/comparison and historical baseline provenance; remaining data/history compatibility and Business UAT decisions.
