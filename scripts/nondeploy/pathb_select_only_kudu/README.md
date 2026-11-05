# Field Visit Path B — SELECT-only Kudu candidate

NONDEPLOY candidate for one separately authorized live SQL preflight. It uses
the existing `api-fieldvisit-pathb-uat` main container and official interactive
`az webapp exec`, stages only four App-side files in a distinct owned `/tmp`
directory, downloads and hash-verifies the five already-pinned Microsoft/Debian
packages into isolated temporary directories, obtains one Owner SQL-audience
token only after App readiness, and performs exactly one unchanged SELECT.

It contains no Storage token, Blob access, marker, Installer, DDL/DML, ACL,
deployment, or retry path. The original `preflight.sql` and existing pure
boundary are byte-for-byte unchanged. A failed or unknown SQL outcome is
`0_OR_1_UNKNOWN` and cannot be retried automatically.

Live execution remains locked until an external authorization JSON exactly
binds the final distribution SHA-256, target server/database and one-call cap.
The approved future invocation must also name the exact App instance, permit
at most two Kudu sessions (run/cleanup and independent cleanup readback), and
set a 600-second total deadline.

No live Azure/Token/SQL operation was performed while producing this package.

The v2 delta parses the real Azure CLI instance shape from `name` (never the
ARM `id`) and emits only fixed `STOP_STAGE`, `TOKEN_REQUEST`, and
`TOKEN_HANDOFF` labels. This preserves secrecy while distinguishing a pre-token
control failure from a possible SQL-start UNKNOWN.
