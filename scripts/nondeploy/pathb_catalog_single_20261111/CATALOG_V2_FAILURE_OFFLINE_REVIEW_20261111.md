# Field Visit Path B — Catalog V2 NONDEPLOY failure review (2026-11-11)

## Exact observed result
- App: `api-fieldvisit-pathb-uat`
- V2 approved commit: `12204f802eae1649ae63e93a2bc806f1904b9645`
- V2 source: `scripts/nondeploy/pathb_catalog_single_20261111/catalog_once_linklocal_v2.py`
- Observed: `CATALOG_V2_SOURCE=PASS`, `CATALOG_ATTEMPT=CONSUMED`, `SQLCMD_DISPATCH=ONE`, `CATALOG_QUERY=HOLD`, `STOP_CLASS=SQL_AUTH_CONNECTION_OR_QUERY_HOLD`, `CATALOG_V2_EXIT=1`.
- No `FV_CAT` receipt observed. SQL server state UNKNOWN; do not infer empty/full/partial. Business UAT 0/25; G1 HOLD.
- V2 authorization consumed. DO NOT REPLAY this attempt or delete/reset the local claim.

## Verified root limitation in V2 code
1. Line ~130 uses `subprocess.run(argv,capture_output=True,...)` to capture SQLCMD stdout/stderr and return code.
2. Line ~132 raises one generic `SQL_AUTH_CONNECTION_OR_QUERY_HOLD` for any nonzero return code, discarding the stdout/stderr and even the numeric exit status. Thus the existing receipt cannot identify ODBC registration, TLS/network, SQL authentication, missing DB, or SQL syntax errors.
3. `finally` unlinks the temporary token, SQL, and ODBC registration files. No stored child-output artifact is available from this program. Claim records attempt consumption but not root cause.
4. The V2 run reached `SQLCMD_DISPATCH=ONE`. This proves a subprocess launch was attempted, but not that SQL was executed on the server; `SQL_CALL_COUNT=0_OR_1_UNKNOWN` remains the correct conservative result.
5. V2 `-G -P <tokenfile>` with a UTF-16LE/no-BOM token file is a documented Linux sqlcmd ODBC mode. Do not claim those flags are the root cause without evidence. The SSH manual help test only tested `sqlcmd -?`, not live authentication.

## Minimal nondeploy remediation — review only, NOT a live authorization
- No new SQL, installer, Azure resources, SSH launcher, or R5/R6 framework.
- Preserve same original Catalog SQL file and exact SHA, original DB/server, source integrity checks, no-retry semantics, and single SQLCMD dispatch.
- In a future separately approved exact-SHA candidate, retain only **sanitized, allowlisted error classes and the numeric child return code** in the final receipt. Do not print/store raw stderr/stdout, token, SQL source, identity strings, ODBC connection string, or endpoint.
- Derive allowlisted classes *in memory* from captured child output, e.g. `CLIENT_DRIVER_OR_DSN`, `TLS_OR_CERT`, `NETWORK_OR_TIMEOUT`, `SQL_LOGIN_OR_PERMISSION`, `SQL_SYNTAX_OR_OBJECT`, `SQLCMD_USAGE`, `UNCLASSIFIED`; never echo matched substrings. Treat ambiguous matches as `UNCLASSIFIED`.
- Perform deterministic offline tests with synthetic failure outputs and verify the same one-shot guard (one token, one sqlcmd, no retry, sensitive data redacted). Only then consider a NEW, separately authorized live readonly observation.
- Do not execute an unreviewed partial patch with previously consumed authorizations. Neither this memo nor existing V2 authorizes a retry.

## Handoff
Owner chose UAT-first, minimal necessary engineering, no rerun passed tests; keep protected branch unchanged. SQLCMD package readiness passed once (five exact Debian packages, hash 5/5, ldd, help). The only necessary future live purpose is to get trustworthy read-only SQL catalog evidence; no G1 install is authorized here.
