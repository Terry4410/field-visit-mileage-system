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

## Kudu channel recovery delta (NONDEPLOY)

`p3_kudu_transport.py` now sends `stty -echo` separately from the readiness
probe and accepts readiness only as a complete independent LF/CRLF line after
the current phase boundary. Command echoes, partial strings, larger lines and
stale buffered markers are rejected.

`p3_kudu_channel_diagnostic.py` is a distinct authorization-locked entrypoint.
It can stage one deterministic non-sensitive file only under
`/tmp/fv-pathb-kudu-channel-diagnostic-v1-20261105`, then use a second session
to verify or remove only that exact owned directory. Its only public receipt is
`PROCESS_START`, `ECHO_READY`, `STAGE_READY`, and `OWNED_CLEANUP`, each with a
fixed `PASS`, `HOLD`, or `UNKNOWN` value. Without `--live` and a short-lived
external authorization record it exits HOLD without contacting Azure.

This recovery delta does not invoke the SELECT-only parent and has no
credential acquisition, database connection, frozen installer, migration,
storage-object, or deployment path. A new exact-artifact Owner authorization
is required before any live diagnostic.
