# v1.8.0 Migration Safety Review

Date: 2026-09-08. Review state: **Review Gate corrections prepared; no Azure SQL execution**. Business baseline: approved `v1.8.0 Post-UAT Requirement Freeze & Impact Analysis` plus Architecture Review corrections.

## Decision summary

No intentionally destructive migration is present. The package contains no `DROP TABLE`, `DROP COLUMN`, `TRUNCATE`, `DELETE`, master re-key, Location merge, Snapshot recalculation or update of existing Trip/Snapshot business values. This is a source-level review only; no Azure SQL schema or data has been changed.

Scripts stop rather than repair data when they encounter an unexpected schema, missing predecessor, duplicate code, effective-date overlap, invalid date range, unsupported vehicle code or partial migration.

## Safety matrix

| Migration | Existing-data effect | Historical/effective-date protection | Main dependency | Assessment |
|---|---|---|---|---|
| `1800_001` | Adds Organization/Team fields and Center/Team-Center structures; no invented Center backfill | Captures v1.7.2 Trip/Snapshot counts and SHA-256 fingerprints; interval trigger | Existing Organizations, Teams, Users, Trip/Snapshot tables | Additive; stops on duplicate TeamCode/partial schema |
| `1800_002` | One-to-one legacy User → Person/Employment; copies only authoritative existing facts | No name merge or invented HR dates; effective employment/role/team guards | `1800_001`, Users/Roles and v1.7 assignment tables | Deterministic compatibility backfill only |
| `1800_003` | Creates empty Site and assignment structures | Existing history gets nullable fields only; interval/single-primary guards | Centers, Employments, Teams, Locations | Business seed required; SQL does not infer ownership |
| `1800_004` | Adds Location governance/search/note structures | No Location ID, Trip stop or Snapshot address change | Location/User/Team keys; all delete actions `NO_ACTION` | Additive; no merge/re-key/deactivation |
| `1800_005` | Adds Project/Visit Type/rate lifecycle metadata | Existing rate/Snapshot values unchanged; overlap stops execution | Project/Visit/rate tables and Organization | Additive; unknown/overlap data is a stop condition |
| `1800_006` | Adds `OptionalEmailNotificationEnabled`, event semantics, UAT policy, outbox/log | Transaction/System notices ignore personal optional preference; Reminder honors it | Employments/Users and event FKs | UAT remains `Test`, `Disabled`, empty allowlist; no email sent |
| `1800_007` | Adds provider request audit and company-approved mileage decision evidence | No old mileage output rewrite; no Snapshot coordinate columns | Trips, immutable submitted Snapshot, Mileage, Users | No Google-derived coordinate/distance/duration/request-ID persistence |

## Google persistence correction

The prior draft would have permanently stored geocoding latitude/longitude, route distance/duration, provider request ID and Snapshot coordinates. Those fields have been removed from `1800_007` pending IT/Legal confirmation of the company's contractual retention rights.

The corrected script persists only application/business audit metadata and the company decision: provider label, request basis/hash, status, sanitized error code/message, correlation ID, timestamps/requester, `ApprovedDistanceKm`, decision source/basis, approver and approval time. Full polyline, turn-by-turn data and raw responses remain prohibited.

Any proposal to retain Google-specific output long term is a separate IT/Legal decision gate requiring exact field list, contractual basis, retention/deletion control and a new reviewed versioned migration. Google attribution and application Terms of Use/Privacy Policy must also pass review before live UAT. See `POST-UAT-v1.8.0-EPIC-F-GOOGLE-IMPACT.md`.

## Notification semantics correction

`EmailNotificationEnabled` is replaced before execution by `OptionalEmailNotificationEnabled`. The migration explicitly marks whether an event honors that preference:

- `Transaction`: mandatory workflow notification; never suppressed by the personal optional preference.
- `Reminder`: optional; suppressed when `OptionalEmailNotificationEnabled = 0`.
- `System`: operational result notification; not governed by the reminder preference in this version.

