# Field Visit Path B G1 TOOL_2 Preclaim Fix — NONDEPLOY

This archive is engineering evidence only. It does not authorize Azure access,
Token acquisition, Blob mutation, SQL, DDL, installation or deployment.

## Preserved immutable baseline

- Provenance commit: `f85dc629bef21dce9ee0aa7c57b3f22ca3a3bd59`
- Prior Handoff ZIP: `ee9182c96f03ea218a85b8a8e16b2330a0dac6f93c8ecce4800b0be6fa093039`
- Original Candidate ZIP: `8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141`
- Original Candidate binding: `302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45`

The embedded Candidate ZIP is byte-for-byte unchanged. Therefore its frozen
Installer, 14 SQL layers, preflight/postchecks, SQL adapter, Blob guard, SSH,
Claim, journal, 19-call limit and fail-closed rules are unchanged.

## Minimal correction

`g1_tool2_preclaim_handoff.py` now:

1. stages and revalidates the original Candidate;
2. runs the original management/authorization preclaim gates;
3. acquires and verifies all five exact Debian packages into an owned 0700
   temporary cache, with 0600 regular files, before claim consumption;
4. consumes a new operation claim only after all five packages pass;
5. invokes the exact original Parent through `g1_preverified_parent.py`;
6. replaces only the Parent module's `download_once` reference with a
   network-free copy from the preverified cache.

TOOL_2 allows one redirect only when it is exactly HTTPS, host
`pmc-geofence.trafficmanager.net`, the unchanged Debian 12 package path and the
single query `geofence=true`. A second redirect or any other host/path/query is
rejected. This endpoint behavior is documented by Microsoft for SQL packages:
https://github.com/MicrosoftDocs/linux/blob/main/docs/packages.md

TLS certificate validation uses Python's default verified HTTPS context. No
TLS downgrade, arbitrary redirect, hash substitution or cache fallback exists.

## Safe output

Preclaim failures emit only one fixed class:
`TOOL_1_*` through `TOOL_5_*`. URLs, response bodies and raw exceptions are not
printed. A preclaim tool failure emits `G1_LOCAL_CLAIM=NOT_CONSUMED` and
`G1_PARENT=NOT_LAUNCHED`.

## Execution boundary

Only `--check` is authorized by this delivery. Any future `--live` execution
requires a new exact-SHA Owner authorization, a fresh Operation ID and all
original live gates. Historical consumed operation IDs and claims remain
consumed and must never be reset or replayed.

