# Path B — isolated Business UAT release contract

**Status: HOLD — no Azure resource, SQL migration, runtime flag or deployment has been executed.**

## Exact release target

| Component | Proposed identity | Binding |
|---|---|---|
| Resource group | Existing `rg-fieldvisit-uat` | reuse only after Owner authorizes Azure work |
| SQL server | `sql-fieldvisit-jpe-pathb-uat.database.windows.net` | new dedicated logical server; availability still to be checked |
| SQL database | `db-fieldvisit-pathb-uat` | 69-table NewEmpty profile only |
| API App Service | `api-fieldvisit-pathb-uat` | new isolated app; no old database connection |
| App Service plan | `asp-fieldvisit-pathb-uat` | separate or explicitly approved shared plan |
| Frontend | dedicated Azure Static Web App `swa-fieldvisit-pathb-uat` | separate host; must not overwrite existing GitHub Pages |
| Release profile | `PathBNewEmpty` | exact source/API/frontend/schema/principal/flags signed together |

Names are proposed deterministic names. Azure name availability and quota must be checked during the single approved provisioning window. No target can be changed by an environment override after approval.

## Install and activation gates

- Installer target `pathb-uat` binds the exact Path B SQL host/database, checks the target database is empty, requires SQL Engine Edition 5, and uses create-only scripts. The existing `uat` target remains fixed to the old Existing010 database.
- API startup refuses to serve without a `PathBNewEmpty` release contract and runs the signed SQL trust preflight before listening. Runtime attestation binds source SHA, API and frontend package hashes, 69-table schema hash, SQL principal SID/name, exact Path B host/database, TLS validation, and profile.
- Runtime trust denies the old UAT server, Production, unknown targets, wrong schema counts, wrong signatures, or a non-runtime principal with owner/DDL/broad-writer privileges.
- Path B UAT Option C execution requires `OPTIONC_EXECUTION_MODE=Uat`, signed activation, `OptionC__Candidate__Enabled=true`, `OptionC__Approval__Enabled=true`, worker enabled, `Providers__Route=GoogleMaps`, and a non-empty Google key supplied only through the private app setting. Missing values fail closed.
- Runtime SQL principal receives only the tested object-level grants; a separate one-time migration operator runs the create-only installer and then loses DDL privileges. No API Runtime membership in `db_owner` or `db_datawriter`.
- Frontend/API/schema manifests and checksums travel as one release package. Cleanup remains off (`UatAutomation:CleanupEnabled=false`).

## Google Maps acceptance

The frozen 25-case matrix retains VIS02 real Google Routes success and VIS03 real service-failure to explicit manual fallback. The default source configuration stays Mock and no API key is committed. Before live UAT, the Owner must approve a restricted UAT-only key and any Google Maps billing/quota settings. If a key is unavailable or a real route call cannot be performed, Business UAT remains NO-GO; no test case is removed.

## Matched recovery and deployment plan

1. Build API and frontend from the same PR #83 candidate SHA and produce SHA-256 manifests.
2. Install exact 69-table schema into the new Path B SQL database, apply object-level runtime ACL, then sign activation facts.
3. Enable only the Path B settings listed above; create separate Visitor/Leader/Admin demo test actors with unique nonproduction credentials.
4. Preserve API ZIP, frontend ZIP, SQL installer package, schema/ACL manifests, activation profile and timestamps under a private recovery store; take SQL backup/PITR point after install and before Business data.
5. Restore to an isolated secondary target, reapply exact API/frontend packages, run health/role/SQL smoke checks, and record all three component hashes. Only a matching API+frontend+database profile counts as recovery rehearsal.
6. Keep old Existing010 database and its 63 tables/26 markers untouched throughout.

## Stop / recovery criteria

Stop before schema work if the SQL target is nonempty, target identity differs, the candidate SHA or package hash differs, runtime privileges exceed the object-scoped whitelist, encryption/certificate validation fails, trigger/FK trust fails, or attestation/feature flags are absent. Do not redirect to the old database. A failed new Path B install is isolated; preserve the logs without secrets, restore from the before-UAT point, and do not begin Business UAT until a repeatable recovery check passes.
