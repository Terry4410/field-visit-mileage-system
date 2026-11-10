# Field Visit Path B — single G1 original-installer execution control candidate

**STATUS: NONDEPLOY / LIVE OWNER AUTHORIZATION NOT ISSUED / BUSINESS UAT NO-GO**

Purpose: complete one bounded integration candidate for G1 SQL installation *control* using the already approved frozen 14-layer SQL payload. Not a new R5/R6/MI diagnostic, and not a revision of the frozen SQL, release CI, API or Frontend. This package does **not** contain the private sealed original SQL ZIP or an Owner credential. It is not a live GO decision.

Approved source SHA: `d6c220877b9976b6478d85b5171d4e06293595ae`.
Consumed v3 source archive (read-only reference): `95f973e5f6401285b45c76df98b10404ea055df23e0e86a465b7162c4061d0fa`. Never replay v3 or any prior one-shot.
Original `frozen_install.py.snapshot`, `token_adapter.py`, `preflight.sql`, `postcheck.sql`, `schema-fingerprint.sql` retain their original pinned SHA-256 in `p3_install_gates.py` and pass exact byte validation. The original 14 SQL files remain in the separately retained Azure Blob original sealed package.

## Single integrated control delta

1. **Owner and channel:** Existing `rg-fieldvisit-uat/api-fieldvisit-pathb-uat`, exact one App instance and owner/subscription validation. Official `az webapp create-remote-connection` (not `az webapp exec`), dynamic Azure CLI-provided SSH password in ephemeral process memory only, exact ED25519 fingerprint and strict host key; no static password or new infrastructure. PTY echo and canonical buffering disabled before SQL/Storage Token transport.
2. **Pre-DDL:** The original SELECT22 private SQL identity/TLS/empty-schema check and original installer dry-run occur before any DDL claim. Fresh operation ID, artifact, instance, and 22-field gates bound into a *new* create-only Blob marker `operator-once/pathb-g1-original-installer-single-20261110.json`. The consumed v1/v2/v3 markers cannot be deleted, reset or replayed; historical marker HEAD only accepts explicit `BlobNotFound`. Marker creation uses exactly one `If-None-Match:*` PUT plus matching GET/ETag readback.
3. **External irrevocable-call journal:** The original `token_adapter.py` stays frozen and SHA-verified. A separate G1 adapter is executed only by this authorized control path. Before each of the original 16 SQL client invocations, it atomically writes a unique status-only `BEFORE` Blob and verifies same ETag/content with GET. Only then can SQL launch. On success/failure it writes a unique `AFTER` Blob and verifies GET before claiming success. Any error is terminal HOLD with no automatic replay. No raw SQL/error, JWT or business data in receipts.
4. **Postcheck:** Existing SQL postcheck (69 tables, three distinct required SchemaVersions, constraints/triggers) and schema fingerprint remain frozen. When they pass, a new externally immutable `postcheck.json` receipt records schema hash / 19 total calls before terminal PASS can be reported. It does **not** certify app/runtime ACL, cleanup or Business UAT.
5. **Read-only crash recovery:** Pure `g1_readonly_recovery_REVIEW_ONLY.py` checks an independently retrieved exact consumed marker and 16 fixed BEFORE/AFTER pairs + postcheck; reports only externally confirmed progress or HOLD. Missing bytes are UNKNOWN, never proof of zero SQL. It has no network or live CLI. Any future actual retrieval requires separate Owner authorization.

## Focused offline evidence and limitations

- **41/41** focused Python security/interface/contract/mock tests PASS in a fresh isolated working directory; no original accepted CI or 224 legacy suite was rerun.
- Bound 21 runtime files byte-for-byte by `binding-index.json`, original 14-layer source hash baseline intact.
- No Azure/SSH/Token/Blob/SQL/DDL/deployment, permission, configuration or new Azure resource change performed in this conversation.
- **Not yet independently audited end-to-end against the real Azure Owner shell, SSH tunnel, App filesystem, exact signed private package, Blob network and SQL.** The new Parent/Child SSH and external Blob journal have only synthetic/offline proof. The final printed terminal result also depends on Cloud Shell; external ledger helps recover status but has not been tested in a real disconnect. Thus no new Live DDL approval should be issued from these offline tests alone.
- Original consent for previous installers/R3/R4 is **consumed**, not transferable. New exact bound Owner authorization and independent full review are mandatory for any live write. Stop on UNKNOWN.
- UAT next only after G1 actual schema PASS: G2 paired API/Frontend/ACL/three role smoke, then reuse 25 canonical Business UAT cases from PR85; IT docs deferred.

Do not give the solo Owner speculative Cloud Shell commands to trial this archive. This is one consolidated NONDEPLOY integration deliverable for an independent code/security review, not a ready-to-run operation.
