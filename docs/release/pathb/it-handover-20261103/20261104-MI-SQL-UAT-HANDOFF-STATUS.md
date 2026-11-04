# Path B Managed Identity SQL gate and solo-Owner UAT handoff — 2026-11-04

**Classification:** pre-UAT technical handover addendum / documentation only / NOT an execution authorization.

## Verified evidence and source of truth

- Canonical Issue #41: https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6628856349
- Offline approved read-only probe evidence: https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6628824892
- Owner-reported system-assigned Managed Identity ON: https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6628755675
- Approved application source SHA: `d6c220877b9976b6478d85b5171d4e06293595ae`.
- Target App: `api-fieldvisit-pathb-uat`.
- Target SQL server/database: `sql-fieldvisit-jpe-pathb-uat.database.windows.net` / `db-fieldvisit-pathb-uat`.
- Exact nondeploy ZIP SHA-256: `2f717884f629547d11b17f8bd77537939a6dc0d9392f88f9b7d292aab14a2b51`.
- Exact embedded Python SHA-256: `dca1689a18302c990f613169692629ff35b0d2ce9d6cfd96262455e5c598e56a`.

## Evidence state (not inferred from offline checks)

| Gate | State |
| --- | --- |
| Original probe ZIP SHA-256 and CRC | PASS offline |
| Python content SHA-256 | PASS offline |
| App system-assigned Managed Identity | ON, Owner reported |
| Authenticated live App container and safe exact-script transfer in this session | HOLD |
| MI token acquisition | NOT RUN |
| Private SQL TLS login | NOT RUN |
| One fixed metadata SELECT | NOT RUN |
| App MI effective SQL permissions | UNKNOWN |
| 69 tables, three SchemaVersions and fingerprint | NOT VERIFIED |
| Canonical 25-case Business UAT | NOT RUN |
| Production/enterprise Entra SSO | DEFERRED to receiving IT |
| Live DDL or deployment | NOT AUTHORIZED by this read-only approval |

One-time MI probe approval was **unconsumed in the documented session**: zero MI token requests, zero SQL connections, zero SELECT calls. Before any new session acts, reconcile whether a different session has consumed it. A verified offline artifact is not evidence of a deployed remote script, live SQL login or successful SQL provisioning.

## Minimum next executable decision

1. Only a genuinely authenticated Azure-capable session may inspect the existing runtime channel and verify the exact target, script byte SHA and a previously approved secure staging method. A missing channel or unverified transfer is **HOLD**, not permission to invent a replacement.
2. Under the existing bounded read-only authorization only, make at most one MI token request, one encrypted connection and one fixed SELECT. Suppress raw credentials, identities, connection strings and error text. Record fixed labels to Issue #41 and independently read back.
3. A negative or UNKNOWN login/permission result does **not** authorize creating a database user, granting a role or installing DDL. Never grant `db_owner` or `db_ddladmin` to the long-running API MI merely to simplify migration.
4. Independently verify fresh actual database state, installation-once markers, SQL private path and viable one-shot original Installer mechanics. A distinct exact-source/target Owner authorization is required before any DDL. The frozen 14-layer, 19-call installation constraints and fail-closed UNKNOWN policy remain binding.
5. After verified schema installation: scoped runtime ACL, signed activation, paired API/Frontend deployment, three demo actors, role isolation and 25 evidenced UAT cases. Nothing unrun may be marked PASS.

## Independent IT delivery track

PR #85 source/schema/configuration/SOP/UAT tracker is a **pre-UAT technical handover candidate**. Seven accepted GitHub Actions artifacts were reportedly downloaded and verified (Issue #41 comment #6628420378), but their durable possession in Owner-controlled storage is not attested here; original artifact expiration is reported as 2026-11-26 UTC. The technical package may be reviewed or transferred while runtime UAT remains blocked. It must not be called a completed database installation or final IT acceptance.

The historical `DELIVERY-STATUS.md` represents an earlier gate snapshot; this dated addendum supersedes its old wording on live Portal access and MI readiness, without modifying its original checked bytes.

**Integrity notice:** This file is new and is NOT included in the original `checksums.sha256` or the earlier supplemental Git blob index. At final handover, generate and independently validate a complete new versioned manifest at the frozen exact HEAD. Do not treat original checksums as covering this new addendum.

**Scope of this change:** documentation only; no Azure access, MI token, SQL, Blob, Marker, Installer, DDL, runtime ACL, deployment, CI rerun, or PR merge.
