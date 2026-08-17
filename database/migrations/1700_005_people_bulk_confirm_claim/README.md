# 1700_005_people_bulk_confirm_claim

## Purpose

Prevent duplicate People Bulk authorization writes when two Confirm
requests arrive concurrently.

The API uses this state transition:

`Previewed -> Confirming -> Confirmed / PartiallyFailed`

The `Previewed -> Confirming` transition is performed with one atomic
SQL `UPDATE ... WHERE Status = 'Previewed'`. Only one concurrent request
can successfully claim the batch.

## Database change

This migration only widens `CK_ImportBatches_Status` to allow:

- Previewed
- Confirming
- Confirmed
- PartiallyFailed

No business data is changed.

## Prerequisite

Schema version `1.7.0-004` must already be applied.

Migrations `1700_001` through `1700_004` are frozen and must not be
modified or re-run.

## Execution

1. Review `Up.sql`.
2. Execute `Up.sql` exactly once.
3. Execute `Verify.sql`.
4. Require `VerifyStatus = PASS`.
5. Do not re-run `Up.sql`.

After successful application, this migration becomes frozen.

## Failure semantics

`Confirming` is intentionally fail-closed.

If the API process fails after claiming a batch, the system must not
automatically return that batch to `Previewed`, because some item writes
may already have been applied. A stuck `Confirming` batch requires IT
review rather than automatic replay.
