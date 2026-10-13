# Option C — Persistent publication stage, isolated draft only

This batch adds a **real disposable SQL Server stage table** and an
unregistered, parameterized C# stage writer. No runtime route or DI registration
uses it. The draft has a hard database-name guard and does not touch
`SchemaVersions`; it is **not** migration 013 or any approved UAT migration.

## Storage boundary

`LocationPublicationStages` is keyed by canonical `ChangeRequests.ChangeRequestId`.
It holds source LocationId, organization, team, source RowVersion, proposed
JSON and SHA-256, stage/geocode/duplicate-review states, geocode-address
signature, coordinates, duplicate-evidence signature and stage RowVersion.
It never changes the published `Locations` row on creation. FK and unique
indexes reject orphan and competing open stages; CHECK constraints prevent
ReadyForPublish without duplicate review and geocoding evidence.

`V180OptionCStageDraftWriter.CreateFromLockedRequestAsync` requires an
already-open SERIALIZABLE SQL transaction, locks request then source Location,
rechecks source RowVersion and ownership, and inserts a private PendingGeocode
stage using only server-side request data. It is internal and unregistered.
The future caller MUST independently revalidate live HR/role/team authority
before invoking it; this draft writer is **not** an authorization boundary.

## Next work before enabling anything

- Replace isolated draft with separately reviewed, version-assigned migration,
  including catalog preflight, Verify, rollback and UAT drift assessment;
  reserved 013 remains untouched.
- Build geocode and duplicate-evidence writer with signatures tied to exact
  staged JSON/address, immutable audit and rowversion compare-and-swap.
- Build and test the request-first/location-second/stage-third atomic publish
  transaction with failure injection, historical snapshot assertions and
  unknown-commit reconciliation. Keep `ApproveAsync` DENY ALL until authorized.
- Add end-to-end Visitor, Leader Excel, Admin review and rollback UAT tests.

Owner migration/deployment authorization has **not** been granted.
