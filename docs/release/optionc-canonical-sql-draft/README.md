# Option C canonical 011/012 SQL — nondeploy copy for isolated review

**HOLD / NO-GO / NOT EXECUTED.** This directory contains byte-for-byte copies of Package B source branch `work/v180-package-b-b2-b3-controlled` as an immutable starting point. The original candidate files and protected branches remain unchanged. No UAT migration workflow reads this directory. Copying these drafts does not resolve the Option C versioned-location publication contract.

- Source: `work/v180-package-b-b2-b3-controlled` @ `c4ed7b9f053d9c3aac320b8c70321b1451d67e53`.
- 011 creates `dbo.ChangeRequests` and `dbo.ChangeRequestEvents`, never `dbo.LocationChangeRequests`.
- 012 permits audited Approved/Applied atomic transitions.
- 013 remains RESERVED for leader grant; do not repurpose.
- 011 must not run against UAT without exact-schema read-only preflight and explicit Owner migration authorization.
- No SQL from this directory is automatically executed by CI; only static provenance checks are permitted.
- The current executor rejects published identity/geocode-sensitive changes until a versioned swap is reviewed. The candidate 011/012 schema **does not yet** supply that versioned swap.
- Further work: adapt request operation/role policy for Visitor + Leader, define publish/re-geocode lifecycle, test 011/012 on disposable SQL Server 2022 with rollback, concurrency and idempotency, and obtain exact-SHA UAT authorization.

Source Git blob SHA-1 values are pinned in `scripts/ci/test_optionc_sql_draft.py`; no silent drift is allowed.
