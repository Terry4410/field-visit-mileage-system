# Development-only migration compatibility layer

These adapters exist only to evolve the schema-only v1.7 foundation inside the disposable v1.8 development harness. They are mutable development tooling, not production upgrade scripts and not final clean-baseline inputs.

`manifest.json` pins every original `Up.sql`, every original `Verify.sql`, and every selected development apply file. A source or adapter hash mismatch fails closed. Migrations 1800_003 and 1800_006 use their original `Up.sql` directly because their static analysis found no deterministic predecessor or batch-order incompatibility.

The adapters preserve intended schema outcomes while introducing SQL Server-safe deferred compilation where an original script references a newly added column in the same batch. Existing predecessor indexes or constraints are reused only after their complete intended definition is validated. They never drop or recreate predecessor objects.

Only `.github/workflows/v180-development-schema-harness.yml` may invoke this directory. Azure UAT access ends after one schema-only extraction; all compatibility SQL runs against the disposable local SQL Server container.
