# v1.8.0 gated development plan

Status: preparation for review. Migration scripts exist but have not been executed. No Production deployment or main merge is authorized.

## Gate model

Each Epic follows: `Impact Analysis → Coding → Commit to post-uat/v1.8.0 → GitHub Actions Build/Test → Regression`. A failed gate is analyzed and fixed in a new commit; the next Epic cannot start until all required checks pass.

Database migrations are not part of a normal build job. When separately approved, a UAT migration job must be manual, branch/environment restricted, serialized, target-verified and blocked from application deployment until Up + Verify and historical fingerprints pass.

## Epic sequence and dependencies

| Epic | Scope | Migration dependency | Main regression gate | Start condition |
|---|---|---|---|---|
| A | Query and low-risk UX | None | Server-side filter/paging, date shortcuts, debounce, stale response, mobile modal | Already implemented on this branch; retain its successful Actions evidence and do not redo unless regression fails |
| B | Organization / People / Team | `1800_001`, then `1800_002` | Rehire/new employee number, leave/return/termination, Center/Team as-of, single Primary Team, multiple/delegated leaders, role/data scope | This preparation package approved; actual UAT migration access/role and execution separately approved |
| C | Deployment Site | `1800_003` | Team-visible sites, one Primary, start/end default, historical/backdated as-of and unchanged Snapshot | Epic B Actions + regression pass |
| D | Location / Project / Visit Type / Rate | `1800_004`, then `1800_005` | TaxId non-unique search, duplicate warning/no merge, Team Note scope/audit, batch preview, project soft delete, VisitDate+Vehicle rate | Epic C passes |
| E | Notification framework | `1800_006` | UAT Test mode/allowlist, leader/delegate recipients, opt-out, outbox transaction, delivery failure isolation and audit | Epic D passes; provider/sender decision may remain mocked |
| F | Google Maps Platform | `1800_007` | Routes + Geocoding + map, one ordered route, Car/Motorcycle mapping, failure/manual fallback, Snapshot retry, correction invalidation, no response persistence | Epic E passes; restricted UAT keys and TWO_WHEELER review required only for live smoke |
| G | Supervisor retirement | No new table planned; any persistent role-status change requires a reviewed versioned forward migration | UI/assignment removal while UserRole, DataScope, export, audit and historical transaction evidence remain readable | Epic F passes |

## Planned implementation boundaries

### Epic B

- Add domain entities and EF mappings for Center, Person, Employment, status, role, membership, leaders and delegation.
- Keep `Users`, `UserRoles`, `UserTeamScopes` and v1.7 assignment tables as a compatibility projection during cutover.
- Introduce as-of resolvers using VisitDate and optimistic concurrency; no name-based Person merge.
- Update People UI so it owns Person/Employment only; Team Membership remains Team Management's source of truth.

### Epic C

- Add Site master, Site relocation history, Team-Site and Employment-Site services/UI.
- Use VisitDate to list eligible sites. A missing/expired Primary is an explicit validation state, never an inferred replacement.
- New submission Snapshots freeze Center/Team/start/end Site and addresses; old snapshots are untouched.

### Epic D

- Add Location governance fields, scoped Team Notes and audit; similarity is advisory only.
- Keep bulk actions as upload → validate → preview → confirm → commit → result.
- Split Project and Visit Type management; server owns arrow reorder.
- Resolve rate by VisitDate + VehicleType; no hard-coded amounts.

### Epic E

- Persist outbox in the same transaction as the business event, then deliver asynchronously.
- UAT refuses non-allowlisted recipients and cannot be switched to Live mode by schema/config data.
- Fixed templates only. Provider failure updates outbox/log but never rolls back the business transaction.

### Epic F

- Follow `POST-UAT-v1.8.0-EPIC-F-GOOGLE-IMPACT.md` exactly.
- Keep live credentials outside GitHub; mock external APIs in normal CI.
- Never store full polylines, turn-by-turn data, alternative routes or optimized stop order.

### Epic G

- Remove navigation/page and prohibit new Supervisor assignment.
- Do not delete historical Supervisor role, scope, capabilities, exports, audits or transaction evidence.

## Final release gate

After Epic G passes: full backend and frontend tests, Playwright UAT, iPhone/mobile smoke, role/data-scope regression, historical Snapshot fingerprint/read regression, Google failure fallback, UAT email allowlist, all migration Verify scripts, and documentation updates (`CHANGELOG`, `SCHEMA_MAPPING`, deployment guide, `IT-HANDOFF`).

Business UAT sign-off is required before IT Production review. This plan never authorizes Production deployment or merge to main.
