# Existing010Upgrade integration — SOURCE ONLY / NONDEPLOY

This tree is a new integrated *source candidate*, not a new release, deployed binary, or Owner UAT GO. It is based exactly on PR82 `ae9ca02cf2d9539fe9bed61a073d60d63731cbb9` (therefore retains the full PR50 ancestry and the one-line Confirming fix), plus the **unchanged Git blobs** from frozen PR48 `9ee5dacceed2bf55c39310d663f3258bcb545989` and accepted PR81 `5bab502f20d9590d415d6a25a238d1629057989d`. See `integration-ledger.json` for the exact 53 imported file hashes.

## What is now integrated
- Original Clean Install / release runtime source in PR48 is copied, not rewritten; original PR48 HEAD/artifact remain separate.
- Existing010Upgrade signed history/runtime profile from PR81 overlays precisely the PR48 source version after PR48 is imported.
- PR50 Promotion/Verify and original synthetic reference remain byte-for-byte as in PR82's parent.
- PR82 preserves Confirming in phase020 and its eight accepted isolated SQL tests are reused, not repeated.
- The integration CI checks exact Git blob identity, scope and the new combined compilation, **not** full SQL2022 promotion or UAT.

## Mandatory HOLD: do not execute Promotion.sql
PR83 now carries a deterministically regenerated **NONDEPLOY synthetic candidate** whose 020 source hash matches the corrected phase020 SQL and whose 020 post-catalog hash is bound to the disposable SQL2022 result from Run40257405586. The reference provenance explicitly marks that catalog as synthetic and not as the live UAT010 baseline; package approval remains `PROPOSED_NOT_AUTHORIZED`, predecessor fidelity remains `HOLD`, and 013 stays reserved. This clears only the source/Manifest 020 mismatch. It does not prove the live 63-table predecessor, full source-derived catalog, or actual 26-row historical lineage. The full package therefore remains blocked from execution; never replace the source-bound UAT baseline with a synthetic or observed live hash. Independent review of the regenerated payload/package hashes and full Existing010 source contract is still required.

Outstanding LIVE gates: full necessary 63-table predecessor fidelity including column length/null/default/collation and 014 data preconditions; actual API SQL authentication/effective least-privilege principal; executing API and Pages immutable artifacts; backend Demo login and flags; matched API+Frontend+DB rollback. The SQL Portal observer is not proof of API credentials. This branch is NOT deployable; do not run the empty-DB install.py on existing UAT.

No Azure access, SQL calls, secrets, grant, restore, migration, deployment, flag activation or production change is part of this source integration. Owner standing authorization: Issue #41 comment 6382759582. G1=HOLD; Business UAT=NO-GO; IT handover=HOLD.

## 2026-10-25 Live-security deltas: documented evidence, not release GO
- Live API system-assigned MI **Client ID**, not its Object ID, matched the actual private SQL SID / contained EXTERNAL_USER by process and server catalog evidence: Issue #41 #6393698885. API principal binding PASS at that observed scope; do not relitigate the former identifier mismatch.
- Live principal inherits **db_datawriter** (broad DML) and db_datareader; complete protected SchemaVersions history-write deny absent. This is a demonstrated P0. PR83 commit f14c3449e13e47310235cc993306b8194c5f2a0d added an explicit runtime signed-gate rejection of db_datawriter, exact-SHA Run40165621730 SUCCESS. **The actual live SQL role is NOT changed.**
- The original API's six published file hashes and unique mapped process are recorded in #6393515690; original Frontend served-byte ZIP sealed #6393790514. Neither is an exact recoverable API binary package or a tested API+Frontend+DB recovery pair.
- **New review-only evidence query** `docs/release/g1-execution/promotion/EXISTING010-API-RUNTIME-ACL-OBSERVE-READONLY.sql`: SELECT-only, fixed UAT target, requires private already-attested API SID via per-session `FieldVisit.G1.PrivateApiSidHex`. It correlates metadata role inheritance, broad schema/database DML grants and history DENY visibility **without impersonation, exporting identities or modifying SQL**. Observer identity is never treated as the runtime API; this query is NOT an effective-rights or full operation-by-object proof. Needn't run until a distinct approved live evidence delta is necessary; do not duplicate accepted 63/26/014/trigger/MI checks.
- Remaining Gate: complete source-reviewed positive/negative per-table application rights matrix; isolated/recoverable rights cutover plan and actual deny/CRUD proof; full63 010 source lineage plus corrected 020 promotion package and digest; exact recoverable original API+Frontend+DB checkpoint; real effective backend flags. Source-only CI does not close these. 
- Static source review also found the test-only UAT cleanup endpoint performs physical deletes against approval/status/audit/mileage-governance history. It is now guarded by a separate `UatAutomation:CleanupEnabled` flag defaulting to `false`; enabling it remains outside normal Business UAT and must not be treated as permission to grant those history-table deletes. The effective deployed value still requires runtime proof.

## 2026-10-26 corrected phase020 manifest binding — NONDEPLOY

- Run `40257405586` on PR83 SHA `b031aa1d7c3181cbce6b8bc0f7892eae86a43ed8` produced the corrected 020 SQL2022 synthetic catalog digest `2F6346FBC1E113EAF678F4A256A2F4F3704342B3905DA4794FC658728B56D281`; first eight phase catalogs were unchanged.
- `reference.json`, regenerated `Promotion.sql`, `Verify.sql`, and `manifest.json` now bind corrected 020 source/payload/catalog hashes together. The manifest still marks the 010 baseline `HOLD`, the synthetic reference is explicitly not accepted as UAT, and the package remains unauthorized/nondeployable.
- This does not reconstruct 748 actual column definitions or validate 21 trigger bodies from the aggregate live fingerprints; it does not verify the complete 26-row historical content.
