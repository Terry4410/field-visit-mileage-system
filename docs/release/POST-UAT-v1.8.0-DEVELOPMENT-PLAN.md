# v1.8.0 gated development plan

Status: preparation for review. Migration scripts exist but have not been executed. No Production deployment or main merge is authorized.

## Gate model

Each Epic follows: `Impact Analysis → Coding → Commit to post-uat/v1.8.0 → GitHub Actions Build/Test → Regression`. A failed gate is analyzed and fixed in a new commit; the next Epic cannot start until all required checks pass.

Database migrations are not part of a normal build job. `gh-fieldvisit-uat` remains exactly `Reader + db_datareader`. When separately approved, migration uses the distinct identity `gh-fieldvisit-uat-migrate` and a separate `uat-migration` GitHub Environment with `workflow_dispatch`, Required approval and a `post-uat/v1.8.0` branch restriction. No job executes `001`–`007` as one batch; the first future run is fixed to `1800_001 Up → Verify → stop for Review`.

## Epic sequence and dependencies

| Epic | Scope | Migration dependency | Main regression gate | Start condition |
|---|---|---|---|---|
| A | Query and low-risk UX | None | Server-side filter/paging, date shortcuts, debounce, stale response, mobile modal | Already implemented on this branch; retain its successful Actions evidence and do not redo unless regression fails |
| B | Organization / People / Team | `1800_001`, then `1800_002` | Rehire/new employee number, leave/return/termination, Center/Team as-of, single Primary Team, multiple/delegated leaders, role/data scope | This preparation package approved; actual UAT migration access/role and execution separately approved |
| C | Deployment Site | `1800_003` | Team-visible sites, one Primary, start/end default, historical/backdated as-of and unchanged Snapshot | Epic B Actions + regression pass |
| D | Location / Project / Visit Type / Rate | `1800_004`, then `1800_005` | TaxId non-unique search, duplicate warning/no merge, Team Note scope/audit, batch preview, project soft delete, VisitDate+Vehicle rate | Epic C passes |
| E | Notification framework | `1800_006` | UAT Test/Disabled/empty allowlist, mandatory Transaction notifications, optional Reminder preference, outbox transaction, failure isolation/audit | Epic D passes; provider/sender decision may remain mocked |
| F | Google Maps Platform | `1800_007` | One ordered route, vehicle mapping, fallback/Snapshot retry, attribution, Terms/Privacy and no unapproved Google-output persistence | Epic E passes; IT/Legal retention and contract gates required before live smoke |
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
- Transaction workflow notifications ignore the individual's optional email preference; Reminder events honor `OptionalEmailNotificationEnabled`.
- Fixed templates only. Provider failure updates outbox/log but never rolls back the business transaction.

### Epic F

- Follow `POST-UAT-v1.8.0-EPIC-F-GOOGLE-IMPACT.md` exactly.
- Keep live credentials outside GitHub; mock external APIs in normal CI.
- Keep Google-derived coordinates, route distance/duration and provider request IDs transient until IT/Legal approves exact contractual retention. Never store polylines, turn-by-turn data, raw responses, alternatives or optimized order.
- Persist the company-approved distance decision and its source/basis hash, approver and timestamps; do not use durable `SystemDistanceKm` as a Google-result cache.
- Display required Google Maps attribution and block live UAT until applicable Terms of Use and Privacy Policy review is approved.

### Epic G

- Remove navigation/page and prohibit new Supervisor assignment.
- Do not delete historical Supervisor role, scope, capabilities, exports, audits or transaction evidence.

## Migration execution preparation (design only)

| Control | Required design |
|---|---|
| Read-only identity | `gh-fieldvisit-uat`; unchanged at `Reader + db_datareader` and never used for migration |
| Migration identity | Distinct workload identity `gh-fieldvisit-uat-migrate`; creation and least-privilege grants require later review |
| GitHub Environment | `uat-migration`, separate from `uat` |
| Trigger/approval | `workflow_dispatch` plus Required reviewers/approval |
| Branch | Only `post-uat/v1.8.0`; fail before OIDC when any other ref is selected |
| First execution unit | Only `1800_001/Up.sql → 1800_001/Verify.sql` (including historical fingerprint verification) → stop for Review |
| Later sequence | `1800_002` is not selectable in the first workflow and requires a separate post-001 approval; later units remain separately gated |
| Prohibited | Broad role, permanent firewall change, Production access, automatic `001`–`007`, application deployment |

The authoritative execution design and non-registered workflow draft are `POST-UAT-v1.8.0-MIGRATION-EXECUTION-DESIGN.md` and `docs/workflows/azure-sql-uat-migration-1800-001.draft.yml`. They are design artifacts only and cannot be dispatched from GitHub Actions in this state.

## Final release gate

After Epic G passes: full backend and frontend tests, Playwright UAT, iPhone/mobile smoke, role/data-scope regression, historical Snapshot fingerprint/read regression, Google failure fallback, UAT email allowlist, all migration Verify scripts, and documentation updates (`CHANGELOG`, `SCHEMA_MAPPING`, deployment guide, `IT-HANDOFF`).

Business UAT sign-off is required before IT Production review. This plan never authorizes Production deployment or merge to main.
