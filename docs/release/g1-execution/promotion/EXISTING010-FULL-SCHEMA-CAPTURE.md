# Existing010 full-schema capture — prepared, not executed

This fixed-target SQL captures the catalog for all 63 dbo tables and stops unless there are exactly 26 SchemaVersions rows and the 1.8.0-010 marker. It returns table, column, type, default, collation, key/index, FK, CHECK, and trigger definitions and enforcement flags. It returns no business rows, credentials, or principal identifiers.

## Status

- Source artifact and static read-only guards: prepared in PR #83.
- Azure SQL execution: **NOT EXECUTED**. The database is paused, the free vCore allowance is exhausted, and overage billing is enabled.
- Existing 26-row content fingerprint and four historical-table aggregates remain the accepted evidence. This script does not replace or recapture them.
- Source-derived 001–010 reference comparison: **HOLD** until capture is authorized and compared with an exact source-derived disposable migration result.
- Trigger metadata is included because the prior normalizer omitted it.

## Execution boundary

One SQL connection will resume the database and may incur compute charges until Azure auto-pause after 60 minutes. No hard dollar cap has been verified. Do not execute until Owner approves a maximum exposure and one bounded connection window. Bind execution evidence to the exact PR SHA and SQL file hash; no retries or extra queries are implied.

Preparation does not prove runtime permissions, migration safety, API/Frontend/DB paired recovery, or release readiness.
