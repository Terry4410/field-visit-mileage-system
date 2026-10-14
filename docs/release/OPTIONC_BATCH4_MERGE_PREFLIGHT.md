# Batch 4 disposable merge preflight

1. Require DB name `OptionCIsolated_*`, loopback SQL Server, and explicit `OPTIONC_EXECUTION_MODE=IsolatedSqlTest`.
2. Require canonical 011/012/014 rehearsal catalog.
3. Apply `merge/LocationMergeDetails.disposable.sql` only in disposable rehearsal.
4. Verify source/survivor same organization, distinct IDs, active approved rows, and both RowVersions.
5. Verify no VisitTripStop or VisitTripSnapshotStop LocationId update occurs.
6. Roll back on any mismatch; never run against UAT/Production.
