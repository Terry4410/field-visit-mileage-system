# Field Visit v1.8.0 — Path B IT handover package

Package date: 2026-11-03 (Asia/Taipei)  
Application source: `d6c220877b9976b6478d85b5171d4e06293595ae`  
Status: **handover package ready / Business UAT blocked**

This directory is the operational index for IT. The Git commit containing this
directory is based directly on the approved application source, so cloning or
downloading that exact handover HEAD supplies the complete Backend, Frontend,
database, migration, test and workflow source together with these instructions.
No password, access token, Google key, SQL connection string or private key is
included.

## Start here

1. Verify `checksums.sha256` and `handover-manifest.json`.
2. Read `SOURCE-ARTIFACT-INVENTORY.md` for the exact source and accepted binary artifacts.
3. Read `DELIVERY-STATUS.md` before performing any environment work.
4. Use `INSTALL-DEPLOY-SOP.md` only after the applicable Owner/IT approvals exist.
5. Use `BUSINESS-UAT-25-TRACKER.md` for the frozen Business UAT cases; every case is currently `NOT RUN`.
6. Use `RECOVERY-ROLLBACK-SOP.md` for failure and paired recovery decisions.

## Package contents

| File | Purpose |
|---|---|
| `handover-manifest.json` | Machine-readable release, artifact, schema and environment binding |
| `SOURCE-ARTIFACT-INVENTORY.md` | Source, API/Frontend artifacts, SQL package and accepted CI evidence |
| `AZURE-CONFIG-INVENTORY.md` | Existing Path B resources, required settings and secret custody |
| `INSTALL-DEPLOY-SOP.md` | SQL, runtime ACL, activation and paired application deployment sequence |
| `UAT-ROLE-ACCOUNT-SPEC.md` | Visitor, Leader and Admin account/role/isolation contract |
| `BUSINESS-UAT-25-TRACKER.md` | Canonical 25-case execution record |
| `RECOVERY-ROLLBACK-SOP.md` | Stop, rollback, PITR and paired recovery procedure |
| `DELIVERY-STATUS.md` | Completed, pending, blocked and deferred items |
| `checksums.sha256` | SHA-256 for this handover document set |

## Authority and boundaries

- Canonical project record: GitHub Issue #41.
- Accepted source CI: Runs `40438452922` and `40438452961`.
- Latest installation attempt evidence: Issue #41 comment `6594045913`.
- The legacy `db-fieldvisit-uat` and Production are outside this package.
- Production vulnerability scanning, penetration testing, enterprise Entra SSO,
  infrastructure hardening and Production recovery acceptance are IT scope.
- This package does not authorize SQL installation, once-marker reset, deployment,
  secret disclosure or Azure resource changes.

