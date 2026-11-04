# Path B operational evidence addendum — 2026-11-04 (Asia/Taipei)

> Status: **HOLD / NONDEPLOY**. This addendum does not authorize SQL DDL, App deployment, Azure resource changes, or a repeat of consumed one-time executions. Read it together with the original nine-file checksummed handover set. It supplements, rather than replaces, that frozen 2026-11-03 manifest/checksum inventory. The addendum itself is intentionally not asserted to be included in those earlier checksums.

## Current verified facts

- Exact approved application source: `d6c220877b9976b6478d85b5171d4e06293595ae`.
- Exact isolated SQL target: `sql-fieldvisit-jpe-pathb-uat.database.windows.net` / `db-fieldvisit-pathb-uat`. The legacy `db-fieldvisit-uat` and Production are forbidden.
- The sealed SQL package and 14 original installation layers remain frozen. The expected installation result remains 69 tables, three exact `SchemaVersions` markers and trusted/visible constraint/trigger/schema fingerprint checks; **none has been verified live for Path B**.
- Existing official Azure Portal App Service > Development Tools > SSH browser terminal on `api-fieldvisit-pathb-uat` reached a ready container shell and closed safely (four PASS gates): [Issue #41 #6627113020](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6627113020). That authorization was consumed.
- An Owner-operated one-time **synthetic, non-DDL** Portal shell test reported seven PASS gates (fixed marker, 29-byte frame, SHA-256, `/tmp` stage, readback and cleanup). The pasted terminal transcript contained mixed/interleaved text; therefore the observed output is **Owner-reported**, not independently attested command bytes or cleanup: [#6628036067](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6628036067). That authorization was consumed.
- A one-time Kudu CLI `az webapp exec --mode shell --target app --instance <verified-one>` capability proof passed Owner, CLI, syntax, target and unique-instance checks, but supplied **no verifiable remote whitelist labels** and ended `REMOTE_RESULT_UNKNOWN`: [#6627867384](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6627867384). Do not claim that synthetic frame staging or cleanup was completed, or delete any unknown leftover files. That authorization was consumed.
- Earlier password/PTY-based exact Launcher failed closed at `APP_SSH_LOGIN;UNCLASSIFIED_HOLD`, without SQL/DDL: [#6624864733](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6624864733). Subsequent SSH login-only proof showed `PROMPT_HOLD` [#6624998127](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6624998127). Neither may be replayed.
- The offline CLI/Kudu adapter candidate was tested 248/248 PASS, but **does not prove** live transport completion or a fully integrated safe one-time installation executor: [#6627689004](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6627689004).
- A separate 21-gate conservative NONDEPLOY assessment helper and Owner runbook were recorded [#6628185881](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6628185881); neither is an execution or SQL authorization.

## Exact remaining critical path to Business UAT

1. **Do not spend another DDL attempt on transport diagnostics.** A deterministic, bounded Portal-compatible transport/command-completion path must first be independently reviewed and offline-tested against the unchanged original installer, Seal, ordered layers and token handling. **Portal browser shell availability does not prove safe transfer of secrets or exactly-once DDL control.**
2. With separate Owner permission and suitable authenticated private network channel, gather fresh read-only evidence: exact Azure/SQL identity and roles, DNS/TLS/private-only, Storage and SQL Token audience/TTL, two current historical SELECT22 Blob contents/ETags (no historical-immutability claim), 22-field empty SQL catalog, and both once-marker states. A prior failed run does not establish current emptiness.
3. Only if every real-time preflight passes and a newly integrated, exact-SHA one-shot Executor is reviewed, obtain **new, explicit exact-SHA Owner authorization** for at most 19 SQL calls (1 preflight + 16 installer + 2 postchecks). On any timeout, partial install or UNKNOWN commit STOP, do not retry/reset/delete.
4. Verify the 69 tables, three exact markers, constraint/trigger trust and schema fingerprint. Without these outputs: `SQL_INSTALL=HOLD`.
5. Under separate permissions complete object-scoped runtime SQL ACL, signed activation, paired exact API+Frontend deployment, three Demo Visitor/Leader/Admin actors and Browser → API → SQL smoke. Only then open the canonical 25 Business UAT cases.
6. Execute all frozen 25 tests and retain tester/time/sanitized data/evidence for every actual result. Do not substitute CI/offline checks for Business UAT PASS. Include Google Routes success and explicit fallback and physical iPhone scenario.
7. After UAT results, reconcile the IT handover manifest and documentation; include actual final source, schema, access controls, environment inventory, backups/recovery and unresolved risks. Enterprise Entra SSO remains for receiving IT.

## Integrity and operator boundaries

- Original handover `handover-manifest.json` and `checksums.sha256` remain **unchanged**, to preserve their 2026-11-03 evidence. This new addendum is supplementary and is not covered by those nine digest entries; future final release must generate a **new** versioned complete integrity manifest rather than silently presenting old checksums as covering this file.
- No secret, access token, SSH password, SQL connection string or SAS should be included in this or future GitHub documentation.
- No previously consumed one-time authorization may be reused. This addendum is **documentation only** and not approval for any Azure, SQL, SSH, Blob, Marker, ACL, deployment, or PR merge operation.

Owner context: one-person UAT operation with personal Azure and GitHub environments, no local software installation. Preserve accepted CI and avoid new Azure infrastructure.
