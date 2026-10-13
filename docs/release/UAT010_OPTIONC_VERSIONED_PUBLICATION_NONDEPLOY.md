# Option C — Versioned publication and atomic swap boundary (NONDEPLOY)

**Status:** engineering contract and pure preflight only. Not an approved
migration, API, enabled B3 approval, or Business UAT release.

## Stable identity and staged data

- Keep the existing `Locations.LocationId` stable for live trip and picker
  references. **Do not mutate Approved/Active address or geocoding as a
  background job side effect.**
- Future staged version must be a separate immutable record tied to canonical
  `ChangeRequests.RequestPublicId`, the source LocationId, organization,
  team, proposed address/identity, and expected source/request RowVersion.
  A stage cannot become authoritative merely because geocoding succeeded.
- On the future single serializable transaction, lock request **first**,
  published location **second**, staged version **third**, then validate live
  requester HR/role/team and independent Admin; reject stale versions,
  replayed DecisionKey, duplicate-review drift and geocode/address mismatch.
- Persist new published version and audit events with the guarded Location
  update in **one transaction**. On any failure, leave old Approved/Active
  record intact; never deactivate it as an intermediate state.
- Preserve `VisitTripStop`, `VisitTripSnapshotStop`, and all other
  historical snapshots/attribution. Verify the actual historical FK/snapshot
  coverage before designing final DDL; a stable LocationId alone does not
  prove every historical read remains immutable.
- A geocode result is bound to the exact staged address/version. The result
  of an earlier address, a failed lookup, or duplicate candidates without
  independent review cannot authorize publication.
- A metadata-only change can avoid a new geocode only if published identity
  and address fields are identical; independent approval and transaction
  safeguards still apply.

## Implementation boundary

- Pure C# candidate:
  `V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap` plus
  fail-closed tests for self-review, scope, RowVersion, geocode, duplicate,
  history, decision replay, and revocation.
- This gate accepts **server-only locked evidence**; booleans are NOT trusted
  from HTTP. It is intentionally NOT connected to the current SQL writer or
  exposed as an API. Passing the gate alone does not authorize a write.
- The existing `V180OptionCPublicationSafety` direct-overwrite DENY rule
  remains mandatory. `ApproveAsync` remains DENY ALL; B3 OFF.
- Canonical 011/012 are disposable-SQL **drafts**, not UAT migrations.
  Reserved 013 must not be reused. No new version number is assigned here.

## Unresolved before implementing swap

1. Verify all Location historical consumers, FK relations and snapshot
   immutability from the actual schema and read paths.
2. Approve the stage table and immutable version identity, concurrency
   uniqueness, geocode evidence and duplicate review signatures.
3. Define request/stage retry and unknown-commit reconciliation by immutable
   correlation key; never blindly retry a commit.
4. Implement real locked SQL atomic writer and failure injection, then
   independent real SQL Server concurrency and end-to-end tests.
5. Separate exact-SHA Owner approvals for UAT migration, backend/frontend
   deployment and Business UAT; no Production action.
