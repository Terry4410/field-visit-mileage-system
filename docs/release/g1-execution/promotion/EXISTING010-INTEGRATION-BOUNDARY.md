# Existing010Upgrade integration — SOURCE ONLY / NONDEPLOY

This tree is a new integrated *source candidate*, not a new release, deployed binary, or Owner UAT GO. It is based exactly on PR82 `ae9ca02cf2d9539fe9bed61a073d60d63731cbb9` (therefore retains the full PR50 ancestry and the one-line Confirming fix), plus the **unchanged Git blobs** from frozen PR48 `9ee5dacceed2bf55c39310d663f3258bcb545989` and accepted PR81 `5bab502f20d9590d415d6a25a238d1629057989d`. See `integration-ledger.json` for the exact 53 imported file hashes.

## What is now integrated
- Original Clean Install / release runtime source in PR48 is copied, not rewritten; original PR48 HEAD/artifact remain separate.
- Existing010Upgrade signed history/runtime profile from PR81 overlays precisely the PR48 source version after PR48 is imported.
- PR50 Promotion/Verify and original synthetic reference remain byte-for-byte as in PR82's parent.
- PR82 preserves Confirming in phase020 and its eight accepted isolated SQL tests are reused, not repeated.
- The integration CI checks exact Git blob identity, scope and the new combined compilation, **not** full SQL2022 promotion or UAT.

## Mandatory HOLD: do not execute Promotion.sql
The accepted PR50 Promotion.sql, Verify.sql, reference.json and manifest.json are deliberately **not** re-labelled or overwritten. `manifest.json.sourceHashes` still pins the superseded phase020 SQL; new phase020 and old manifest are intentionally incompatible. Package ID, all step hashes (especially 020), post-step Catalog reference, complete source-derived UAT010 predecessor contract and full post-upgrade schema fingerprint must be regenerated and independently accepted together, never patched with an observed UAT hash. The 26 actual historical rows must stay intact; synthetic history PASS is not actual historic lineage proof. Migration 013 stays reserved.

Outstanding LIVE gates: full necessary 63-table predecessor fidelity including column length/null/default/collation and 014 data preconditions; actual API SQL authentication/effective least-privilege principal; executing API and Pages immutable artifacts; backend Demo login and flags; matched API+Frontend+DB rollback. The SQL Portal observer is not proof of API credentials. This branch is NOT deployable; do not run the empty-DB install.py on existing UAT.

No Azure access, SQL calls, secrets, grant, restore, migration, deployment, flag activation or production change is part of this source integration. Owner standing authorization: Issue #41 comment 6382759582. G1=HOLD; Business UAT=NO-GO; IT handover=HOLD.