Environment safety remains a higher-order gate: UAT is seeded in `Test` mode with `IsEnabled = 0` and no active allowlist entry. Therefore the migration cannot send email and does not authorize a provider/sender.

## Backfill and unresolved Business data

Safe deterministic backfill remains limited to `1800_002`. Each legacy `UserId` becomes its own Person and Employment; similarly named people are never merged. Unknown Hire/termination/status dates are not invented.

Business must provide and own the following through the reviewed workbook described in `POST-UAT-v1.8.0-UAT-MASTER-DATA-READINESS.md`:

- Center master.
- Team → Center effective assignment.
- Deployment Site master and Location relationship.
- Team → Deployment Site effective availability.
- Employment → Primary Deployment Site effective assignment.
- Motorcycle and Car mileage rates with effective dates.

SQL and AI must not infer these values. UAT email allowlist/sender and Google project/key/retention decisions are separate IT/Legal inputs and are not Business master-data seed rows.

## Effective-date and dependency rules

All period tables use inclusive `EffectiveFrom`/`EffectiveTo`; open end is treated as `9999-12-31`. Adjacent records therefore start the day after the prior end. The same date cannot belong to two primary/active periods.

Primary uniqueness is interval-based: one Team-Center, Employment status, Primary Team, Site Location, Primary Employment-Site, and active Organization+Vehicle rate for any date. Multiple effective Team leaders are allowed. Delegation does not erase the original leader assignment.

All new business/audit foreign keys use `NO ACTION`; migrations create referenced tables/columns before dependent FKs and indexes. Each `Up.sql` checks its exact predecessor and partial objects. Each `Verify.sql` checks required indexes and trusted FK/CHECK constraints.

## Historical immutability verification

`1800_001/Up.sql` records pre-change maximum IDs, row counts and SHA-256 fingerprints over v1.7.2 Trip, Snapshot and Snapshot Stop columns. `1800_001/Verify.sql` compares the same bounded records. New higher-ID rows do not alter the protected baseline.

For any future migration execution, application writes must be paused or gated. Run `1800_001/Verify.sql` after each separately approved Epic migration and after the final batch. Count-only evidence is insufficient.

## Future migration execution design — not authorized

- Existing `gh-fieldvisit-uat` remains read-only and is not granted migration rights.
- Use a distinct Microsoft Entra workload identity such as `gh-fieldvisit-uat-migrate` with separately reviewed least privilege and no Production access.
- Use a distinct GitHub Environment named `uat-migration`, separate from normal `uat` smoke/read-only operations.
- The migration workflow must use `workflow_dispatch`, `permissions: contents: read, id-token: write`, `environment: uat-migration`, Required reviewers/approval and a branch restriction allowing only `post-uat/v1.8.0`.
- Fail the branch/target guard before requesting the environment token or Azure OIDC login.
- Verify subscription, resource group, server and `DB_NAME() = db-fieldvisit-uat`; never create/relax a firewall rule.
- Execute by Epic and approval batch, not `1800_001` through `1800_007` in one run. Each dispatch selects exactly one approved migration folder, runs its `Up.sql`, its `Verify.sql`, and `1800_001/Verify.sql`, then stops.
- Suggested batches: Epic B=`001` then separately `002`; Epic C=`003`; Epic D=`004` then separately `005`; Epic E=`006`; Epic F=`007`. A failed batch blocks all later batches.
- No migration workflow, identity, role assignment or environment is created by this correction package.

## Stop conditions

Stop and request review if the target differs from approved UAT; schema/predecessor is unexpected; partial objects exist; business seed is absent or ambiguous; duplicate/overlap/unknown data is found; historical fingerprint changes; a destructive/data-repair action would be needed; network access would require relaxation; or permissions differ from the separately approved migration design.

No `Rollback.sql` is included. After backfill or v1.8 writes, dropping additive structures would destroy data/audit evidence. Recovery requires an IT-approved restore point or reviewed forward fix.
