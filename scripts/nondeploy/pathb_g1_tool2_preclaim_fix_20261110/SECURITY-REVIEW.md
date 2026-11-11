# Focused security review

## Review result

The correction is confined to the outer handoff and a network-free Parent
adapter. No file inside the original Candidate was modified.

| Control | Result |
|---|---|
| Five packages complete before claim | PASS — `acquire_all()` precedes `consume_claim()` |
| Exact original package URLs/hashes | PASS — loaded from bound Candidate config |
| TOOL_2 redirect | PASS — one exact Microsoft geofence endpoint only |
| Arbitrary redirect / second redirect | REJECTED |
| HTTPS/TLS | PASS — HTTPS-only policy and default certificate verification |
| Size | PASS — declared length (when present), actual length and 75 MB cap |
| SHA-256 | PASS — exact original expected hash |
| Unsafe path/symlink/cache | REJECTED — exclusive no-follow 0600 files |
| Claim-preverified bytes equal Parent bytes | PASS — one no-follow file descriptor, verified source bytes, exclusive copy, destination reverify |
| Postclaim network download | ABSENT — Parent `download_once` is replaced by cache-only copy |
| Claim on acquisition failure | ABSENT |
| Parent launch on acquisition failure | ABSENT |
| Automatic retry | ABSENT |
| Old Operation ID reuse | REJECTED by original exclusive claim |
| Parent failure/timeout/UNKNOWN | One invocation only; HOLD, no retry |

## Provenance boundary

The official Microsoft package index lists the exact TOOL_2 filename and
Microsoft documents that SQL packages redirect from `packages.microsoft.com`
to `pmc-geofence.trafficmanager.net`. The correction pins that one documented
transition and still requires the original SHA-256. It does not infer safety
from a redirect alone.

## Remaining live gates

This NONDEPLOY review does not prove current Cloud Shell egress, current package
availability, Azure identity, App instance, Blob marker state, SQL identity or
database state. Those original gates remain mandatory in a separately approved
one-shot execution.

