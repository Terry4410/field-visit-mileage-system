# Recovery and rollback SOP

## Before Business UAT

1. Verify Azure SQL backup retention and record an available restorable point.
2. After schema installation and runtime ACL, capture a new post-install
   restorable point before Business users enter data.
3. Preserve the exact API, Frontend, SQL package, schema fingerprint, ACL and
   signed activation as one recovery pair.
4. Confirm SQL stays Private-only.

AMEND-02 moves the full isolated PITR restore plus paired application recovery
rehearsal to IT acceptance. Backup retention and a usable restorable point are
still mandatory before Business UAT.

## Failure during SQL installation

- Stop on the first failed or unknown SQL call.
- Preserve the layer journal and sanitized evidence.
- Do not rerun, continue remaining layers, DROP, reset, restore or manually
  repair the database without a separate recovery decision.
- Treat any unknown App-side once marker or schema state as a partial-install
  possibility until a read-only assessment proves otherwise.

## Failure after application deployment

1. Stop new UAT admission or disable the affected signed feature safely.
2. Preserve application, database and request-correlation evidence without secrets.
3. Roll back API and Frontend together to the last verified pair.
4. If the database must be restored, restore to a separately named isolated
   database first and validate schema, ACL and application trust before switchback.
5. Never point Path B applications at `db-fieldvisit-uat` or Production.

## IT acceptance recovery rehearsal

IT should perform a full PITR restore to an isolated Path B recovery database,
apply the same runtime principal contract, bind the same API/Frontend pair, run
health plus role-isolation smoke tests, record RTO/RPO, and obtain formal
acceptance. Production DR, penetration tests and hardening remain IT-owned.

