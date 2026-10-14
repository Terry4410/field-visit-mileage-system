# Option C Batch 5 — MergeFields readiness

This Recovery-only candidate keeps ChangeRequests and ChangeRequestEvents as the sole approval ledger. LocationMergeDetails.disposable.sql is an additive detail table for the source/survivor pair, both RowVersions, ownership and immutable evidence; it is guarded to OptionCIsolated_* databases and does not alter reserved migration 013.

The candidate HTTP surface adds POST /api/v1/optionc-candidate/locations/merge-fields. Existing queue, approve, reject and fresh-connection reconcile routes are reused. The deployed candidate flag remains false.

Submission locks source and survivor in deterministic LocationId order inside a Serializable transaction, validates live HR/role/team ownership, records canonical Submitted audit and creates the existing Stage. Approval delegates through the existing live authorization transaction and the merge writer. The writer validates both versions and ReadyForPublish evidence, updates survivor fields, rebinds only current operational references, deactivates source, creates a new survivor publication version, marks Stage and Request Applied, and writes Approved/Applied audit events. Trip and snapshot references are intentionally not updated.

Reject uses the existing canonical rejection path and abandons the Stage. Any missing merge detail schema, stale version, duplicate pending request, conflicting operational reference, missing geocode/duplicate evidence or authorization revocation fails closed.

Real MergeFields HTTP/SQL execution remains a HOLD until the Recovery workflow's disposable database explicitly applies the additive detail script and seeds a two-location fixture; no UAT or Production execution is authorized.


## Batch 5C verification — 2026-10-14

- Merge submission was executed through the candidate HTTP route against disposable OptionCIsolated SQL Server 2022.
- A real merge publication transaction was committed through the existing TDS acknowledgement-loss proxy; the client acknowledgement was dropped after COMMIT and no retry was issued.
- Fresh non-pooled reconciliation verified Request=Applied, reviewer=Admin 2, Approved/Applied events, source deactivation, survivor Version 2 current with Version 1 retained, current references rebound, and historical Trip/Snapshot references unchanged.
- Merge stale source and stale survivor RowVersion tests fail closed with no partial publication.
- Engineering: PASS. UAT target schema preflight: NOT RUN. Business UAT: NOT RUN.
- Remaining HOLD: merge-specific incomplete-evidence negative reconciliation and application-owned geocode/duplicate evidence transition remain outside this batch; background publication remains disabled.
