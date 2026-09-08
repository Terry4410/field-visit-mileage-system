# v1.8.0 clean database baseline

This directory is the future clean-install entry point for new environments. It is separate from `database/migrations`, which remains the upgrade path for databases with retained data.

`schema.sql` currently fails closed. The repository does not contain a complete pre-v1.6 foundation, so concatenating the historical migrations would not be a valid clean install and would violate the Fast-Track decision.

## Baseline production procedure

1. Create an isolated disposable SQL Server/Azure SQL-compatible database.
2. Establish the complete current schema through reviewed source scripts in that isolated database.
3. Extract one schema-only script with deterministic object ordering; exclude users, role memberships, environment secrets, firewall configuration, and business/test rows.
4. Replace the fail-closed `schema.sql` with that artifact.
5. Apply it to a second empty disposable database.
6. Apply system configuration and synthetic seed through the reset plan.
7. Run `Verify.sql`, Fast Regression, and Full AI Validation.
8. Record the artifact SHA-256 in `manifest.json` and freeze it only at Final UAT Candidate.

No step here authorizes execution against `db-fieldvisit-uat`.
