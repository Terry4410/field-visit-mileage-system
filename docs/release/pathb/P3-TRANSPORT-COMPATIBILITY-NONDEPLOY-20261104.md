# Field Visit Path B — P3 transport compatibility, constrained nondeploy review (2026-11-04)

**STATUS: HOLD; NO AZURE/SQL EXECUTION.** Owner approval: engineering assessment and minimal NONDEPLOY transfer-only fixes on independent GitHub branch. This document is evidence and a stop decision, **not** a released transport implementation or installation authorization.

## Scope binding

- Approved app/source base: `d6c220877b9976b6478d85b5171d4e06293595ae`.
- Target app: `api-fieldvisit-pathb-uat`.
- Target database: `sql-fieldvisit-jpe-pathb-uat.database.windows.net` / `db-fieldvisit-pathb-uat`.
- Original frozen SQL/Seal/14 layers untouched, `19 = 1 preflight + 16 installer + 2 postcheck` call cap, durable install-once marker, fail closed on partial/UNKNOWN, no auto retry.
- Do not fake original `PINNED_ED25519_OPENSSH_STDIN` proof; replacement transport needs an independently bound exact-evidence contract with separate explicit Owner approval.

## Source discovery — important correction

The P3 parent/runner code **is not tracked inside the approved application source Git tree** (verified via the exact GitHub tree at the approved SHA). The original archived Python source is preserved as independent Library ZIPs. Thus neither a direct edit to `p3_cloudshell_parent.py` nor an in-place “small PR patch” can be claimed without first importing and re-binding the audited source.

Actual private original archive verified in this review:
- `pathb-P3-FINAL-EXECUTOR-SSH-SHORTWRITE-NONDEPLOY-20261103.zip`: SHA-256 `1247d40ca4f6ddbb8879c80f45125f6d4385f56755b9d541be2b726d2fa0ead8`; includes `p3_cloudshell_parent.py`, `p3_app_installer_runner.py`, sealed installer **snapshot**, gates/marker guards and tests. It excludes actual 14 SQL layer payload bytes.
- `pathb-P3-KUDU-TRANSPORT-NONDEPLOY-20261104.zip`: SHA-256 `376e954c3a78a22529af55e72a62d72997078edf82a59a304369dc2370cfa547`; transport-only candidate, historical 248/248 offline PASS including original tests, explicitly has no live entrypoint.
- `PathB_P3_Kudu_Stage_Hardening_NONDEPLOY_20261104.zip`: SHA-256 `035392fb85ddc29f465c889706ef9df19b6274719ee1a6a5c57f0f9fc5d0d656`; contains `p3_kudu_transport.py` and `test_p3_kudu_transport.py`, with hardening for ZIP aggregate expansion, file/child collisions and pre-write CRC check.

### Independently reproduced in current private offline container

```
cd <extracted hardened archive>
python -m unittest -q test_p3_kudu_transport.py
Ran 36 tests in 5.051s
OK
```

Hardened `p3_kudu_transport.py` SHA-256: `804219c9d3ffc9de9b298cde5429687355412ef7f3e9275b96ecff137574b5ef`; 489 lines. This is an **offline-only** PASS. No official Azure Portal / Kudu or SQL call occurred in this review.

## Exact blocking technical facts

1. The hardened module is an import-only adapter; it **has no integrated trusted Owner CLI parent and no DDL execution entrypoint**. Historical original runner binds a different pinned SSH transport, and neither byte-bound staging nor token/PTY equivalence is proven against the frozen original installation.
2. The candidate still contains `REMOTE_STAGE = "/tmp/fv-pathb-p3-kudu-transport-v1-20261104"`. The earlier Kudu session ended with `REMOTE_RESULT_UNKNOWN`; reusing that static path risks collision or unknown prior state. A newly validated per-attempt stage ownership and cleanup receipt is required, without touching unknown leftovers.
3. Authentication and authorization are not established by CLI flags alone. Owner, target App single-instance pin, token privacy, archive bytes/Seal, two predecessor Blob states, once marker, exact SQL preflight and unknown-stop all require independent evidence before any real installer action.
4. The existing App Service lacks verified production-ready SQL client tooling; offline Kudu transport tests do not resolve ODBC/pyodbc or substitute a secure private SQL client.
5. For this limited scope, importing the incomplete adapter into an application branch and presenting it as runnable would mislead the Owner. The only safe decision is HOLD before code integration or runtime execution.

## Delivery-first decision

- **Do not** rewrite the 14-layer Installer, alter source package hashes, rename/spoof the old SSH attestation, replay used launchers, install OS packages, change Azure, obtain tokens, contact SQL, run DDL or merge protected branches.
- Treat the archived hardened candidate as reusable **evidence**, not a newly authorized deployable package. Avoid another duplicated test suite or redesign.
- Any next engineering attempt needs explicit, finite acceptance of stage-uniqueness, durable receipt, equivalently secure token handoff, original runner integration, and prerequisite trusted SQL client. Unless these can be proved without new infrastructure or core modification, keep SQL/UAT HOLD.
- IT pre-UAT source, artifact and schema handover in PR #85 can proceed separately; three roles / 25 cases remain NOT RUN.

**This branch changes documentation only.** No original code or existing protected branch changed and no Azure infrastructure or data was accessed.
