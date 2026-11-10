# Field Visit Path B — G1 one-shot sequencing correction (NONDEPLOY)

**Status: NONDEPLOY / NO LIVE AUTHORIZATION / NOT UAT READY.**

This package fixes exactly one source-level handoff flaw: the prior wrapper created its local irreversible `.consumed` marker *before* checking actual Azure Owner/management/instance and the original Parent's time-bound authorization. The corrected wrapper calls the pinned original Parent's read-only `management(cfg)` and `validate_authorization(...)` **before** marking the local attempt as consumed. The unchanged Parent still repeats these checks after launch.

The preclaim check is performed by importing the unchanged hash-bound Parent from an isolated directory containing the original verified files. It does not request SQL or storage tokens, SSH, Blob state, connect to SQL, or execute migrations. Only after successful local/management/auth checks does the wrapper atomically write the local claim and perform the original `--live` handoff. Any preclaim failure means the local claim is not created; any failure after the claim means **NO RETRY**. This improves one control but does not guarantee whole-flow success, nor prevent uncertainty or incomplete SQL transactions after launch.

## Exact preserved source
- Original embedded Candidate ZIP SHA-256: `8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141`
- Original embedded Binding Index SHA-256: `302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45`
- Release source commit: `d6c220877b9976b6478d85b5171d4e06293595ae`
- No modification to original Parent, 14 SQL layers, frozen installer, release, or old claims.

## Evidence
- 24 focused offline tests: PASS (6 added tests, 18 inherited from the previous single handoff), with no actual Azure calls.
- ZIP, CRC and SHA256SUMS verified in this artifact.
- `--check` validates candidate integrity only. It is *not* Azure readiness or deployment authorization.

## LIVE STOP — important
This correction is **not** a LIVE installer approval. The wrapper's `--owner-confirm` string and a matching 0600 JSON cannot independently attest fresh Owner consent. It is therefore prohibited to execute `--live` from this package until a separately controlled, verified, new exact-version Owner approval and complete execution-gate review have been established. The preclaim performs Azure management reads when invoked in live mode; it has not been exercised against Azure. Do not paste live commands into Cloud Shell, do not create self-authorizing JSON, and never reuse consumed approvals or Blob markers. New issuance/approval and stop/recovery procedure remain separately required.

## Progress
G1 SQL schema/ACL HOLD, G2 API deployment HOLD, Business UAT 0/25. Existing SWA front-end config.js already points at the correct Path B API; do not redeploy its prior rebind candidate. No new Azure resources, no extra diagnostics, no IT handover work.
