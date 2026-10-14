# Option C Batch 4 — MergeFields impact analysis

## Result

The current canonical 011/012/014 schema cannot safely represent a MergeFields request.

A merge has two independently versioned Location identities (source and survivor), while the existing ChangeRequest and PublicationStage rows carry one EntityId/LocationId and one expected RowVersion. Encoding two identities into EntityId or ProposedJson without a relational detail record would weaken locking, replay protection, and stale-request detection.

## Safe candidate

The disposable-only additive detail table is `docs/release/optionc-canonical-sql-draft/merge/LocationMergeDetails.disposable.sql`. It is linked to the canonical ChangeRequests row and is not a second approval ledger. It records source/survivor organization, team, RowVersion, and immutable before/proposed evidence. Historical VisitTripStops and VisitTripSnapshotStops are intentionally not rewritten.

## Remaining implementation HOLD

A merge-specific atomic writer and independent Admin route still require review of the survivor/source state machine, source deactivation, current operational reference rebinding, duplicate evidence, and unknown-commit reconciliation. Until that writer is implemented and disposable SQL E2E proves it, published MergeFields remains fail-closed.

Existing reference-only merge behavior is preserved.
