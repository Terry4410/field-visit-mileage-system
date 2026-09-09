# v1.8.0 development schema harness

This is a **DEVELOPMENT BASELINE** (status: **MUTABLE**), not the final clean-install baseline.

The harness performs one schema-only extraction of the trusted current UAT v1.7 structure, publishes it into disposable SQL database A, applies `1800_001` through `1800_007` in order, and generates a schema-only script for empty database B. Every migration `Verify.sql` is run in order. No migration in this harness is executed against Azure UAT.

The generated SQL and logs are CI artifacts. They are not release-frozen and must not be used for IT handover. `database/baseline/v1.8.0/` remains the future final-release entry point and stays fail-closed until the later Final Clean Baseline gate.

The harness excludes users, roles, role membership, permissions, secrets, and table data from the generated schema script. Schema-version metadata is applied separately by the harness; master data and synthetic data remain separate reset/import inputs.

Run locally only with a disposable SQL Server and never with the Azure UAT connection string. The supported CI entry point is `.github/workflows/v180-development-schema-harness.yml`.
