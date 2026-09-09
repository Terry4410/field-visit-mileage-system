# UAT Fast-Track backlog

This backlog reorders only previously documented v1.8.0 work; it does not add business requirements.

| Priority | Batch | Existing scope | Completion gate |
|---|---|---|---|
| 0 | Development schema harness | Establish a trusted v1.7 schema-only foundation and evaluate 1800_001-007 in isolated disposable SQL; keep the development schema mutable | Ordered Verify.sql checks pass; no Azure UAT mutation; Final Clean Baseline remains not frozen |
| 1 | Epic B — Organization / People / Team | Center and Team lifecycle; Person/Employment; status/role/team membership; leaders and delegation; compatibility projection | Rehire/new employee number, leave/return/termination, as-of Center/Team, one Primary Team, multiple/delegated leaders, role/data-scope regression |
| 2 | Epic C — Deployment Site | Site master/history plus Team-Site and Employment-Site assignment; VisitDate eligibility and Snapshot preservation | Primary/effective-date/history regression passes |
| 3 | Epic D — Location / Project / Visit Type / Rate | Existing lifecycle, search, duplicate advisory, Team Note audit, batch preview, soft delete, ordering, and VisitDate+Vehicle rate rules | Boundary, concurrency, duplicate, audit, import, and mileage regression passes |
| 4 | Epic E — Notification framework | Transactional outbox, UAT Disabled/Test/empty allowlist, fixed templates, failure isolation | No unintended recipient; transaction/reminder semantics and failure audit pass |
| 5 | Epic F — Google governance | Existing approved impact design, mocks in CI, fallback/Snapshot, attribution and retention gates | IT/Legal gates before live provider smoke |
| 6 | Epic G — Supervisor retirement | Remove new assignment/UI while retaining historical role, scope, export, audit, and transaction evidence | Historical reads remain valid |

## Concrete coverage gaps found

- No complete fresh-install database foundation exists; the current files start from an earlier schema.
- No executable v1.8.0 clean baseline has been isolated and verified.
- Existing browser coverage is strong for Epic A but live role smoke/write lifecycle depends on environment secrets and UAT data.
- No automated clean-install/reset verification currently proves configuration, synthetic seed, schema version, and data-integrity invariants end to end.
- Epic B application/domain/API/UI implementation and its listed as-of/concurrency scenarios remain the next feature batch.

## Next modification batch

After the Development Schema Harness passes, implement Epic B as one bounded vertical slice: domain and mappings first, read/write service contracts second, Admin UI and import integration third. Preserve the current `Users`, `UserRoles`, `UserTeamScopes`, v1.7 assignment behavior, Trip/Snapshot reads, and role/data-scope enforcement until compatibility tests prove replacement behavior. Final Clean Baseline work is deferred until Epic B-G and Full AI Validation.
