# VAL-B1-002 — Owner-led Leader Location Authorization Attestation

Status: **BLOCKED_UNSIGNED**. This is a controlled evidence form, not an approval and not a database change.

## Purpose

Permit a Leader to create or maintain only `Customer` Location drafts for an actual managed team. A positive decision requires every item below; a Leader role, JWT claim, UI scope, historic membership, or the 1800_002 backfilled `TeamLeaderAssignments` row is insufficient.

## Evidence record — one row per managed team and effective period

| Required field | Owner / HR / IT entry |
| --- | --- |
| Evidence ID (immutable) | `VAL-B1-002-...` |
| Leader employment ID and employee number | |
| Organization ID / code | |
| Managed Team ID / code | |
| Authority type | `FormalAppointment` or `ExplicitDelegation` |
| Appointment source record ID | |
| Delegation source appointment ID (if delegated) | |
| Effective from / to | |
| HR current-status source and as-of timestamp | |
| Active Leader-role source and as-of timestamp | |
| Current team-membership source and as-of timestamp | |
| Independent attestor (not the requester) | |
| Attested UTC timestamp and evidence SHA-256 / controlled link | |
| Revocation owner and revocation process | |

## Acceptance checks

1. HR confirms the employment is currently active and not on leave/terminated.
2. IT reconciles the row using [VAL-B1-002-leader-location-read-only-reconciliation.sql](../../database/candidates/VAL-B1-002-leader-location-read-only-reconciliation.sql); it must be read-only and its output is retained with the Evidence ID.
3. A current effective Leader role, team membership, and direct appointment—or a delegation whose source appointment is also current—match the same organization and team.
4. The independent attestor is not the Leader/requester and the evidence is immutable after attestation.
5. Revocation, expiry, team transfer, organization transfer, leave, or HR-status change immediately removes the grant at the next server-side read.
6. High-risk Location changes (activation, type/team/owner changes, or published master changes) remain Admin-review-only. With B3 OFF, they remain unavailable; no direct write is permitted.

## Current system limitation / HOLD

The protected/deployed schema has no independent-attestation provenance fields. Candidate `1800_013` adds an explicit `AdminMaintained` source, evidence reference, approval/revocation fields and preserves all existing records as `HistoricalUnverified`. Until that candidate is separately approved, deployed, and reconciled, Leader location mutation remains **DENY ALL**. This template does not itself create a real HR appointment.
