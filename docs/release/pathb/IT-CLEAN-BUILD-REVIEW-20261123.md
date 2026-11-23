# Field Visit v1.8.0 — IT Clean Build evidence and release boundary (NONDEPLOY)

This is a *review* record for the company IT handoff, **not an executable/production installer** and **not authorization** to deploy, grant permissions, change login, or run SQL.

## Business baseline

- Business Owner approved Path B **Business UAT ACCEPT WITH OPEN ITEMS** on 2026-11-23: [Issue #41 decision](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7099388100).
- The 26 original P0 IDs remain accounted for: 19 scoped actual PASS; 3 canceled Supervisor N/A; 2 superseded old business rules; 2 retained partial limitations `QUERY-02` and `E2E-01`. The **new Daily Plan** proposal is still pending business-user acceptance and **is not part of the released UAT baseline**.
- Never overwrite historical accepted test runs, status or signed release evidence. CI/SQL checks are **not Business UAT**.

## Locked component identities

| Component | Git source | Status |
| --- | --- | --- |
| Selected Path B API | `ab71aaa96d9ac259f66386555812448284527d15` | Accepted UAT source; selected package mount DLL byte fingerprint not independently confirmed |
| Selected Path B Frontend | `90fe9bd61c24be57ccfaf6d450bf053e9c646ac1` | Accepted UAT source from distinct line of commits |
| Historical Clean Build RC | `9d431ea318fcec34f04f3cc36a8fb6d01d00c48a` | 57 EF mapped tables; source used to build disposable 69-table SQL Server 2022 clean target |
| Corrected final 020 source | Git blob `ce7bbb6f204bf1ab69c106ae59a5b139077494bc` | `CK_ImportBatches_Status` includes `Confirming` |
| Controlled NONDEPLOY integration | Draft [PR #117](https://github.com/Terry4410/field-visit-mileage-system/pull/117) | Review-only, not merged |
| SQL Server | GitHub disposable SQL Server 2022 / container | NOT company internal SQL instance |
| Company Windows/IIS/AD SSO | IT versions/approach TBD | NOT RUN / NOT CERTIFIED |

## Supported evidence, not an installation claim

1. [020 focus test #45202494508](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45202494508) succeeded: SQL2022 allowed `Confirming`, denied invalid Batch/Item status, source hash locked. No shared DB writes.
2. [Selected API + FE compile #45204798661](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45204798661) succeeded, separate exact UAT component sources. All ZIPs have deliberately invalid configuration to **prevent accidental deployment**. No company SSO validation.
3. [69-table isolated clean build #45206735733](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45206735733) succeeded: effective final CHECKs (including corrected 020), 69-table scoped catalog, independent sqlcmd replay and recover-verify, scoped API RC candidate evidence. Not full selected API business regression.
4. Additional selected current UAT API / clean SQL readiness smoke is required to close one narrow bridge; *do not claim PASS before exact CI has succeeded*. Even then, IT Clean Build remains separate.

## What IT must verify independently

1. Establish one final source/compiled API/compiled frontend/schema manifest and exact hashes; do not blindly merge divergent Git branches, or install old Azure-only scripts into a new company SQL target.
2. Obtain company-provided Windows Server, IIS, .NET 8 Hosting Bundle, SQL Server version, TLS/security/network and Windows SSO/AD architecture specification.
3. Have IT install to **fresh separate company test DB** using a reviewed company-target installer, approved service principal, authentic minimal role/reference data and no UAT-only Demo credentials. No fabricated historical migration markers.
4. Prove complete SQL catalog facets (columns, FKs, indexes, trusted checks, triggers, schema ledger, source and target SQL permissions); run actual deployed API/Frontend create/edit/approve/report and negative access tests on that same clean DB.
5. Verify foreign-Team/Org data denied for Visitor/Leader and allowed within proper scope for Admin: query, direct GET, exports, location writes. Retain original QUERY-02 business UAT exception until these real checks complete.
6. Verify IIS/Windows SSO login, HR identity to business roles, disabled/revoked users, secrets, Google route provider licensing/settings, Traditional Chinese fonts, backups/recovery, monitoring and operating owner.
7. Only after documented IT results and explicit go-live authorization can `IT Clean Build Passed` or `Production Ready` be declared.

## Status and stop conditions

- `Business UAT: ACCEPT WITH OPEN ITEMS` — actual Owner approval.
- `Code readiness: bounded CI source/build/SQL PASS` — per specific evidence only.
- `IT Deployment Ready: NOT YET PROVEN` — release does not yet include company-target validated installation + SSO config.
- `IT Clean Build: NOT RUN BY IT`.
- `Production Ready: NO`.

There is **no Azure/Production/Shared SQL write**, no deployment, no Github protected-branch merge, no enterprise principal or permission change, and no new Daily Plan in this branch. All work must remain nondeploy without new scoped authorization.
