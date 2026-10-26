# Existing010 full-schema capture — completed once; source parity still on HOLD

This fixed-target SQL captures the catalog for all 63 dbo tables and stops unless there are exactly 26 SchemaVersions rows and the 1.8.0-010 marker. It returns table, column, type, default, collation, key/index, FK, CHECK, and trigger definitions and enforcement flags. It returns no business rows, credentials, or principal identifiers.

## 2026-10-26 exact-SHA capture result

- Source PR #83 SHA: `723868922d229d557130b3c8b2aad35acbc1803a`.
- Query SHA-256: `AC8EE4D845E4154AF8162EBD4679D3A9A1DB50492D4CDFE779E82F8C3186AB91`.
- Azure management plane showed the fixed UAT database Online. One connection succeeded on attempt 1; the exact seven-SELECT script executed once with a 30-second command timeout. The connection was closed before evidence upload. No DDL, DML, Migration, permission, deployment, or settings operation was run.
- Result row counts in query order: `1, 63, 748, 395, 152, 93, 41` (target, table inventory, columns, indexes, foreign keys, CHECK constraints, trigger events).
- Private Blob path: `schema-evidence/capture/723868922d229d557130b3c8b2aad35acbc1803a/20261026T033738Z/schema.json`.
- Captured JSON: 684,787 bytes; SHA-256 `887661A84E3FB447BAC74CF9172EA020008584DA94741A0AE611D8D1EB1B161D1`.
- Manifest SHA-256: `F075D1A5F79E6E6B3D0DC963C89FECC9AE72AD5B210EA93F7BE18D330BC89256`.
- The private object was downloaded and byte-checked against the uploaded JSON SHA. Full catalog definitions remain private and are not published in GitHub.

The existing normalized comparer previously accepted only four facets (Columns, Indexes, ForeignKeys, Checks). It is being extended to normalize all seven result sets and compare Table Inventory and Triggers as well, preserving multi-line definitions and excluding only volatile trigger create/modify timestamps.

### Source baseline finding

The `1800_001_organization_center_team_lifecycle/Up.sql` and `Verify.sql` sources are present at the exact PR SHA. The earlier Issue #41 statement that migration 001 was absent was incorrect. Its protected necessary-reference also pins the 001 Up.sql SHA-256 `8e21073a1cd8bf239ad20504e670ea8c0df69cf02f630bb92f473465fbd6726c`.

The repository does not contain a complete source snapshot of the pre-1.8.0 1.7.2 schema needed to create an independent full 63-table reference. The 001–010 migrations are incremental (for example, 001 requires existing `Users`, `Organizations`, `Teams`, and historical trip tables at predecessor `1.7.0-008`); earlier 1600/1700 scripts also assume legacy objects. Existing references are explicitly scoped necessary/synthetic contracts, not an observed or complete predecessor baseline. Thus the live capture can now be normalized and compared, but a full 63-table source-parity PASS and deployable Existing010 package remain blocked until a trusted full predecessor schema source is found or supplied.

The 26-row history fingerprint and four historical-table aggregates are reused from their accepted prior evidence; this schema query did not recapture business data.

## Execution boundary

One SQL connection will resume the database and may incur compute charges until Azure auto-pause after 60 minutes. No hard dollar cap has been verified. Do not execute until Owner approves a maximum exposure and one bounded connection window. Bind execution evidence to the exact PR SHA and SQL file hash; no retries or extra queries are implied.

Capture and private storage integrity do not prove full source parity, runtime permissions, migration safety, API/Frontend/DB paired recovery, or release readiness.
