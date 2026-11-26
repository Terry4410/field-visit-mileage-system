# Field Visit — Current Project Status

> **Snapshot: 2026-11-26 Asia/Taipei.** Canonical history: [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41). Owner rules V2.1. This is governance documentation on an existing documentation branch, not a deployed code change or protected-branch merge.

## Current decision and release freeze

Owner formal sign-off: [#7194918527](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7194918527). Business UAT **ACCEPT WITH OPEN ITEMS**. Frozen source `fb60b7849ac8ac1988814d8ad1741f5df35df6b6`, Schema `1.8.0-010`, Azure Path B deployment [#7190082054](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7190082054). Azure is development/UAT only. No new general functionality, deployment, SQL/ACL/Azure changes or replay of consumed authorization.

| Milestone | Status |
| --- | --- |
| Code Ready | PASS, [CI 45793885742](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/45793885742): 45 backend/61 frontend isolated tests |
| Business UAT | Owner ACCEPT WITH OPEN ITEMS; not blanket LIVE PASS |
| IT Deployment Ready | REVIEW CANDIDATE, not declared ready |
| IT Clean Build | NOT RUN by company IT |
| Production Ready | NOT RUN; security and operational acceptance required |

## Retained evidence

- [#7193743501](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7193743501) and #7193396035: eight LIVE actual Excel/PDF file-content PASS.
- [#7194772526](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7194772526): cross-team list allow/deny, full Plus Code, canonical duplicate distinct, Leader two-item nonfinancial batch close, preserved snapshot amounts, rate keyword and correction history evidence. Prior valid scoped PASS preserved.
- IT review package [existing Library item](https://chatgpt.com/library?file_id=libfile_dc3b006b171c819193a7fcea92ceb7ae&account_id=personal), v3 technical baseline (38,383,289 bytes; SHA256 fc12cad3bc5251b5b0ba38ea6128a2261877d1d0edaa4504de033002c7b835b1), documented in #7194772526. Closeout documents may update that same item; use latest verified receipt in Issue #41 for exact package identity.

## Accepted open items

| Item | Disposition |
| --- | --- |
| P0 direct-ID cross-team detail denial and necessary backend role/team/org access | NOT RUN for direct ID; mandatory IT security acceptance before production, not waived. List PASS is not detail PASS. Resolve security failures before production. |
| P2 legacy direct UseExisting and MergeFields | NOT RUN, Owner-approved deferral; retain canonical independent review and published-master fail-closed safeguards. |
| P2 Admin financial multi-item close | NOT RUN, deferred; existing individual workflow, not inferred from Leader nonfinancial batch PASS. |
| P1 rate date filter | LIVE inconclusive, verify in IT supported browser; fix only confirmed material defect. |

## IT handoff

Corporate Windows Server/IIS + intranet SQL Server. Entra ID permitted by IT [#7119365137](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7119365137), reconciled at [#7193912981](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-7193912981). Native IIS Windows authentication rewrite not required. Company IT owns tenant/application configuration, identity mapping, company data initialization, least-privilege SQL identity, clean install from 69-table schema and migrations, independent IIS deployment, security scanning, access and functional tests, backup/restore, monitoring and operational responsibility. Confirm app maintenance ownership. No Azure UAT DB or pilot credentials transferred as production data. IT review may begin immediately; no corporate deployment approval inferred.

## Historical snapshot (retained; superseded for current decisions)

The following 2026-11-19 snapshot is historical only, not the current UAT or architecture state.

# Field Visit — Current Project Status

> **Snapshot date:** 2026-11-19 (Asia/Taipei). **This is a dated evidence snapshot, not live telemetry.**  
> **Canonical live/history index:** [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41). Recheck its newest comments and actual PR/CI state before any further decisions.  
> **Operating rules:** Owner-managed ChatGPT Project Instructions (check actual configured version) and [LESSONS_LEARNED.md](LESSONS_LEARNED.md).

## 1. Release identity and provenance

- **Repository:** `Terry4410/field-visit-mileage-system`.
- **Protected delivery base targeted by this documentation PR:** `post-uat/v1.8.0`. This does **not** mean the docs PR is merged or deployed.
- **Previously accepted Path B release source:** `d6c220877b9976b6478d85b5171d4e06293595ae` (as cited in [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981609385)). **Not independently attested here as the current live application binary SHA.**
- **Latest related NONDEPLOY ACL test PR:** [Draft PR #106](https://github.com/Terry4410/field-visit-mileage-system/pull/106), head `4708f7896472727483e24f7bb4903bfa8c871434` at [latest recorded checkpoint](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6998329810), **DRAFT/UNMERGED**. [CI 44448082092](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/44448082092) reported SUCCESS: disposable SQL Server 2022 parse-only on V2 text with one MI Object ID placeholder, plus generic permission behavior tests. **No Azure execution or grant proof.** Recheck actual live PR head/CI before use.
- **Do not assume README's v1.6.0 header describes the current Path B deployed version**; identify version per source, environment and test evidence.

## 2. What has actually passed (Path B Azure UAT, not enterprise production)

| Item | Snapshot classification | Evidence / limitation |
| --- | --- | --- |
| Structural SQL install | **LIVE PASS** for 69 tables + 3 SchemaVersions | Reported historical installation evidence in [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41); not proof that all app queries are allowed. |
| App managed identity contained user + CONNECT | **LIVE PASS** (single use consumed) | [Live SQL receipt](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981277254). **Do not replay**. |
| API `/health/ready` | **LIVE PASS** at 2026-11-18 14:21:40 UTC | [Actual ready response](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6981335831). Connect-only readiness, not full SQL CRUD or login authorization. |
| UAT synthetic seed | **LIVE PASS** for exactly 8 tables / 20 synthetic rows and readback, per later checkpoint | [Live seed/readback summarized with SQL permission findings](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997120701). **Single use consumed; do not replay**. |
| SQL public network access | **Owner-reported Disabled** after bounded Query Editor checks | [Owner confirmed restoration after the later SQL syntax error](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6998225869). Not independently checked live in this documentation batch. |
| Generic SQL Server 2022 permission behavior + masked V2 syntax | **DISPOSABLE CI PASS**, **not** Azure PASS | [Latest PR #106 checkpoint](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6998329810): narrow permission behavior and V2 `PARSEONLY` passed; V2 text differed only by masking MI Object ID. Does **not** establish Azure parser/runtime success. |

## 3. Actual user-facing blocker

**Business UAT: 0/25 (no live scenario PASS at latest cited checkpoint).** First scenario VIS01 remains blocked at login.

- Actual API log revealed **SQL error 229**, `SELECT permission denied` on `dbo.UserIdentityProfiles` during `AuthService.LoginAsync`: [live evidence](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997120701).
- Earlier historical pilot-user absence was real at that time; the later **20-row UAT seed is now live PASS**. Do **not** continue claiming missing seed is the sole current blocker.
- Catalog inspection of 8 login-related tables found **no explicit table SELECT grants visible** for the inspected principal, but a catalog-only result is **not** full effective permission proof: [ACL observation](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6997400122).
- **Newer live fact superseding the earlier proposal status:** the original eight-table SELECT GRANT candidate (SHA-256 `42ff5385db9c2d292b470b5a48e19bbce554eaf43ea64b7129141e8773472492`) was **authorized and attempted once** in Azure Query Editor at **10:42:11 local time on 2026-11-19** but returned `Incorrect syntax near ')'`. **No success receipt, no independent grant readback; outcome UNKNOWN/NOT PASS**, not proof of zero changes. This exact one-shot authorization is **CONSUMED; do not replay**. [Live attempt report](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6998127615).
- A **new V2 repair review only**, SHA-256 `cc78b3f476466812c36f46935dde37a0e6f7d36e76ab933b1f3be6c5d49eabd9`, has since passed **masked-equivalent SQL Server 2022 PARSEONLY**, but has **not** received new exact-SHA live GRANT authorization and has **not** run in Azure. Azure Query Editor syntax-error root cause remains unproven. [Latest observed checkpoint](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6998329810).
- The API may expose further legitimate DML/authorization problems **after** login; do not assume a SELECT correction completes 25 UAT cases.

## 4. One-use actions / prohibited assumptions

- **Consumed:** prior contained MI `CREATE USER + CONNECT`, the 20-row UAT seed, **and the original exact-SHA eight-table SELECT GRANT attempt** that returned syntax error. No replay/retry or reinstallation under these old grants.
- **No permission inferred:** prior exact-SHA Owner consent was valid for the consumed earlier attempt only. A new V2 candidate, even with offline PARSEONLY PASS, requires separately reviewed **new exact-SHA authorization** for any live `GRANT`; no extra DDL/DML, broad grants, new Azure networking or deployment inferred.
- **No broad grant by default:** do not substitute `db_owner`, `db_datareader`, `db_datawriter` for object-scoped permission review.
- This governance documentation update itself performs **no Azure/SQL write, GRANT, token, deploy, IT production work or Business UAT**. The earlier Azure Query Editor attempt was performed separately by Owner; its outcome remains UNKNOWN/NOT PASS.

## 5. Next three minimum necessary actions (not blanket live authorization)

1. **P0 — Do not replay failed original GRANT.** Establish prior-attempt grant state from permissible nonmutating evidence, confirm exact V2 source and target/identity/permission gates, then seek a **new, bounded exact-SHA Owner authorization** for any necessary live repair. Preserve Public Network Access Disabled outside a separately approved minimum-time, exact-IP window. Never treat SQL2022 PARSEONLY as Azure GRANT success.
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
