# G1 original installer — P0 process group integrated (NONDEPLOY)

Base: FieldVisit_PathB_G1_Integrated_SSH_Cleanup_Merged_NONDEPLOY_20261110.zip
Base SHA-256: bdc1f6e620ed719987df6fab271bf01acac1a9274c689deed59ea46d4b713411

This is a coordinated update of existing G1 execution controls (NOT an R5/R6 or MI diagnostic).
Changed runtime source: p3_app_installer_runner.py and token_adapter_g1_REVIEW_ONLY.py only.
The installer now owns a process group, verifies it is gone, and blocks cleanup if process-group status remains uncertain. The SQL client inherits the group rather than starting a separate session. SQL execution outcomes remain UNKNOWN on interruption, not retriable.
The original 14 SQL layers, frozen installer, original token_adapter.py, preflight.sql, postcheck.sql, schema-fingerprint.sql, and historic accepted CI remain unchanged.

New focused tests: 4/4 PASS + two affected existing order/binding assertions PASS (6/6 total). Other accepted 41 tests NOT rerun.
Test contract test_g1_integrated_delivery.py is updated only for the changed supervised installer invocation name.

NO LIVE GO. No authenticated Owner Azure, true SSH tunnel, Blob service, SQL, App runtime, or host crash was exercised.
Linux process group membership does not guarantee killing a descendant that deliberately detaches/re-sessions. A killed SQL client does not guarantee rollback of a committed SQL statement. Cloud Shell disconnect/host death recovery is not established by local tests.
No consumed v2/v3 authorization, Blob marker or original source is reset. No DDL authorization is granted.
G1 HOLD; G2 unverified; Business UAT 0/25. IT documents deferred.
