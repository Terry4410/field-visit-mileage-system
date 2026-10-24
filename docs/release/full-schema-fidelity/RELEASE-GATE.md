# Full Schema Fidelity NONDEPLOY release gate preparation

Reviewed source: `4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc`; tree `780003abb1750b36806832bf1f90745c2cd1af7c`.
Accepted C1 run `39233612142` remains accepted. This preparation neither executes UAT nor reopens R3, A+C or authentication engineering.

## Reused contracts and bounded scope

The intended existing Full Inventory contract contains exactly seven result sets: target identity; SchemaVersions count/latest; columns/defaults; index columns and flags; foreign-key columns/actions; CHECK definitions/trust; four historical table aggregates. The existing validator checks result count, ordered column signatures, nonempty sections, target, latest `1.8.0-010` and historical table names. PR #38 exception classification remains unchanged. First unchanged-source disposable executions failed at SQL execution: run `39245201587`, classification `SqlSyntaxOrCompatibility`, provider `156`. The unquoted `AS RowCount` alias is reserved syntax on SQL Server 2022. Run `39245410366` then proved the unquoted form fails and the bracketed form returns the contract successfully. A separate release-gate candidate changes ONLY `AS RowCount` to `AS [RowCount]`, preserving the output name and all seven result sets. The original file and accepted R3 pins remain byte-identical; the original is historical evidence and is NOT the future execution candidate. No inference is made about the cause of prior R2/UAT failures.

The fixture executes actual SQL on SQL Server 2022 rather than substituting mocked DataSets for execution proof. The final release-gate candidate path and immutable pins are in `scripts/ci/full_fidelity/hashes.json`.

“Full” means the inventoried relational facets. It does not include a complete database definition: triggers, views, procedures, computed expressions, temporal/partition/storage settings, security/RLS and grants are outside this SQL's output. Four-stage R3 Catalog PASS or a fingerprint does not establish these facets or schema fidelity.

The input ledger `protected-inputs.json` binds reviewed repository files, including 001–010 Up/Verify files, 1.5 Verify, available design archive and application model. It records observations, not approval of a historical baseline. The migration chain requires an existing historical schema and prior version; it cannot honestly recreate 010 from an empty database using these files alone.

## Missing provenance and reference

No independently approved original historical 1.5 CREATE baseline was found in the reviewed repository inputs, available archive or GitHub release list. `database/015_schema_v1_5_verify.sql` checks selected existing objects; it is not CREATE DDL. Its identity-returning verification queries are not authorized for future execution by this document. The Gate1 archive's `SQL_Schema_Draft.sql` is explicitly a design draft, not an approved 1.5 baseline. A+C's synthetic 010 bootstrap likewise is not observed UAT parity or historical provenance.

A reproducible, independently trusted expected 010 reference therefore remains unavailable. AppDbContext conventions, migration text and synthetic fixtures provide useful requirements but do not supply the absent historical DDL or an independently attested complete relational reference.

A bounded alternative can use a separately authorized observed current UAT 010 inventory, labelled **ObservedCurrentUat010**, plus a reviewed per-facet migration-contract ledger and application relational-model export. Independent review must resolve each expected/observed difference and unrepresented facet; comparing an observation with itself proves nothing. Source/evidence hashes bind bytes and provenance assertions, not historical truth. Historical 1.5 origin, data lineage, historical compatibility/backfill, grants and migration readiness remain unproven.

Minimum separate Owner risk decision, only if this alternative is needed: accept a specifically enumerated historical-provenance exception for a specifically bounded future release gate, based on independent review of the observed reference and migration/model requirements. It must not rename an observed reference as original 1.5, waive unresolved compatibility risks or authorize migrations/Business UAT. No such decision is requested or granted here. Preparation can be independently audited while actual fidelity remains HOLD.

## Compatibility and completeness risks

SQL Server 2022 execution proves the tested syntax/contract on that provider, not every Azure SQL permission or production configuration. Metadata visibility can hide objects or definitions while leaving nonempty result sets. Before a future inventory, existing identity permission coverage and absence of effective metadata DENY must be independently established for the entire intended scope. A shape-valid inventory is insufficient. Do not grant permissions under this preparation. If a separately reviewed read-only visibility attestation/probe is needed, its exact SQL and invocation count require separate authorization; it must not be silently inserted into R3.

The seven SELECT result sets are sequential observations, not a transactionally frozen schema/data snapshot. Future evidence needs a separately approved no-change observation window or must retain this consistency limitation. Historical counts/high-water marks are sensitive aggregate evidence, not proof of business-row equality or lineage.

The original validator validates SHA syntax; the execution wrapper must bind actual checkout bytes, SQL and validator hashes to the exact approved source. The comparator CLI now binds both evidence files and their declared source to reviewed digests. It preserves significant SQL literal whitespace and prints only bounded MATCH/HOLD, including malformed input/argument errors; low-level in-process facet helpers are not release gates. A valid hash does not establish an approved reference. The 010 constraint token check is only a necessary marker check; it is not a SQL expression theorem prover.

## Restricted evidence handling — unresolved prerequisite

