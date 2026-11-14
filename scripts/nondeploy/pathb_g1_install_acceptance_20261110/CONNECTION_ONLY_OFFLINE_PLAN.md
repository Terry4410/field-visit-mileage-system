# Path B — connection-only feasibility (NONDEPLOY)

Status: **DESIGN ONLY / NO LIVE AUTHORIZATION**. Based on PR #88 commit `73ad013196957b05d08baa385f8b8c704ef73058`.

## Scope and current evidence

- Target: existing `rg-fieldvisit-uat/api-fieldvisit-pathb-uat` private-network container; SQL `sql-fieldvisit-jpe-pathb-uat.database.windows.net/db-fieldvisit-pathb-uat`.
- Last observed database state: Serverless **Paused**; schema **UNKNOWN**. No inference of empty or installed schema.
- Container inventory: `sqlcmd`, `isql`, `tsql`, `pyodbc`, `pymssql` all **ABSENT**.
- Portal Query Editor blocked because public network access is disabled; **do not enable public access**.
- Existing G1 `p3_app_installer_runner.py` defines `stage_tools(cfg)`, which verifies SHA-256 of five pinned Debian packages, extracts them under a private tool root, validates binary hashes and prepares ODBC environment. This is **not** proof that packages are present in the container or that tool staging succeeds there.
- The same runner's `sql_call()` executes supplied SQL; its `run()` has authorization, preflight, tokens, marker and installer logic. **Neither may be called for connection-only**.
- Existing Parent `p3_kudu_cloudshell_parent.py` live mode builds an installation stage and is not a connection-only entrypoint.

## Separation contract (offline only)

1. Reuse the *design* and pinned hashes of `stage_tools()`, but never invoke `run()`, `sql_call()`, `install.py --execute`, live Parent or any installation authorization.
2. An isolated staging candidate must accept only five local pinned `.deb` files; verify each SHA-256 **before** extracting; verify every extracted executable/library hash **after** extraction; fail closed on missing files, unexpected paths, links or mismatches.
3. No package-manager installation, network download, App configuration change, deployment, Azure access, SQL Token, SQL connection, SQL query, DDL/DML, Blob or marker operation during offline work.
4. Stage only in a newly owned temporary directory; avoid fixed paths and collisions. Clean only owned temporary content; no recursive deletion of unknown paths.
5. A subsequent **separately authorized** live connection design must prove its client performs one authenticated login and close with **zero SQL statements**; an interactive `sqlcmd` prompt alone is not proof. Explicitly assess SQL token acquisition and any driver/session initialization statements. No automatic retry even on Serverless transient failure.
6. **Do not issue a live command** until artifact identity, environment, identity, network, token scope, exact single-attempt accounting, timeout and output suppression are independently reviewed and Owner approves the exact live procedure.

## Offline acceptance matrix

| Gate | Required result |
|---|---|
| Frozen source and original 14 SQL layers unchanged | PASS |
| Five `.deb` files available as exact pinned hashes | PASS, else HOLD |
| All extracted binary hashes match `executor_config.json` | PASS, else HOLD |
| No call path to installer, `sql_call()`, Azure, Token, Blob or SQL | PASS |
| No automatic retries or SQL statements in future proposed login | PROVEN, else HOLD |
| Cleanup limited to owned staging paths | PASS |
| Live connection | **NOT RUN** |
| Schema verification and Business UAT | **NOT RUN** |

## Decision

Current verdict: **HOLD_LIVE**. The offline source-level separation is feasible; actual package availability and a demonstrably statement-free SQL login are not yet verified. A source-level plan does not authorize Azure access, SQL token requests, connection attempts, or installation. Preserve PR #88 and its passing CI without rerunning.
