# A-stage management GET channel — NONDEPLOY proposal / NOT AUTHORIZED

Scope: only the execution-channel blocker. Archive rc-f29affb3-20261020 is ACCEPTED and is not retrieved, changed or reverified. PR49 f29affb3299828f45b1f131321779e8bea2f86a8, PR50 c12451700b619897afe0e4c5dc9ae2862e259d2f and all application/SQL/workflows remain unchanged. No Azure call/login, SQL, CI dispatch, merge or live operation occurred in preparation.

## Exact proposed access path / unresolved binding

Existing source `.github/workflows/azure-oidc-uat-smoke.yml` uses environment `uat`, `vars.AZURE_CLIENT_ID`, `vars.AZURE_TENANT_ID`, `vars.AZURE_SUBSCRIPTION_ID`, and id-token:write. The accepted C1 used the reviewed parent/private R3 workflow and the same environment values, with successful actual authentication at that historical source. Those are management identities, not the API DefaultConnection or migration principal.

The collector accepts an ALREADY issued ARM bearer through A_STAGE_ARM_TOKEN and an already approved read-only GitHub bearer through A_STAGE_GITHUB_READ_TOKEN. It does not acquire tokens, invoke azure/login, emit environment values or create a privileged workflow. The bearer claim check binds tenant/client/audience/expiry before requests; this is not local signature validation. Successful ARM authentication is still necessary. No token or identity is output. An authentication failure stops without retries.

The existing OIDC host must be separately reviewed to hand off the existing identity safely, mask identifiers/claims before login, preserve the exact approved workflow/subject trust, and supply reviewed subscription equality. Changing job_workflow_ref cannot be assumed trusted. This batch does NOT modify that host or federation. A new privilege or federation grant is not a permitted workaround. The host, identity values, exact full resource IDs, GitHub Pages read permission and current authenticated availability remain UNKNOWN. No claim is made that this standalone collector closes those live prerequisites.

## Frozen GET allowlist

All ARM paths start with the privately reviewed `/subscriptions/{approved}/resourceGroups/rg-fieldvisit-uat/providers/`. The actual subscription must equal both the existing uat value and a separately reviewed A_STAGE_APPROVED_SUBSCRIPTION; never supplied as an arbitrary dispatch input or printed.

1. Microsoft.Web/sites/api-fieldvisit-uat?api-version=2025-03-01
2. Microsoft.Web/sites/api-fieldvisit-uat/deployments?api-version=2025-03-01
3. Microsoft.Sql/servers/sql-fieldvisit-jpe-uat/databases/db-fieldvisit-uat?api-version=2023-08-01
4. Same database /backupShortTermRetentionPolicies/default?api-version=2023-08-01
5. https://api.github.com/repos/Terry4410/field-visit-mileage-system/pages

Five normal GETs, globally at most20 including every redirect. Redirects can only remain on that exact list and same origin, with token selected by origin; zero arbitrary URLs. No retries, pagination, SDK, CLI, proxy credentials, discovery or business/health/settings/backup-storage endpoints. No requests for archive bytes or expiring Actions artifact metadata. Every request has at most10 seconds; total monotonic600 seconds; response at most2MiB. Authentication acquisition is NOT hidden in the budget: absent a preissued token and separately reviewed host, execution is HOLD. Requests made by that host, including authentication-related GETs, must also fit the owner's whole-operation budget; this code alone cannot certify external-host network behavior.

### Accepted archive evidence binding (offline, not a new archive verification)

The source/digest prerequisite is the exact frozen OWNER_ACCEPTED_PRIVATE_ARCHIVE record: repository Terry4410/field-visit-rc-archive-private, release rc-f29affb3-20261020, source f29affb3299828f45b1f131321779e8bea2f86a8, original run39357993766/artifact12236590943, size68631380, SHA256 6a604b212e88fb6039c449f9a6701d8aa0c68d7c8feec9e7b1e2600a4ddedb7b. The historical run/artifact IDs are provenance only, never live dependencies. No cross-repository token, read permission, metadata lookup or download is added. Exact source/tag/digest/decision mismatches or unknown fields STOP before network. RC_ARCHIVE_ACCEPTED_BINDING=PASS means the pinned accepted record matches, not a fresh check of remote visibility, bytes, immutability or retention.

