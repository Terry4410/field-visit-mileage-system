# PR50 minimal preflight delta — HOLD_WITH_EXACT_MISSING_EVIDENCE

## Scope and exact source

PR50 remote Open/Draft HEAD88c21921860888f531a0962867f916c307452bbc verified.
Parent dbf5f2d1fa1bf7182918540f992c58629eac78ba;
tree36e5e1b1359641e2dea2608de9fa25fc53245e86. RC remains
f29affb3299828f45b1f131321779e8bea2f86a8. Protected checkpoint
4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc is not today's deployed binary proof.
Original ZIP5fb75af3c88169ddf3f7c7865fa30abbc6180f190e4789bb74130c0a835fd70a
verified unchanged. No new branch, PR, commit, push, workflow dispatch or GitHub write.
The delivery is an uncommitted local review delta; no new commit SHA is invented.

## Source-derived necessary-contract matrix

necessary-reference.json has22 exact input SHA256s: frozen candidate011/012/014–020,
source001–010 Up files, EF AppDbContext, historical1.5 Verify and frozen25 case spec.
derive_reference.py reproducibly extracts candidate declarations without executing
them. Final named CHECK replacements override prior definitions in migration order.
Candidates are source contracts, not approved UAT execution files or observed UAT.

| Contract | Source-verified expectation | Proposed collection / remaining proof |
|---|---|---|
| Numeric010 predecessor | VersionNumber/AppliedAt; latest010, unique010, no011–020 numeric markers | Existing fixed target/marker SQL; actual DB UNKNOWN |
| Candidate columns |86 declared columns including optional019; type, byte length, precision/scale, nullability, identity | SCOPED_COLUMN_FACETS compares necessary non019 facets; defaults/computed/PK/trigger/full010 reconciliation incomplete |
| CHECK definitions |35 final named definitions, later source revision retained | Missing/disabled/untrusted FAIL; byte-exact definition PASS; SQL Server rendering differences UNKNOWN, never normalized into a guessed PASS |
| FK relationships |19 named mappings, ordered columns, parent/reference, delete/update actions | Non019 mappings and enabled/trusted state compared; source010 relationship completeness UNKNOWN |
| Indexes |15 explicit indexes, ordered key names, uniqueness and filter source text | Missing/key/uniqueness/disabled drift FAIL; filter text mismatch UNKNOWN; implicit PK/other010 indexes still need reconciliation |
| Snapshot model | EF unique(VisitTripId,SnapshotVersion); unique(SnapshotId,StopSequence); snapshot-stop cascade; route-attempt NoAction; decimal10,2/12,2; varbinary32 approval basis | Exact EF source pinned; not all010 model properties/constraints materialized into SQL reference; UNKNOWN |
| Historical integrity | Accepted regression checks before/after four-table Trip/Stop/Snapshot/Stop fingerprint equality and Version1 preservation | Synthetic accepted evidence reused, not rerun; no observed UAT historical values, baselines or invariants invented; UNKNOWN |

Mandatory25 subset excludes019:74 column facets,30 CHECK definitions,18 FK
mappings and15 explicit indexes. These counts describe source declarations only.

Source-derived definition strings are restricted review inputs, not runtime log
payloads. This artifact contains repository source, not real UAT catalog data.
019 Merge is recorded for provenance but excluded from mandatory25 scope in SQL.
013 remains reserved. No Full Inventory, proposed ledger/checksum fields or aliases
are prerequisites. The source1.5 Verify is not an original CREATE baseline.

011/012/014/015/016/017/018 support the frozen request/review/publication paths;
020 supports canonical Excel statuses.010 supplies base trip/HR/team/history.
These dependencies are semantic requirements, not permission to run migration IDs.
MIGRATION_REQUIRED requires observed incompatible/absent necessary contracts AND
reviewed remedy mapping. All exact contracts already compatible => migration may
be unnecessary, but only after remaining full scoped equivalence/permissions
proof. Mixed/partial/unknown states => HOLD; no automatic repair, skip or replay.

## Corrected independent collection vs release decision

1.01-target must be entirely PASS (fixed logical server/db/edition, visibility,
  version shape). Any FAIL/UNKNOWN stops before02.
2.02-marker must PASS. Otherwise stops before catalog/permissions.
3.03-catalog returns reviewed evidence. Known FAIL/UNKNOWN preserves overall
  HOLD but permits04, which is an independent read of connected-principal rights.
4.04-permissions labels are CONNECTED-PRINCIPAL-ONLY. It cannot establish deployed
  API identity. API_PRINCIPAL_BINDING stays UNKNOWN.

