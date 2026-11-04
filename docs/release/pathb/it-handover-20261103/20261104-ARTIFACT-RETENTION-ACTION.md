# Path B accepted artifact retention — verified 2026-11-04

**Action required before 2026-11-26 UTC.** The accepted release binaries will expire in GitHub Actions even though the source commit remains in GitHub. This is an IT handover preservation task, not SQL/DDL authorization.

Live GitHub Actions artifact metadata was independently fetched from runs `40438452922` and `40438452961` on 2026-11-04. All seven returned artifacts were `expired=false` at time of inspection; expiration times below come from the API and must not be treated as proof of future availability.

| Artifact ID | Type | GitHub SHA-256 artifact digest | Expiry (UTC) |
|---|---|---|---|
| `12705590592` | accepted Path B build | `5cc23ff55fbb7ef86b7c00aa3a695326f012c8f96a2b1793f82c2e737ec6894a` | 2026-11-26 02:28:00 |
| `12705345764` | integrated API candidate | `323da7400698f8d03ee456c07273b9de7fe63a127a41dd8a6e14e55edbbbc084` | 2026-11-26 02:28:35 |
| `12705635708` | Frontend candidate (NONDEPLOY) | `743182631cddc98037e3a967e1c7b1fc4c447505721f77864a2bd234bb0d63b7` | 2026-11-26 02:29:26 |
| `12705610691` | integrated source reference | `64a89c368e6b5cb2feaa333187683bc6c7d7947b6bd67aa9f22f570a8157fbba` | 2026-11-26 02:29:00 |
| `12705785455` | API runtime ACL candidate (NONDEPLOY) | `890c269852ba4239f60e70d30a7f3e64aca4561902924c62b6159b7de796ec19` | 2026-11-26 02:29:53 |
| `12705890376` | corrected 020 promotion (NONDEPLOY) | `832ea7f2bd8e18fcb4cd509b133a35f8439ba8d7cb624708e67c48213784072f` | 2026-11-26 02:30:10 |
| `12705107107` | corrected 020 synthetic test evidence | `b6db5aeb8c8c97e899e1295d1d6c2351228788243ac2b469f2f0895d23eba0dc` | 2026-11-26 02:30:12 |

All artifacts are associated with approved source `d6c220877b9976b6478d85b5171d4e06293595ae`, as returned by GitHub.

## Custody action (does not grant new deployment authorization)

1. Prioritize the build, API and Frontend candidate; preserve each **actual artifact ZIP** in an Owner-controlled location with access limited to eventual IT transfer, without publishing binaries or credentials in a public repository.
2. Verify each downloaded ZIP SHA-256 against the exact digest supplied by GitHub, and record the file size, date and custody location (without revealing private URLs or keys). Do **not** call a filename or a metadata record a backup.
3. Retain all seven if feasible to preserve ACL, schema migration and test evidence; IT should be able to review but must still verify the release build, schema contract and intended UAT pair.
4. If the artifact cannot be safely retrieved or checksum verification fails, record `ARTIFACT_CUSTODY=HOLD` and avoid implying IT has the binary. GitHub Actions retention extension/rebuild is not authorized by this document.
5. These artifacts are not a substitute for the sealed SQL package (original ZIP SHA-256 `5a0c956d4ef5c88e0ff49d2d606ee9c1cb34e81e7f0c0f85b5aea8c5743e5828`) or final Business UAT / deployment evidence.

Source inventory already exists in the original handover manifest. This addendum is factual, supplementary, and **not included in original `checksums.sha256`**. Its Git blob identity and commit should be recorded in the next supplementary index refresh. No artifact ZIP was downloaded by this documentation action; **binary preservation remains NOT VERIFIED**.

Canonical hub: [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41). P3 SQL installation remains HOLD / Business UAT NO-GO. No Azure/SQL/SSH/DDL/deployment authorization implied.