Response validation rejects non200, wrong target, unknown state/package, truncation/pagination, duplicate JSON keys, secret-bearing bodies and invalid schema. Raw data are memory-only; exceptions/errors are suppressed. No raw metadata artifact. A failed operation prints only `A_STAGE=HOLD; Details=SUPPRESSED;` and discards partial results. Success emits only exact reviewed source/RC and fixed PASS/HOLD/UNKNOWN codes. Deployment metadata does not prove running binary bytes. Current/previous recovery artifact IDs are NOT known and are NOT invented or followed. Missing metadata yields UNKNOWN; malformed/unauthorized responses STOP. PITR policy presence is not a restore/recovery proof. A Paused/Resuming database yields HOLD; no wake or SQL code exists.

## Future ONE execution decision — DRAFT / NOT AUTHORIZED

Before executable Owner approval: independent code/manifest audit, exact reviewed source/host binding without a Protected merge workaround, code SHA256, verified existing OIDC host/privacy/request-budget contract, private exact subscription/resource binding and existing GitHub read access. No missing values may be guessed or new grants requested implicitly. The current standalone entry still requires ONE_A_STAGE_MANAGEMENT_GET, approved source equal to GITHUB_SHA and protected ref; debug is forbidden. Those unchanged gates deliberately refuse the current work branch. No approved existing host can currently check out this reviewed collector under the trusted OIDC context with all required guards. This is an execution HOLD, not a request or authorization to merge code merely to enable it. A reviewed no-merge host/checkout binding would require a separate narrowly scoped design decision; it is not invented here. Environment strings are not human authorization or globally consumed one-time grants.

Owner's subsequent separate prompt may authorize ONE execution only after the host gaps below are resolved: the five GETs above, maximum20 including all host/collector redirects and authentication-related or other GETs, maximum10 minutes for the whole operation, no retries/pagination/discovery, no additional dispatch. Operator must check no prior/concurrent consumption and stop after one attempt. Re-running requires new authorization. No standing/recurring permission. No automatic workflow is added. If one-time consumption and host-wide accounting cannot be established, HOLD.

Expected evidence: fixed-resource match, operational availability flag, retention metadata validity, Pages target and offline accepted RC archive binding. API/frontend binary alignment, actual API SQL identity/effective permissions, matched recovery pair and G1 remain UNKNOWN/HOLD. No source stamp or deployment record is upgraded into binary proof.

### Existing-host feasibility — HOLD, no live authorization recommendation

Protected source inspected: 4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc.

| Existing host | Demonstrated limitation |
|---|---|
| azure-oidc-uat-smoke.yml | Trusted uat variable references/branch guard exist, but login/account/resource steps emit identifiers, do not invoke this collector, and do not meter authentication plus management GETs. Manual dispatch is repeatable. |
| azure-sql-uat-schema-fidelity-readonly.yml + r3-private-uat-execution.yml | Existing successful C1 establishes that historical path only. Private job includes readiness/wake and R3 SQL; no isolated A-stage invocation. Do not dispatch R3 to obtain a token or call its private job. |
| Collector work branch | Entry source/ref guards cannot run as-is from this branch. No independently reviewed no-merge source checkout/host binding or safe token handoff exists. Guards are not relaxed. |
| Authorization consumption | Existing concurrency cancel-in-progress:false serializes runs but permits a second run after the first. No atomic durable claim tied to a specific Owner grant, rerun rejection and concurrent-run exclusion has been established. A new permission or ad hoc write ledger is not assumed authorized. |

Missing dependencies: one reviewed already-trusted no-merge host/checkout/token handoff contract; authentication-inclusive HTTP accounting and global deadline; fixed private identity/subscription/resource binding; existing Pages read access; and a durable at-most-once consumption mechanism supported by already approved capabilities. No new federation/privileges/Protected merge is proposed as a shortcut. Until independently proven, the Owner-ready recommendation is HOLD: authorize no login, dispatch, management GET or SQL. These are exact execution dependencies, not live facts or defects in accepted C1/R3 behavior.

Costs: no SQL connection, serverless resume, keep-alive, configuration or billing action. Existing hosting/storage costs may continue independently; do not claim a zero bill. No approval of SQL, migration, deployments, flags, restores, credentials or Entra changes. Visitor/Leader/Admin test login unchanged. No backup/storage access to stfieldvisitbackup2608, fieldvisit-backup or $logs.

## Independent READ-ONLY audit

Recompute manifest; inspect exact four-file addition against Protected; confirm original privileged workflows/application/SQL unchanged. Inspect transport allowlist, redirect/auth isolation, timeout/body/JSON/secret limits, synthetic negatives, no hidden login/retry/SQL. Confirm offline tests prove code behavior only, not Azure permissions, trust, identity or actual runtime. Resolve the host/private binding/one-use accounting prerequisites before any Owner execution authorization. Archive acceptance and prior engineering are not reopened.
