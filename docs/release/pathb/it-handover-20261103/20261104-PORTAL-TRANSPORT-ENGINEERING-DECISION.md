# Path B P3 — Portal transport engineering decision (2026-11-04)

**Decision: HOLD FOR DDL, NONDEPLOY engineering only.** This document supplements the existing handover; it is not included in the frozen 2026-11-03 `checksums.sha256` and must be incorporated in a versioned final handover integrity manifest. It changes no installer or Azure resource. Owner is operating alone; minimize the number of Azure sessions and one-time authorizations.

## Evidence baseline

- One-time official Portal container terminal availability PASS: Issue #41 [#6627113020](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6627113020).
- Portal synthetic non-DDL proof: seven fixed PASS labels **Owner-reported**, with a mixed/interleaved transcript that prevents independent replay/cleanup attestation: [#6628036067](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6628036067). Consent consumed.
- Kudu CLI one-shot test: exact app, CLI, instance preflights PASS but **REMOTE_RESULT_UNKNOWN**, without remote completion evidence: [#6627867384](https://github.com/Terry4410/field-visit-mileage-system/issues/41#issuecomment-6627867384). Consent consumed. Do not inspect or delete any unknown leftovers without new authority.
- Original P3 Executor ZIP SHA-256 `0be0ae4b53df5380a96f16cb093f7013d60af240114b1bf3562a52eb3f41def6`; offline Kudu NONDEPLOY ZIP SHA-256 `376e954c3a78a22529af55e72a62d72997078edf82a59a304369dc2370cfa547`. Do not replay consumed exact Launcher.

## Code-level compatibility blockers (read from exact frozen original)

1. **SSH binding is material, not decorative.** `p3_cloudshell_parent.management()` explicitly sets `ssh_transport='PINNED_ED25519_OPENSSH_STDIN'`; `p3_app_installer_runner.run()` requires that exact value and sets the `pinned_ssh_stage_verified` gate based on it. Replacing the transport with a Portal browser terminal without creating a separately verified portal-specific evidence contract would violate the original contract. Do **not** simply rename or prefill the SSH marker.
2. **Secret handoff contract differs.** App runner prompts for separate SQL and Storage JWTs using `getpass.getpass()` after `FV_P3_TOKENS_READY`. The parent obtains tokens using Azure CLI and sends them via a controlled PTY after echo is disabled. Portal browser paste is not equivalent to a verified private non-echo token channel; no passwords, JWTs, SAS or SQL text may be pasted into the chat or GitHub.
3. **Original file-stage contract differs.** Parent constructs a bound `stage.zip` containing original sealed ZIP, Seal, tools, config, authorization, and a fresh `live-facts.json`, then wraps the stage as Base64 in an SSH-send Python heredoc. The Portal synthetic **29-byte** test does not prove safe delivery or integrity/readback of this much larger package; naive copy/paste is unsafe.
4. **One-time controls span two channels.** Parent performs Azure management/Blob predecessor checks and establishes SSH/PTY with pinned host key; the App runner validates 22-field empty SQL, fresh DNS/TLS, exact original package/Seal, dry-run, then an atomic create-only Blob marker immediately before the original 16 SQL calls. The 19-call limit (1+16+2), stop-on-UNKNOWN and postcheck must remain intact. Existing parent `finally` contains a **second** tunneled SSH session for independent cleanup; that is unavailable via a one-off Portal text terminal and cannot be silently omitted.
5. **Current identity and state are not implied by prior success.** Live Owner/Tenant, fresh Token audience and TTL, exact app and SQL, SQL empty metadata, two historical Blob current content/ETag and both once markers require fresh verification before any DDL. Prior `REMOTE_RESULT_UNKNOWN` does not prove file cleanup; do not delete unknown files or reset markers.

## Minimal architecture for an eventual approved installer

- **Control split:** use an independently authenticated owner-side coordinator to verify exact Azure target, Blob history, pinned Seal and package, and acquire fresh SQL/Storage tokens **without writing them to a browser transcript**. Use a separately reviewed, byte-bound Portal-compatible container receiver only if both one-instance delivery and secret-free terminal behavior can be demonstrated. If this cannot be demonstrated, **HOLD rather than ship a browser-paste DDL script**.
- **Transport attestation:** a fresh explicit evidence field such as `PORTAL_AUTHENTICATED_CONTAINER_STAGE_V1` is not interchangeable with `PINNED_ED25519_OPENSSH_STDIN`; it needs a distinct verifier, bounded transport receipts, staged-byte digest and exact app instance association. Reject absent/ambiguous receipts. Do not hardcode PASS.
- **One-shot execution contract:** preserve the original runner's live SQL preflight, encrypted private SQL/TLS check, zero user-object requirement, durable create-only installer marker, strict SQL budget and postchecks. A failed/partial/unknown commit must permanently HOLD; no retries, no automatic rollback or marker clear.
- **Cleanup:** must only remove uniquely proven trial-owned files. Irreversible marker and installation evidence must never be cleaned up as temporary files; unknown legacy temp paths stay untouched.
- **Proof stages:** first offline tests for all new state transitions and hostile transcript/error/Token cases; only then separately authorized bounded non-DDL live proof for the *actual integrated transport*, not another trivial connectivity test. Request an exact SHA one-shot DDL approval **only after** those gates pass.

## Practical single-Owner critical path

1. Finish the above NONDEPLOY transport and Token security design; publish exact SHA and offline tests; disclose remaining blockers plainly.
2. One separately approved fresh read-only App marker + SQL 22-field catalog check over a valid private channel; if the private channel cannot prove Owner identity, do not query SQL.
3. New independent Owner approval for **one** exact integrated SHA DDL attempt, at most 19 SQL calls; otherwise maintain HOLD.
4. Verify 69 tables, three `SchemaVersions` markers, constraints/triggers/fingerprint.
5. Separately approve runtime ACL, signed activation and matching API/Frontend UAT deployment. Establish three Demo accounts and three-role smoke before releasing UAT URL.
6. Owner executes frozen 25 Business UAT cases; preserve actual evidence. Complete PR #85 handover manifest after UAT and provide IT with source/schema/config/recovery, deferring Enterprise Entra SSO to IT.

**No Azure, SSH, Blob, Marker, SQL, DDL, ACL, deployment or installer action is authorized by this document.**
