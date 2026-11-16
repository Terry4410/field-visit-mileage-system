# Field Visit Path B — pinned G1 source-only delivery review (NONDEPLOY)

**Decision: STATIC SOURCE-DELIVERY CANDIDATE ONLY; G1 LIVE = HOLD.**

## Scope and immutable baseline

- Existing NONPROTECTED base: PR #88 head `73ad013196957b05d08baa385f8b8c704ef73058`
  (`work/pathb-g1-ssh-trust-delta-nondeploy-20261113`).
- Release source: `d6c220877b9976b6478d85b5171d4e06293595ae`.
- Frozen original installer: SHA-256 `b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f`.
- Frozen preflight SQL: SHA-256 `e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add`.
- Original SQL package expected SHA-256 `5a0c956d4ef5c88e0ff49d2d606ee9c1cb34e81e7f0c0f85b5aea8c5743e5828`.
- Original SQL seal expected SHA-256 `a37dc3c7fbf1f805639fb72a76cc788bd9bec1f0f9933e07909e2eacae5c4a5f`.
- Existing installer contract: up to 19 SQL calls, 14 immutable SQL layers, expected 69 user tables and 3 `SchemaVersions` markers. These counts are **expectations**, not live installation evidence.

## What this delta does (offline only)

1. `g1_pinned_source_distribution_nondeploy.py` builds **only** the exact source-only archive accepted by existing `p3_kudu_cloudshell_parent.validate_distribution_archive`: the pinned `binding-index.json` and its listed files, unchanged.
2. Fail closed on wrong release, contract, target, original installer/package/seal pins, SQL call ceiling, file hashes, missing members, symlinks, oversized entries, or existing output. Deterministic ZIP byte order, timestamps and modes; never overwrite.
3. Native-parent offline acceptance tests ensure the generated ZIP matches the existing G1 parent validator; negative tests cover tampered immutable SQL, installer, target, missing file, symlink, duplicate/extra archive entry, and output overwrite.
4. A scoped GitHub Actions job tests and builds this source-only artifact without secrets, Azure login, SQL clients or deployment steps. **Do not treat successful CI as live readiness.**
5. No original Blob ZIP or seal is embedded in GitHub: the original package must later be fetched and hash-verified through a separately reviewed/authorized process. No synthetic SQL is a substitute.

## Independent local/Portal evidence supplied by Owner on 2026-11-16

- API v1.8.0 live `/health/live` PASS, Browser CORS and forced preflight PASS, background worker disabled.
- Owner's one-time Entra portal SELECT returned `DatabaseName=db-fieldvisit-pathb-uat`,
  `UserTableCount=0`, `SchemaVersionsExists=NO`, `AppMiNameMatch=NO_MATCH`,
  `AppMiRoleCount=0`. These are **catalog-visible results only**; not a fresh
  installer pre-DDL gate nor a proof of which identity caused `/health/ready=503`.
- Owner confirms SQL server Public Network Access was restored to **Disabled** after the authorized temporary single-IP rule. This document does not independently inspect the firewall rule list.
- The single `/health/ready` call returned 503 and cannot be replayed under its consumed authorization.

## Security gates retained, not waived

| Gate | Decision |
| --- | --- |
| Frozen installer, original 14 SQL layers, pre/postcheck and adapter | UNCHANGED; source hashes checked |
| Package/seal actual bytes available offline | NOT AVAILABLE — no fabricated full SQL ZIP |
| Source-only ZIP exact entries and digests | Offline test and Actions artifact required |
| Existing Kudu transport and token non-echo | Historical static review only; LIVE HOLD |
| Old consumed G1/SELECT22 claims | NEVER replay, clear or modify |
| New once-only Blob marker / independent provenance | NOT CREATED OR AUTHORIZED |
| Target App + instance SHA / Owner credential + SQL privilege | LIVE UNVERIFIED |
| SQL PNA Disabled / firewall_count 0 | Owner reports Disabled; full live gate must verify |
| Exact private DNS, TLS, DB identity and latest empty-catalog preflight | LIVE UNVERIFIED |
| API Managed Identity SQL database principal and least privilege | UNKNOWN; no `CREATE USER` authorized |
| Installation DDL, deployment, Azure settings, token or SQL operations | ALL FORBIDDEN in this PR |
| G1 live authorization and Business UAT | NOT GRANTED; 0/25 |

## Remaining true blockers before a new *separate* live G1 authorization

1. Review source-only CI-produced ZIP SHA-256, binding SHA-256 and native-parent acceptance; retain both as immutable candidate identifiers.
2. Verify availability and exact hashes of original package and seal using approved existing artifact sources, without copying secrets or SQL package into a public PR.
3. Independently resolve actual G1 execution transport/operator identity and durable once-only marker safety. Existing App MI being ON is **not** evidence of database permissions; the original G1 Owner Entra operator path has its own credentials and one-time controls.
4. Re-check current SQL catalog in the **same** authorized installation flow immediately before DDL, as required by the frozen contract. Never reuse prior SQL/Claim authorizations.
5. Obtain fresh, specific Owner approval for that exact delivery SHA, binding SHA, target and instance, duration, at-most-once installation, and explicit SQL/Blob/Token operations; if ANY prerequisite is uncertain, STOP at HOLD.

**No live execution is requested or implied by merging/approving the offline artifact.**
