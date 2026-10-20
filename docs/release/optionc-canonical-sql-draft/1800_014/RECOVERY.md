# 1800_014 Option C recovery — nonexecutable draft

This is not authorization to execute a migration. `013` remains reserved.

- `Up.sql` is forward-only and hard-stops outside `OptionCIsolated_*`.
- No automatic destructive rollback is permitted. Before any future UAT review,
  obtain a verified backup/restore point and record the exact source SHA.
- If preflight, catalog trust, canonical JSON projection, or source RowVersion
  backfill is ambiguous, stop and restore the database backup; do not guess.
- If the transaction fails, verify that no `LocationPublicationStages`, ledger,
  `Locations`, request, or audit partial state remains before any retry.
- A commit acknowledgement failure is UNKNOWN COMMIT. Reconcile from a fresh
  read-only connection and never blindly rerun the approval.
