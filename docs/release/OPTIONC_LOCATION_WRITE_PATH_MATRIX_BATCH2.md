# Option C Location write-path matrix — Batch 2

Scope: `Locations` identity/address/geocode fields and mutations of references to
those rows. This is a source inspection record, not a claim that the Option C
workflow is deployed. `ChangeRequests` remains a candidate schema and all
runtime flags remain off.

| Path | Fields or effect | Guard / finding | Batch 2 disposition |
|---|---|---|---|
| `V170LocationRepository.UpdateMaintenanceAsync` | City, district, address, Plus Code, tax ID, master note, geocode state | Previously wrote every field directly after RowVersion check. | Published location identity/address/tax changes now fail explicitly with `LOCATION_HIGH_RISK_CHANGE_REQUIRES_OPTIONC_APPROVAL`; low-risk master note edits remain direct and retain RowVersion protection. A functional request-submission route is still HOLD because candidate schema/policy is not deployable. |
| `V170LocationRepository.MergeAsync` (`MergeFields`) | Survivor name/type/team/address/tax and source merge state | Admin merge preview and RowVersion checks existed; published survivor fields could still be overwritten. | A merge touching an approved published source or survivor is denied pending canonical approval. Reference-only merge mode retains its existing behavior. |
| `V160FinalRepository.CreateManagedLocationAsync` | New identity/address fields | Creates inactive, `Pending` row; geocoding is pending. | Draft create, not a published Location overwrite. Retained. |
| `V160FinalRepository.UpdateManagedLocationAsync` | Name/team/type/address/tax and active state | `V180OptionCLocationWriteRules.EnsureDirectDraftUpdate` limits this path to drafts; RowVersion enforced. | No published write path found. Retained. |
| `V160FinalRepository.DeleteManagedLocationAsync` | Deletes a Location row | `EnsureUnpublishedDraft` plus reference impact checks. | Published/historically referenced rows cannot be directly deleted. |
| `WorkbookImportService.ConfirmAsync` — Location import | Creates new pending rows; existing updates include identity/address | Existing row is rejected when `ApprovalStatus == Approved` or active; pending update resets geocode/approval and deactivates. | Published direct update guard already present. |
| `WorkbookImportService.ConfirmAsync` — ProjectLocations import | Creates/updates project-to-location links | Changes project references, not Location identity/address. | Not a Location publication write. |
| `BackgroundJobService` geocoding | Latitude/longitude, geocode state/time | Previously rewrote coordinates on the same row while retaining Approved/Active state. | Confirmed bypass fixed: approved locations are skipped with an explicit Option C required result; only unpublished drafts proceed. |
| `V180MasterDataAdminRepository` | Deployment-site/location assignments, relocation history | Reads Location rows and writes assignment/history entities; no Location master-field assignment found. | No Location identity publication bypass found. |
| `V180MasterDataBulkWorkbookService` | Deployment-site LocationCode resolution/assignment | Resolves Location and changes site/location assignment records, not Location master fields. | No Location identity publication bypass found. |
| `V170ProjectLocationAdminRepository` | Project-to-location links | Writes `ProjectLocations`; Location rows are read for scope/eligibility. | Not a Location publication write. |
| `V180B3ChangeRequestService.SubmitAsync` / Option C writer | Canonical proposal and reviewed publication | Candidate service verifies schema but `PackageB:B3:Enabled=false`; publication path is isolated-test only. | No deployed endpoint/DI activation. This prevents unsafe substitution of test-only evidence for live authorization. |

## Remaining boundary

The high-risk maintenance/merge paths now fail closed rather than persisting an
unapproved Location mutation. They cannot yet create and return a canonical
ChangeRequest through the normal application API: the production schema and
UAT execution policy are not authorized, while the available Option C writer
requires an `OptionCIsolated_*` database and the isolated actor fixture. The
maintenance response therefore reports that approval is required and does not
claim the edit was saved. A future batch must provide the reviewed UAT schema,
live evidence adapter and transaction-owned submission service before enabling
that route.
