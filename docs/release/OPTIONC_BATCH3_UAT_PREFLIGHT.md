# Option C UAT Preflight Checklist (non-executable plan)

This is a checklist and evidence contract, not a migration script.

- Target: exact approved server/database; reject environment-name-only trust.
- Backup: timestamp, retention, restore test owner, and recovery point recorded.
- Schema: read-only verification of 011 -> 012 -> 014 dependencies.
- Safety: 013 remains reserved; no migration file is changed.
- Idempotency: stop on existing objects, duplicate SchemaVersions, duplicate DecisionKey, or partial catalog.
- Data: verify no ambiguous active Location authority; preserve LocationId and historical references.
- Runtime: backend before frontend; flags remain off until separately approved.
- Failure: rollback/restore owner and no-blind-retry procedure documented.
- Result of current work: disposable canonical rehearsal PASS; UAT/Production preflight NOT EXECUTED.
