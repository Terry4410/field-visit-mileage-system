# Field Visit — Current Project Status

> **Snapshot date:** 2026-11-19 (Asia/Taipei). **This is a dated evidence snapshot, not live telemetry.**  
> **Canonical live/history index:** [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41). Recheck its newest comments and actual PR/CI state before any further decisions.  
> **Operating rules:** ChatGPT Project Instructions V2.0 (Owner-managed) and [LESSONS_LEARNED.md](LESSONS_LEARNED.md).

## 1. Release identity and provenance

- **Repository:** `Terry4410/field-visit-mileage-system`.
- **Protected delivery base targeted by this documentation PR:** `post-uat/v1.8.0`. This does **not** mean the docs PR is merged or deployed.
- **Previously accepted Path B release source:** `d6c220877b9976b6478d85b5171d4e06293595ae` (as cited in [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981609385)). **Not independently attested here as the current live application binary SHA.**
- **Latest related NONDEPLOY ACL test PR:** [Draft PR #106](https://github.com/Terry4410/field-visit-mileage-system/pull/106), head `6bf1ca57ba822ca276ea906586b56adf7f04bbd0`, **OPEN/DRAFT/UNMERGED** at cited checkpoint. [CI run 44445077631](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/44445077631) reported SUCCESS; see [checkpoint](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997965303). Recheck PR head and CI if later modified.
- **Do not assume README's v1.6.0 header describes the current Path B deployed version**; identify version per source, environment and test evidence.

## 2. What has actually passed (Path B Azure UAT, not enterprise production)

| Item | Snapshot classification | Evidence / limitation |
| --- | --- | --- |
| Structural SQL install | **LIVE PASS** for 69 tables + 3 SchemaVersions | Reported historical installation evidence in [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41); not proof that all app queries are allowed. |
| App managed identity contained user + CONNECT | **LIVE PASS** (single use consumed) | [Live SQL receipt](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981277254). **Do not replay**. |
| API `/health/ready` | **LIVE PASS** at 2026-11-18 14:21:40 UTC | [Actual ready response](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981335831). Connect-only readiness, not full SQL CRUD or login authorization. |
| UAT synthetic seed | **LIVE PASS** for exactly 8 tables / 20 synthetic rows and readback, per later checkpoint | [Live seed/readback summarized with SQL permission findings](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997120701). **Single use consumed; do not replay**. |
| SQL public network access | **Owner-reported Disabled** after bounded Query Editor checks | [Owner evidence noted](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997400122). Not independently checked live in this documentation batch. |
| Generic SQL Server 2022 permission behavior | **DISPOSABLE CI PASS**, **not** Azure PASS | [Draft PR #106 and CI](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997965303): narrow SELECT permits 8 tables, explicit DENY/outsider/failure cases tested. No Azure GRANT executed. |

## 3. Actual user-facing blocker

**Business UAT: 0/25 (no live scenario PASS at latest cited checkpoint).** First scenario VIS01 remains blocked at login.

- Actual API log revealed **SQL error 229**, `SELECT permission denied` on `dbo.UserIdentityProfiles` during `AuthService.LoginAsync`: [live evidence](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997120701).
- Earlier historical pilot-user absence was real at that time; the later **20-row UAT seed is now live PASS**. Do **not** continue claiming missing seed is the sole current blocker.
- Catalog inspection of 8 login-related tables found **no explicit table SELECT grants visible** for the inspected principal, but a catalog-only result is **not** full effective permission proof: [ACL observation](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997400122).
- A prepared **eight-table narrowly scoped SELECT GRANT proposal** and separate readback script are **not live-authorized or executed** as of the [2026-11-19 checkpoint](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997965303). Actual App MI binding, target, DENY inheritance and exact runnable artifacts remain critical guards. Generic disposable SQL2022 PASS does not validate an Azure SQL write.
- The API may expose further legitimate DML/authorization problems **after** login; do not assume a SELECT correction completes 25 UAT cases.

## 4. One-use actions / prohibited assumptions

- **Consumed:** prior contained MI `CREATE USER + CONNECT` and the 20-row UAT seed. No repeat/retry or reinstallation.
- **No permission inferred:** Owner authorization for SQL identity setup, seed, or NONDEPLOY review does **not** authorize new live `GRANT`, extra DDL/DML, Azure networking changes or deployment.
- **No broad grant by default:** do not substitute `db_owner`, `db_datareader`, `db_datawriter` for object-scoped permission review.
- No Azure/SQL write, token acquisition, GRANT, deploy, IT production work or Business UAT was performed in creating these governance docs.

## 5. Next three minimum necessary actions (not blanket live authorization)

1. **P0 — Decide a bounded, independently reviewed login SQL permission correction** based on the latest exact source/identity and permission evidence; obtain a **new exact-scope Owner authorization before any live GRANT**. If independent checks or current target binding fail, HOLD. Do not repeat old one-use operations.
2. **P0 — Perform real 3-role login smoke then VIS01** only after a verified permission correction, record timestamps/response and role-specific access evidence; handle new failures by actual root cause.
3. **P1 — Complete all Business UAT 25 scenarios**, record pass/fail on the known version and environment; in parallel begin **IT internal clean-build requirements** (SQL Server version, Windows/IIS/.NET, AD Windows SSO, least-privilege service identity, security scanning/operations).

## 6. IT handover track (planned; not signed off)

| Deliverable | Status as of this snapshot | Ownership / exit criterion |
| --- | --- | --- |
| SQL Server-compatible, versioned full schema + migrations and reference seed | **NOT YET VERIFIED FOR IT CLEAN BUILD** | Development prepares; IT recreates an empty DB on its supported SQL Server version. |
| React/API source, reproducible build and IIS deployment configuration | **NOT YET VERIFIED ON INTERNAL IIS** | Development package, IT hosts and validates. |
| AD Windows SSO integration + business authorization matrix | **PLANNED, NOT YET VERIFIED** | IT defines AD/Windows requirements; HR owns business roles; development implements. |
| Scans, security defects, logging, backups and rollback | **IT GATES NOT YET CONFIRMED** | IT sets acceptance standards, development addresses app defects. |
| Business UAT and formal sign-off | **0/25 CURRENT CHECKPOINT** | HR leads business acceptance. |

**Desired enterprise target:** internal Microsoft SQL Server, Windows Server/IIS, AD Windows SSO — based on Owner planning, **not IT's signed architecture specification**. No need to migrate or preserve Azure SQL as a production dependency.

## 7. Update contract

Only update this document when an observed state, canonical release, blocker, approval boundary or IT decision materially changes. For each edit record:

- observation date/time and environment;
- code SHA/PR + exact source where known;
- PASS/HOLD/UNKNOWN with **live vs offline** classification;
- remaining blocker, owner and specific next step;
- a link to the actual evidence.

Never claim a script was run, permission granted, UAT passed, IT sign-off obtained or governance record merged without evidence. The newest live evidence supersedes this dated summary; preserve the historical snapshot and link the newer source when updating.
