# Option C Batch 2B — Owner Single Entry delta

Verified candidate implementation on the Recovery branch:

- Candidate route surface: `/api/v1/optionc-candidate`
- Operations: submit published Location change, own status, Admin queue, Approve, Reject, UNKNOWN reconciliation.
- Submission preserves the published `Locations` row and creates one canonical `ChangeRequests` row, `Submitted` event, and `LocationPublicationStages` row in one Serializable transaction.
- Live HR/profile/employment, effective role intersection, organization/team scope, ownership, dated Visitor membership or Leader appointment, RowVersion and duplicate-pending checks are server-owned.
- Approve and Reject re-read live evidence after request-first locking; Reject closes the stage atomically. Approval uses the existing atomic publication writer and UNKNOWN COMMIT reconciler.
- HTTP candidate routes are authenticated and DI-composed, but the candidate flag is false by default and every operation requires an isolated loopback `OptionCIsolated_*` SQL target.

Verified nondeploy constraints remain:

- `OptionC:Approval:Enabled=false`
- `OptionC:Candidate:Enabled=false`
- `PackageB:B3:Enabled=false`
- frontend approval flag false
- no UAT/Production SQL target, migration, deployment, or 013 modification

Business UAT and future UAT execution remain HOLD pending independently authorized
schema preflight, UAT execution policy, deployment planning, and owner approval.