Every result is validated BEFORE any later command. Unexpected code/column/row/
result-set, malformed output, provider exception, timeout or validator failure
stops immediately. No catch-and-continue; no retries. Only an all-PASS mandatory
decision could release G1, and fixed UNPROVEN flags prevent that in this package.
Permission failures/unknowns never convert catalog findings to PASS or vice versa.
An incomplete successful prefix is not accepted as final evidence.

New tests prove offline phase decisions and rejection only. They do NOT prove
PowerShell control flow, provider result-set/parameter behavior or SQL execution.
The accepted14-case evidence is retained. Only its one affected final-completeness
case is updated to four groups and rerun, alongside the new targeted tests.

## Actual execution availability / exact blockers

NOT EXECUTED: PowerShell collector and SQL2022 positive/negative scenarios
(wrong target, missing objects, CHECK/index drift, parameter binding, timeout,
provider errors, cleanup). No pwsh, dotnet, docker, podman or sqlcmd executable is
available in this workspace; no local disposable SQL instance/container runtime
is supplied. Existing GitHub full migration jobs are not a ready minimal collector
validation job. No broad CI rerun is substituted, no cloud/UAT fallback is used.

Minimum missing execution environment: isolated SQL Server2022 Developer container
on loopback, ephemeral synthetic database/credentials; PowerShell7; pinned
Microsoft.Data.SqlClient runtime (existing SQL module may supply it); Python3.
No Azure identity/environment or UAT secrets. Then run just the above collector
cases. This is a remaining verification requirement, not an Owner software task.

Other remaining preparation: complete base010/EF property/PK/default/trigger and
history scoped reference reconciliation, SQL Server CHECK/filter rendering
verification, and API-context binding. Therefore preparation is still HOLD.

## Actual API principal: UNKNOWN; minimal read-only evidence plan

RC GetConnectionString("DefaultConnection") is source proof only. Bind evidence
to the deployed slot/binary/source and effective configuration-provider precedence.
Inspect only nonsecret provider/key/auth-mode metadata and Key Vault reference
resolution status under future authority; do not fetch/export connection values.
Record whether effective authentication is SQL credential, managed identity or
service principal. Enabled App Service identity alone does not prove selection.

Use an existing approved in-memory application connection factory/context if
available. Reuse the SAME authentication selection; compare resolved database
principal SID/current execution identity internally with reviewed binding evidence,
emitting match only, never names/SIDs/token/string. HAS_PERMS_BY_NAME then describes
that connected context. No EXECUTE AS, probes that write, or privilege changes.
If existing secure context reuse cannot be established without new secret access
or endpoint development, STOP with UNKNOWN; do not improvise a resolver.
Map required SELECT/DML and denied SchemaVersions/DDL paths to the actual deployed
application before claiming full operation permission adequacy. Current scalar
checks alone do not prove every escalation route or ownership-chain behavior.
GitHub OIDC management and migration/admin identities are never substitutes.
Existing Visitor/Leader/Admin test login remains unchanged.

## Single future authorization proposal — DRAFT / NOT AUTHORIZED

Only after source/reference/collector independent audit AND actual disposable
provider tests pass, approve exact package manifest/SQL/collector hashes and one
guarded existing-application-context run on
sql-fieldvisit-jpe-uat.database.windows.net / db-fieldvisit-uat.
ONE connection; connect120s, up to4 commands30s each,240s SQL budget; no retries.
Process cleanup/validator scheduling is not a hard wall-clock or billing cap.
No SQL text substitution or alternate identity. Only sanitized flags/hashes.

Owner must explicitly choose whether that SAME one Open may wake the Paused
database. Without cold-start approval require management-plane Online or STOP.
With approval acknowledge free-credit consumption, possible overage and continued
Online billing until existing auto-pause; timeout does not cap Azure charges.
No separate warm-up probe.40613/timeout consumes attempt and STOPs.

Wrong target/source/hash/marker/visibility, malformed results, unexpected provider
error or privacy defect STOPs collection. Known independent evidence FAIL/UNKNOWN
allows only the reviewed remaining read-only phase, never release/deploy/migration.
No other SQL, migration, grants, restore, Full Inventory, deploy, flags or changes.
Sanitized evidence only; restricted repository readers; proposed retention7days,
pending Owner approval. Existing backups/logs/storage untouched.

## Independent CHAT audit

Verify original artifact and current source tuple; new file hashes; derive all22
inputs from frozen RC; inspect exact facets/FK/index/check extraction and known
coverage holes. Confirm stage0/1 STOP, stage2 known findings allow only independent
stage3, immediate malformed/error STOP and final HOLD. Inspect tests as offline
only; insist on missing real provider evidence and actual API binding. No live
authorization or Business UAT GO follows. No further WORK starts automatically.
