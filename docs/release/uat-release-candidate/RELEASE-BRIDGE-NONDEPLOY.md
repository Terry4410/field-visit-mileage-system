# PR #48 release bridge — offline-only verification of frozen candidate

This release-bridge source is a **tooling-only delta**. Its commit SHA may differ from the accepted UAT candidate SHA. **Never update the candidate SHA pins when the bridge changes** without an independently accepted new candidate artifact and manifest. No azure/SCM/WSS/Shell/SQL/connection/deploy/activation/restore operations are implemented.

## Immutable inputs

- Accepted binary candidate source: `9ee5dacceed2bf55c39310d663f3258bcb545989` (the *candidate*, not the bridge HEAD).
- Accepted focused Run: `39998237406`; Artifact `12520693555`; original provider Artifact ZIP SHA-256 `88a6bb4a9d88da064da577f9439500d929ec2810581d5982c08665fd1141c324`.
- API ZIP: `9a04e848ae0e6cdbd63a574f4383e074811d71f4fee2c7ea718f5d4df585e848`.
- Frontend ZIP: `f756d00f845752d585a520cd11386a1cc9693c48bee095654728496e0ee00ec7`.
- API Assembly composite: `9de20f343388da09d2cd2e4b6cc9a57d1aa93169bb8e9bdc70fd94e47d9ca707`.
- Candidate schema-contract.json SHA-256: `742f2a95f7f58889d1609aceefa656030cf1f1365f10cc9c89968e89cfff0c63`.

## Use (offline only)

Run `python3 scripts/ci/uat_release/release_bridge.py --artifact <original-Actions-artifact.zip>` on an isolated pre-handover machine **only after** the original ZIP has been securely made available. No network access is performed by the verifier. In this chat, no Owner device/software work is requested. Verifier reads but **never extracts** any files; checks outer artifact SHA, each inner checksum, exact API/Frontend package hashes and their embedded file hashes, schema binding, 14 SQL layer hashes, 25 `PARTIAL` acceptance cases, and feature OFF.

A zero exit code is strictly `offlinePackage=PASS` and `status=HOLD_LIVE_G1`. This is **not** Business UAT GO, Azure-runtime attestation, currently deployed hash proof, authority to use `install.py --execute`, SQL schema migration, rollout, feature activation, or recovery. Any input omission, drift, mismatch, changed case disposition, malformed file or nested archive returns sanitized HOLD and nonzero exit. No CI dispatch, OIDC or Azure token required.

## Release/rollback gates NOT fulfilled by this helper

1. Actual running Azure API+Frontend hash and deployment ID; matched verified **prechange** API+Frontend rollback archive.
2. Current UAT SQL schema and principal effective permissions, with safe target-specific acquisition channel and controlled private evidence.
3. Current independently recoverable DB restore point and matched API+Frontend+DB recovery execution boundary; never treat the fixed UAT **empty-database** installer as an upgrade.
4. Demo test-account/HR-Team-Org isolation and effective feature OFF evidence; separate Owner activation authority if and only if all mandatory G1 are independently PASS.
5. Actual phone/iPhone and full 25 Business UAT cases require an approved deployed version and Business signoff. No automated isolated fixture can sign these cases.

`post-uat/v1.8.0`, old D0 v2/v3 and ARM6 exhausted workflows, API/Frontend application code, SQL, existing deployment workflows and protected settings must remain untouched by this bridge. This is not a new deployment workflow and must never trigger an Azure action.
