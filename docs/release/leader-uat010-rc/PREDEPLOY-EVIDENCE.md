# Predeployment evidence — plan only / no live inspection executed

Current Protected `7f0cf0cb55467916fb0f57239301a8787fd5cb69` is source control evidence only. It is NOT evidence of deployed API or Pages binaries. Candidate CI artifact stamps identify only the candidate output.

| Gate | Required immutable evidence | Current state |
|---|---|---|
| Actual API deployment | Existing target resource ID and active deployment/run ID, original source SHA, deployed package SHA-256 and source stamp; no settings/secret values | UNVERIFIED / HOLD |
| Actual Pages deployment | Existing site URL/environment/deployment ID, actual workflow/artifact ID, source SHA, archive SHA-256; deployed config identity/hash | UNVERIFIED / HOLD |
| Prior rollback eligibility | Captured prior API + Pages artifact IDs, hashes, availability/expiry, source provenance, paired configuration fingerprint and PDF font inventory | HOLD; do not assume Protected rebuild equals prior binary |
| Real Protected reference | Approved immutable schema-only 1.5 baseline + provenance; unmodified prerequisite and 001–010 Up/Verify chain; normalized metadata and hashes | HOLD: baseline absent from repository/history |
| UAT inventory | Independently authorized normalized metadata inventory matching exact reviewed SQL/validator hashes; marker alone insufficient | HOLD; WORK C never dispatches/retries it |
| Application identity | Existing API SQL principal SELECT/INSERT/UPDATE/EXECUTE requirements checked by separately approved procedure; reference sa success does not establish it | HOLD |
| Actual compatibility | Exact normalized reference vs UAT catalog comparator, then scoped app-principal permissions / runtime review | HOLD |
| Backup/export | Approved backup/export evidence and cleanup plan without putting business/personnel content into RC/CI artifacts | HOLD |
| Launch | Control Tower accepts partial scope/tradeoffs; one consolidated Owner API+Pages execution approval with exact SHA and verified rollback IDs | NOT AUTHORIZED |

Prepare a separate bounded read-only collection plan for the actual Azure deployment and GitHub Pages deployment records before using service-specific tools. Do not request tokens in chat. If artifact bytes cannot be independently obtained and hash-verified, keep provenance and rollback HOLD. No query of Azure, UAT SQL or live app settings is performed by this PR.

The unapplied deployment-workflows.patch rejects rollback until verified immutable prior artifacts are pinned and all release gates are separately reviewed. Do not change its guards to use current Protected HEAD as a proxy. The current workflow continues to build/test/upload candidate artifacts only.
