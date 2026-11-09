# Path B Owner Catalog — R3 integration contract (REVIEW ONLY, 2026-11-09)

## Outcome
The actual R3 Protocol-Fix archive was cold-extracted and 37 prior tests were preserved. An additional offline-only, dependency-injected Parent/Child execution-order contract and 10 tests were authored; **47/47 tests PASS** locally. No Azure/SQL/token/Blob/DDL/installer/deployment execution.

## Immutable external archive
File: `FieldVisit_PathB_Owner_Catalog_R3_IntegrationContract_REVIEW_ONLY_20261109.zip`
SHA-256: `5c2751a1d4c9a21a4e3b5d766321c380e7a3427395058bb58c7fe77a387d2389`
Includes R3 SQL, Parent, Child and previous tests; new code files:
- `owner_catalog_integration_REVIEW_ONLY.py`, SHA-256 `4280e55b01c1a7561e02a98937f92ee8f5afe24aae8900e2549aef6a10264a24`
- `test_owner_catalog_integration.py`, SHA-256 `a1943ac2c448d396210c21c596bafd3870903659957ebb1c7d1182ceb9185406`

**Note:** the ZIP itself is not in GitHub and these new source files are not yet committed. This metadata is an audit reference, not a reconstructed source deliverable.

## Integration order validated only with injected mocks
1. Verify pinned artifact digest and Owner/tenant/subscription/exact target.
2. Attempt a NEW atomic conditional Blob create. UNKNOWN or non-201 = terminal HOLD, no retry.
3. After durable claim, stage to existing App Service tunnel.
4. Require authenticated SSH host and no-echo TTY readiness.
5. Request Owner SQL token once from trusted signed-in Cloud Shell, send through protected TTY once.
6. Accept only a fixed sanitized terminal receipt; cleanup verified-owned temporary files.
7. Never clear consumed markers. Any uncertain token transmission is consumed.

## Critical remaining engineering
The actual Azure adapter (real SSH tunnel lifecycle, securely generated token, real Blob PUT, protected Child stage, authenticated receipt and safe owned cleanup) is **NOT IMPLEMENTED** in this offline contract. Existing Parent and Child mains and integration-contract main remain locked. 47 synthetic tests are NOT live integration proof. Do not run against Azure or remove locks; obtain separately reviewed full exact-SHA candidate and new explicit Owner authorization first.

Business UAT stays NO-GO until current SQL catalog and API readiness prove the prerequisites. IT handover deferred.
