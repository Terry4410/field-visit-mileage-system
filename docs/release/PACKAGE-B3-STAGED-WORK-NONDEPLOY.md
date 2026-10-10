# Package B3 isolated WORK staging — implementation boundaries

- Issue #24 / source protected, B1C manager roster not attested.
- B3 backend flag `PackageB:B3:Enabled=false` by default.
- Frontend route requires `VITE_PACKAGE_B3_ENABLED=true` AND an Admin role.
- B3 uses new ChangeRequests and ChangeRequestEvents schema from **unexecuted** DDL draft.
- Every B3 DB operation checks actual table/schema status including 1.8.0-011; no schema has been created.
- Implemented scaffold: visitor-owned active Customer change submission (no Leader team grant), personal listing, Admin pending listing, Admin reject with rowversion and non-self-review.
- **Approval executor intentionally DENIES ALL**. Geocoding success never implies approval. No fake Applied status.
- Unimplemented: full atomic approve/apply, prospective geocoding/versioned address changes, audited manager grants, notification runtime, end-to-end UAT, broader Team/HR/Role/Project surfaces.
- **DO NOT turn on either feature flag** before separate approved schema migration, security review and full runtime verification.
- No Production, UAT, SQL or Migration permission conferred.
