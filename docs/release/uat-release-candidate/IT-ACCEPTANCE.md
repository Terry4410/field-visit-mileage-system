# UAT Release Candidate — deployment preparation and IT acceptance

Default: NONDEPLOY; all packaged API/Frontend feature gates OFF. No live approval is conferred by these files.

The immutable accepted install/recovery evidence is SHA 39c86f03cc8d66881a78a800ded974b2f338cf07, Run 39981135659, artifact 12513786826. The focused release job consumes its hash-verified thirteen SQL layers as a fixture, without rerunning that installer certification or its old business probes. It adds only source-verified 018/020 capabilities and focused browser/SQL/trust tests. 013 remains reserved. No historical migration rows are fabricated.

## Source and schema

Verify checksums.sha256, release-manifest.json, release-contract.json, source overlay manifest and all package manifests before acceptance. The final object manifest schema-contract.json covers all 69 installed tables: ordered columns/types/lengths/precision/nullability/identity/computed/default expressions, keys/indexes/filters, trusted CHECK/FK definitions/actions and enabled trigger definitions/SET options. The complete JSON SHA-256 binds the exact SQL target schema to the API and Frontend. This is the installed candidate object manifest; frozen-case results are reported separately, never inferred from its size.

source-code-immutable-rc.zip is the unchanged accepted application source. release-source-delta.zip contains the exact PR's deterministic source overlay, runtime trust implementation, UI delta, tests, linked accepted fixture sources, Python compiler dependencies and workflow. Reproduce by extracting the RC source, verifying its pinned git SHA (or supplying the pinned source ZIP via --rc-archive for a fresh extraction), applying prepare_candidate.py, publishing the API and building the normal Frontend with VITE_RELEASE_SOURCE_SHA set to the candidate commit. Original RC and derived candidate are distinct.

## Standalone new-empty installation

install.py defaults to a no-connection hash-only dry run. standalone-install-manifest.json binds fourteen ordered layers and separately derived exact-UAT SQL. The UAT derivatives preserve every object/constraint and replace only two known disposable target guards with explicit db-fieldvisit-uat plus Azure SQL engine guards; every layer also has that target assertion. The caller fixes the TLS-authenticated endpoint to sql-fieldvisit-jpe-uat.database.windows.net; arbitrary DB/host and nonempty DB are rejected. Input or original guard drift fails closed.

Actual execution needs separate target-specific Owner approval, an already-empty approved database, the manifest SHA injected as FIELDVISIT_APPROVED_INSTALL_MANIFEST, and an IT-controlled installation identity. Passwords use SQLCMDPASSWORD environment injection, never command arguments or files. UAT uses certificate validation and optional sqlcmd Entra installation authentication; application login remains Demo test accounts. No reset, deletion or existing-DB upgrade is implemented.

A failed partial new install stops immediately; preserve sanitized stage evidence. Do not rerun on a nonempty target or inject historical ledger rows. Recovery for an existing UAT target requires a separately inspected restore point and approved API+Frontend+DB pair. Reuse the accepted COPY_ONLY/CHECKSUM/VERIFYONLY/RESTORE/CHECKDB exercise; it certifies the disposable source schema, not a live Azure restore point.

## Runtime identity and activation

Application identity is separate from installer/backup identities. Grant object-scoped DML and VIEW DEFINITION only on required application tables; deny writes to SchemaVersions/SchemaMigrationDataBaselines; no db_owner, ALTER or CREATE TABLE. Metadata visibility is required by the guard. Complete target-specific effective permission acceptance after separate live authorization.

UAT runtime trust requires all of: fixed endpoint+database; Azure SQL engine; TLS with certificate validation; actual current database principal SID hash/name; non-owner/no-DDL effective permissions; matching complete schema fingerprint; actual running Api/Application/Domain/Infrastructure assembly composite SHA; exact Frontend ZIP SHA and candidate source SHA; exact feature set; and a bounded signed attestation. HMAC verification uses a separately injected 32-byte-or-longer secret, never packaged. Missing/expired/wrong signature, target, identity, DDL, binary, release or feature set denies execution. Production and unknown targets remain denied.

sign_activation.py prepares an eight-hour attestation only from the paired contract and separately inspected facts. It never connects or enables anything. Key/attestation injection, OPTIONC_EXECUTION_MODE=Uat, API Candidate enablement, FIELDVISIT_UAT_ACTIVATION and the source-matched Frontend runtime switch all require later explicit activation approval. Neither a mode nor a UI flag grants SQL access. The normal Frontend binary is tested with a loopback-only runtime config in CI; the packaged config stays OFF.

## Live gates before Business UAT

After separate authorization: verify exact deployed API/Frontend/source hashes, target SQL fingerprint/capabilities, actual least-privilege principal, restoration boundary and test-account scopes; approve controlled activation; execute physical iPhone and actual deployed three-role Browser→API→SQL cases. CI mobile viewport/WebKit is not physical iPhone evidence. Business UAT signoff and full 25-case disposition remain separate from technical candidate readiness.
