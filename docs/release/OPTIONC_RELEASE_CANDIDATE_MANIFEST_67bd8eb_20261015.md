# Option C Release Candidate Manifest — `67bd8eb34da6b4735964bd8ab6b64b848ab8dba6`

Frozen for the 2026-10-16 UAT entry-readiness meeting. This is a Recovery branch engineering manifest, not a deployment approval.

| Field | Value |
|---|---|
| Repository | `Terry4410/field-visit-mileage-system` |
| Branch | `work/v180-optionc-final-gates-recovery-r1` |
| Exact commit | `67bd8eb34da6b4735964bd8ab6b64b848ab8dba6` |
| Exact-SHA workflow | `38663584025` — SUCCESS |
| Backend | 758/758 PASS; Release build PASS |
| Frontend | 25 test files PASS; build PASS |
| Option C isolated SQL | 1/1 PASS |
| Canonical rehearsal | 011/012/014: 2/2 PASS |
| Release gate | NO-GO PASS |

## Included engineering deltas

- Controlled isolated candidate route retry for Leader mileage; requires `OptionC:Candidate:Enabled=true`, loopback SQL and `OptionCIsolated_*`.
- Real isolated HTTP/DI/SQL provider-stub route success and Leader approval assertions, including RouteCalculationAttempt, governance, ApprovedDistanceKm, ApprovedAmount and snapshot history.
- Return/Correction/Resubmit historical submission assertions.
- Existing canonical reservation, live authorization, atomic publication, audit, LocationId and historical Trip/Snapshot protections retained.

## Explicit exclusions

- The route-retry endpoint is not deployable to UAT/Production.
- No live Google Maps call was made; provider evidence uses a controlled stub.
- No complete timeout/error/revocation/concurrency/report/export/browser UAT matrix is claimed.
- No UAT target preflight, SQL write, migration, deployment, restore, promotion, merge or flag activation occurred.
- Migration 013 was not modified.

## Artifact and rollback gate

Do not promote this SHA until exact target identity, read-only schema preflight, backup/restore proof, DBA checksum review, deployment window, rollback owner and separate Owner execution approval are all recorded. If a future execution has an uncertain commit, classify from a fresh non-pooled read-only connection and do not retry blindly.

## Scope decision

Existing v1.8.0 may be discussed as a limited readiness candidate. Restricted Trip/Mileage and full Scheme A/Option C remain NO-GO for UAT execution. All candidate flags remain OFF by default.
