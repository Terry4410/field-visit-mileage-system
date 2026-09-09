# Architecture decisions

## ADR-001 — Clean baseline during disposable UAT development

New installations must apply one latest-release schema baseline rather than replay disposable development-stage UAT history. The baseline is generated and verified in an isolated disposable SQL environment. Existing migration files remain immutable inputs and audit references until the baseline is frozen.

## ADR-002 — Versioned migrations after retained data exists

Production and any environment whose business data must survive upgrades use reviewed, ordered migrations with verification and recovery controls. The `1800_001` framework, SHA locks, Grant/Verify/Revoke scripts, PITR evidence, and GitHub Environment controls are preserved for that path. They are not the current UAT reset path.

## ADR-003 — Synthetic and real data are separate

Repository data is synthetic and uses non-real identifiers and `example.invalid` email addresses. Real Final UAT master data is supplied outside Git and imported through the approved preview/validate/confirm path. Historical UAT transactions are not an installation dependency.

## ADR-004 — Minimal Change and Protected Baseline

Validated behavior is protected. Every feature starts with dependency and downstream impact analysis. Unrelated refactors, renames, UI redesigns, API changes, database changes, permission changes, and aesthetic cleanup are excluded. A necessary shared change must name affected features and add regression coverage before completion.

## ADR-005 — Two IT handover paths

1. Clean Installation: latest baseline, configuration, master-data import, application deployment, verification.
2. Future Upgrade: retained database, ordered migrations, verification, and recovery controls.

The handover package does not require old UAT transactions or reconstruction of intermediate development database states.

## ADR-006 — Two automated validation levels

Fast Regression runs static policy checks plus the existing frontend and backend suites for ordinary feedback. Full AI Validation adds builds, isolated browser regression, boundary/negative/data-integrity scenario expansion, and later role E2E. Live write-flow UAT remains explicitly gated and Final Human UAT validates usability and business correctness.

Every reproducible defect follows `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## ADR-007 — Mutable development schema harness

During disposable UAT development, the working schema is a **DEVELOPMENT BASELINE** with status **MUTABLE**. It is a disposable integration foundation for Epic B-G and automated regression, not a clean-install handover baseline and not SHA-frozen. A trusted v1.7 schema-only extraction is evolved through the ordered 1.8 migrations only inside an ephemeral SQL environment. Migration defects are reviewed explicitly; historical 1800_001 remains immutable.

## ADR-008 — Final baseline is a later release gate

The **FINAL RELEASE CLEAN BASELINE** is **NOT YET CREATED / NOT FROZEN**. It is produced only after Epic B-G and Full AI Validation, followed by empty-database proof, deterministic hashing, and IT handover review. Clean installation and production upgrades remain separate paths.

## ADR-009 — Cost guardrail

Repository/static checks and one consolidated ephemeral validation run are preferred. Azure access is read-only and minimized to one schema extraction where required. No new Azure resources, paid scaling, repeated SQL wake-ups, duplicate Actions runs, or unnecessary runners are allowed. Destructive actions and permission/security changes require a Human Gate.
