# E-A2B permanent disposable SQL regression

Run `bash scripts/test-ea2b-location-event-integration.sh` with .NET 8 and Docker.
The protected Fast-Track calls this script immediately after the unchanged E-A2A suite.
No Azure/UAT connection or mail delivery is used. Each run has a fresh SQL Server
database, using the existing E-A2A prerequisite bootstrap and unchanged authoritative
1800_006 scripts. Expected times are compared exactly after SQL `CAST AS datetime2(3)`.

Initial creation proof is `AuditLogs.EntityType=Location`, exact decimal LocationId
in EntityId, `Action=LocationInitialReviewCycleV1`. NewValues permanently describes
`markerVersion=1`, `marker=INITIAL_REVIEW_CYCLE`, exact locationId. The marker is
atomic with creation; it is not an approval-history entry.

Applied Location items preserve the full original source JSON under `source`, with
`envelopeVersion=1` and immutable `appliedMutation`. EF rejects modification,
deletion, status reversal or EntityType reversal of Applied envelope evidence.
CREATE is positive proof; any exact LocationId REREVIEW is permanent v1.8 suppression.
Neither source LocationCode nor timestamps/order/Outbox/history absence is cycle proof.

| Permanent marker | Evidence |
| --- | --- |
| EA2B-01_MANAGED_CREATE_PENDING | Manual/managed create, exact marker/key, no Pending-entry history, authoritative time |
| EA2B-02_TRIP_TEMP_CREATE_PENDING | Trip create, marker/TEMP_CREATE key/time |
| EA2B-03_TRIP_TEMP_UPDATE_PENDING | Trip update temporary producer |
| EA2B-04_IMPORT_CREATE_ENVELOPE_RETRY_IMMUTABILITY | Legacy source-only input, exact source preservation, version/CREATE, canonical key, durable retry, writer retry, Applied immutability |
| EA2B-05_PENDING_TO_PENDING_NO_EVENT | NO_NEW_REVIEW/prior Pending/enteredPending=false, no new event |
| EA2B-06_REREVIEW_DISTINCT_ITEMS_MONOTONIC_SUPPRESSION | Two durable re-review items, prior Approved, distinct keys, transition time, no later Approved notification |
| EA2B-07_IMPORT_ITEM_ROLLBACK_PARTIAL_SUCCESS | First item committed; second creation/envelope/outbox rolled back, failure evidence/counts |
| EA2B-08_IMPORT_REREVIEW_ROLLBACK | Existing Approved Location restored; no applied fact or attempted notification |
| EA2B-09_MANUAL_APPROVAL_INITIAL_PROOF_HISTORY_ID_INITIATOR_RETRY | Manual/managed/Trip initial proof, generated history key, CreatedBy initiator, SQL-precision time, same occurrence retry, already-Approved no event |
| EA2B-10_BACKGROUND_INITIAL_APPROVAL_REPROCESS | Background initial approval/time, not requester, actual already-Approved reprocessing/history with no new notification |
| EA2B-11_LEGACY_AMBIGUOUS_REREVIEW_NEGATIVE_AUTHORITY_DORMANT | Legacy ambiguity, legacy re-review, changed LocationCode/old suppression timestamp/additional positive marker, geocoding-only negative, Returned dormant |
| EA2B-12_CREATE_AND_APPROVAL_ROLLBACK_ATOMICITY | Managed/Trip creations and manual/background approval rollback after first flush/outbox Add |
| EA2B-13_SETTINGS_OPTIONAL_PREFERENCE_UNUSABLE_EMAIL_EVENT_TIME | Disabled review still commits proof, proof independent of Outbox, disabled approval business-only, Transaction ignores optional false, unusable email terminal Failed |
| EA2B-14_IMPORT_CREATED_BACKGROUND_INITIAL_APPROVAL | Import CREATE envelope authorizes background initial cycle |

The protected E-A1/E-A2A suites remain the permanent authority for Add-only writer,
surgical SQL collision translation and negative uniqueness/547/concurrency matrices;
their code and markers are not changed by E-A2B.