This repository is public. GitHub Actions artifact access follows repository read access, so a raw inventory artifact here is not Owner-only. See [GitHub artifact access](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts) and [SQL metadata visibility](https://learn.microsoft.com/en-us/sql/relational-databases/security/metadata-visibility-configuration).

Full normalized JSON contains schema names, defaults, constraint definitions and historical counts/keys. None may enter public logs, Issue #41, screenshots, public artifacts or exception text. Capture Write-UatSchemaInventory's returned summary instead of printing it. Sanitize failures with the unchanged classifier; never print exception messages, SQL text, identities, tokens or connection strings.

Before any future UAT execution, independently verify one concrete existing Owner-only evidence destination, access list and retrieval path, or an approved recipient-encryption plan with verified key custody. No such destination or key-custody arrangement has been verified in this preparation. Creating infrastructure, identities or new access permissions is outside scope. Proposed retention: maximum seven days in the verified restricted destination, followed by verified deletion; record only source/hash/run/classification in the public Hub. Runner plaintext must be owner-readable only, excluded from upload globs and removed in an always-run cleanup. Retention alone is not access control. This plan is **not approved restricted storage**.

NONDEPLOY artifacts contain repository source and sanitized synthetic test totals only. Fixture metadata, aggregate values and credentials must not be packaged.

## Genuine future release HOLDs

1. Independently approved historical 1.5 baseline / trusted expected 010 reference, or a separate reviewed and explicitly accepted bounded provenance exception.
2. Verified restricted evidence destination, authorized access, encryption/retrieval and retention/deletion procedure.
3. Existing UAT identity's effective complete metadata visibility; no privilege changes are authorized.
4. Separately reviewed exact Full Inventory execution wrapper/source/pins and privacy evidence, plus a NEW one-run Owner authorization. No Full Inventory execution workflow is installed by this preparation.
5. Actual authorized UAT inventory, independent reference comparison and unresolved data/history compatibility. Accepted A+C release gates, migration authorization and Business UAT remain separate.

## Future Full Inventory authorization DRAFT — NOT AUTHORIZED

Authorize ONE new, separately reviewed Full Inventory execution only after all above prerequisites have independently passed. Fill in the exact reviewed Protected SHA/tree, inventory SQL SHA-256, validator SHA-256, execution-wrapper SHA-256, comparator SHA-256, independently approved reference kind/source/digest and verified restricted evidence destination/access/retention. Current reviewed SQL and validator pins are in protected-inputs.json; do not assume future source identity.

Use only the existing approved UAT OIDC identity, fixed repository/ref/source and `sql-fieldvisit-jpe-uat.database.windows.net` / `db-fieldvisit-uat`; exact latest version must be `1.8.0-010`. One reviewed inventory invocation; connection bound 120 seconds, query bound 30 seconds; no retries/rerun/redispatch, no substitute SQL, no DDL/DML/grants/migrations/deployment. Source/hash/target/readiness/privacy/visibility/reference/storage failure means STOP before inventory.

Reuse accepted readiness/privacy controls without modifying or adding Inventory to R3. Online requires no wake. Paused/unknown/unavailable means STOP unless a NEW authorization explicitly and separately permits one connection-only wake and acknowledges free-credit consumption, potential overage and residual Online billing until auto-pause. No wake or billable activity is authorized by this draft. Any added metadata-permission probe must be separately reviewed and enumerated in this authorization; never inferred from prior C1 authorization.

Only sanitized PASS/STOP/classification/source/SQL/validator hashes and restricted evidence digest may be published. All schema/aggregate evidence stays in the verified restricted destination. Success does not independently prove historical 1.5 reconstruction, migration readiness or Business UAT GO.

## Independent READ-ONLY Delta AUDIT checklist

- Verify Protected baseline SHA/tree and new branch ancestry; inspect exact PR delta and source tuple.
- Recompute protected-inputs ledger, unchanged R3 paths and classifier hashes; no application/migration/authentication changes.
- Inspect original real-SQL failures and final disposable contract evidence; distinguish harness correction from demonstrated source defects.
- Check seven result sets/facets, wrong targets/version/permissions/shape, syntax/unsupported classification and visibility-gap characterization.
- Check comparator malformed/missing reference, exact source/content binding, facet drift and literal whitespace tests; no metadata in CLI errors/output.
- Verify CI has only contents:read, local disposable SQL and synthetic credentials; no Azure/OIDC/environment/dispatch/deploy capability.
- Inspect artifact ZIP contents and digest; no raw fixture or UAT evidence, credentials or identifiers.
- Confirm provenance/reference/storage/permission HOLDs remain explicit and future authorization is NOT AUTHORIZED.
- Confirm no shared SQL, wake, grants, Azure configuration, Protected/main merge or privileged dispatch occurred.

## Impact and evidence interpretation

Changes are limited to a separate one-token Inventory SQL compatibility candidate, comparator CLI binding/privacy/precise string comparison, disposable proof harness/workflows, immutable pins and this source/gate documentation. No accepted R3 source, original Inventory SQL, classifier, application, authentication or migration is changed in the final delta. The candidate must be independently reviewed; keeping the original file does not certify its execution compatibility.

Real SQL evidence includes exact seven sets/facets/aggregates, original syntax rejection, target/version/column/table/permission failures, result-shape rejection, safe provider classification, unsupported suppression, drift and a low-privilege partial-catalog case. The latter intentionally demonstrates why validator shape alone cannot attest completeness. A normalized real-provider fixture also passes the strict comparator; it proves interoperability, not an independent reference. Synthetic expected/actual tests cover binding, malformed evidence, differences, each relational section and privacy-negative CLI outcomes.

Exact final source tuple, CI run IDs, test totals and downloadable artifact digest belong in the Draft PR completion record and immutable CI source.txt/JSON. Earlier failed characterization runs remain preserved; the final exact-source run is authoritative for the delivered candidate. No future UAT workflow has been installed, scheduled or dispatched.
