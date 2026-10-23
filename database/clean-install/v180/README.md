# Field Visit v1.8.0 — SQL Server 2022 Clean Install (partial, NONDEPLOY)

**Release decision: HOLD. Not yet an IT installer and not authorized for live UAT.**

## Verified source and execution

The accepted application runtime source is pinned to
`9d431ea318fcec34f04f3cc36a8fb6d01d00c48a`. Existing
`tools/clean-install/ModelBaselineRunner.csproj` generates the EF relational
model DDL and executes it against a newly created disposable SQL Server 2022
database named `FieldVisitV180CleanInstall`. It validates the **57 EF
mapped tables and 664 mapped columns**, constraints/trust state, and a
synthetic Organization insert/read with transaction rollback.

The next additive layer `scripts/ci/clean_install/build_sql_only_tables.py`
extracts the exact `CREATE TABLE` statements for **seven SQL-only tables**
from two pinned application RC migration sources:

- `database/migrations/1800_001_organization_center_team_lifecycle/Up.sql`:
  `SchemaMigrationDataBaselines`.
- `database/migrations/1800_006_notification_framework/Up.sql`:
  `NotificationEnvironmentPolicies`, `NotificationEmailAllowlist`,
  `NotificationSettings`, `NotificationSettingRecipients`,
  `MailOutbox`, `MailDeliveryLogs`.

The extraction fails closed for duplicate/missing tables, unbalanced SQL
literals/parentheses and ambiguous terminators. The generated file
`sql-only-core-tables.sql`, associated
`sql-only-core-tables-manifest.json`, source hashes and package hash are
archived as NONDEPLOY CI evidence. The disposable runner installs these
seven tables **after** validating the unmodified EF baseline and rechecks
their existence and trusted constraints, then destroys only its own
disposable database. CI errors, including cleanup failures, are fatal.

**These are seven source-derived tables, not a certified count of all
SQL-only or application-dependent schema objects.** Older source inventory
searches only numbered `Up.sql` files with regular expressions and can
miss other objects, dynamic SQL, and superseding definitions.

## What remains mandatory before PASS

The new database does **not** yet have a demonstrated complete set of
runtime SQL triggers, migration-only indexes/constraints/defaults,
stored modules, A+C candidate 011–020 final DDL and dependency ordering,
reference/initial configuration, and effective Visitor/Leader/Admin
RBAC/audit/approval behavior. Migration 013 remains reserved. Some A+C
sources are explicitly disposable proposals; they must be reviewed and
converted into approved final target-state DDL, not run unchanged against
a real shared database or disguised as historical migrations.

Do not claim `FULL_CLEAN_INSTALL=PASS` until a true fresh install contains
and validates those missing object contracts, demonstrates necessary
Browser/API/SQL tests and restricted runtime SQL permissions, and provides
IT reproducibility, recovery and verification instructions.

The CI workflow is **NONDEPLOY only**. It makes no Azure connection, no
shared SQL write/deletion/migration, no grants, no test-account creation,
no feature activation, no artifact promotion, and no Business UAT GO.

This partial state is useful for engineering and fails closed if the
expected clean-install source or disposable SQL Server test changes.
