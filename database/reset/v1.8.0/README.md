# Repeatable UAT reset plan

The reset sequence is repository-ready for planning but deliberately cannot mutate Azure yet.

1. Human Gate A approves destructive replacement/reset of `db-fieldvisit-uat`.
2. Create or replace the approved UAT database target.
3. Apply `database/baseline/v1.8.0/schema.sql` once its manifest status is `verified` and its SHA-256 is recorded.
4. Apply environment-safe system configuration.
5. Import synthetic automated seed for automation runs, or approved real UAT master data through the application import path. Never combine the two datasets silently.
6. Run `database/baseline/v1.8.0/Verify.sql`.
7. Run Fast Regression, then the applicable role E2E/data-integrity checks.

The existing Azure UAT database is not changed by this structure. `plan.json` keeps execution disabled until the baseline and destructive workflow are separately reviewed.
