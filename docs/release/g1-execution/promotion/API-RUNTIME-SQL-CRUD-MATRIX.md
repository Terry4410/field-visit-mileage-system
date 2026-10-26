# API Runtime SQL CRUD Matrix — NONDEPLOY Candidate

**Status: partial source-reviewed candidate; P0-A remains HOLD.** Updated at PR #83 head `d8475b6cc3fd2f46569b10aa1dc67dd63009d0ce`; this document and its CSV do not authorize Live SQL rights changes. `CleanupEnabled` remains false.

The CSV is one row per physical table in the reviewed callsite set (32 tables at this SHA). CRUD columns identify candidate table permissions (`YES`/`NO`), not proof that every API route has passed. It covers current 010 trip, approval, admin identity, import and worker paths plus gated post-010 Option C objects. `010_AND_POST010_OPTIONC` marks tables that already exist in 010 and are also touched by the gated candidate. Option C rows are future schema candidates and must not be applied before their migrations are independently approved. Migration history is SELECT-only for Runtime; Migration Operator grants remain separate and time-bounded.

## SQL2022 test boundary

`scripts/ci/test_api_runtime_acl_matrix.ps1` provisions an isolated database using the runner's local SQL2022 administrator connection, creates simple same-name probe tables, creates `FieldVisitRuntimeTest WITHOUT LOGIN`, applies the CSV rights, and impersonates that database principal for each DML statement. It requires the principal to have neither `db_owner` nor `db_datawriter`; every permitted insert/update/delete must affect exactly one row; each protected migration-history insert/update/delete must raise SQL permission error 229. The test writes a sanitized receipt and drops the isolated database in `finally`.

This standalone ACL test proves SQL Server table-permission behavior against same-name probes and nonzero DML row counts. The separate Path B job runs the current API over HTTP against the pinned 69-table clean install as the matrix-restricted principal; its coverage and exact failures are reported independently. Source review identified the route/geocoding attempt UPDATE needed for the controlled Pending-to-terminal transition and insert-only governance events; this does not imply complete callsite closure or live readiness. It does not validate Excel workbook parsing/transactions, all Admin/Leader/Visitor endpoint combinations, or Option C migrations. Those rows remain candidates pending canonical SQL2022 workflow tests. The simplified probe tables are deliberately not called schema-fidelity evidence.

## Permission boundaries

- Runtime candidate: table-scoped `SELECT` and only listed business DML. Protected migration history: `SELECT` plus explicit `DENY INSERT, UPDATE, DELETE`.
- Migration Operator: separate identity and existing per-migration scripts; this Runtime matrix grants no DDL or schema-level DML.
- No `db_datawriter`, `db_owner`, schema-wide DML, ownership, or Live SQL action is part of this candidate.
- Existing Azure `db_datareader` is broader than this candidate. No Live role removal or permission change is included.

## Remaining evidence required

Remaining evidence includes full callsite closure for tracked EF modifications, raw SQL and bulk operations; broader Visitor, Leader, Admin, Excel Import and Worker HTTP workflows; verified trigger/FK execution semantics; and full negative protected-history tests against canonical definitions. Path B remains NONDEPLOY and cannot establish Azure rights or effective flags. Until those pass, do not cut over Live rights or mark G1 PASS.
