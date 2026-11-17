# Path B Acceptance Recovery subgate classification (NONDEPLOY)

This directory preserves and minimally patches the exact nonsecret wrapper used
by the terminated read-only Acceptance Recovery recorded in Issue #41 comment
`6947423520`.

## Provenance

- Original wrapper SHA-256:
  `d7cbdbe3dc942170c7614ed43b7ed16f4cd5465939807e3a1001f648f6ad0347`
- Fixed G1 source commit:
  `d4c16becf622119527e9b1a4b7c302f37d818415`
- Offline stop-loss analysis: Issue #41 comment `6948903443`

`original_pathb_acceptance_recovery_blob_readonly.py` is the byte-exact recovered
source. `pathb_acceptance_recovery_blob_readonly.py` changes only machine-safe
failure classification for these fixed subgates:

- `OWNER_INSTANCE_BINDING`
- `STORAGE_TOKEN_REQUEST`
- `STORAGE_TOKEN_VALIDATION`
- `CLAIM_GET`
- `CLAIM_CONTENT_VALIDATION`

Each subgate reports only `PASS`, `HOLD`, or `NOT_RUN`.

Independent NONDEPLOY safety clarifications:
- The authenticated **current** App worker is checked independently from the
  immutable Claim's **historical** worker. A post-G1 restart must not by itself
  invalidate a valid consumed Claim. Its historical digest is format-checked and
  verified by the original exact Claim validator without substituting the
  current App instance.
- HTTP 404 from exact Claim/postcheck Blob GET proves absence only with
  `x-ms-error-code: BlobNotFound`. Generic, missing-code, and unrelated 404s
  remain UNKNOWN/HOLD; they must never become evidence of a missing Claim.

The existing stop on the
first failure, single Storage-token request, exact GET allowlist, no retries, and
no Blob writes remain unchanged. Raw errors, tokens, Owner identity, Claim
content, and business data are never printed.

## Offline verification

```bash
python3 -m py_compile scripts/nondeploy/pathb_acceptance_recovery_subgate_20261117/*.py
python3 -m unittest -v scripts/nondeploy/pathb_acceptance_recovery_subgate_20261117/test_pathb_acceptance_recovery_blob_readonly.py
```

The negative tests inject all Azure/Blob dependencies. They never execute the
recovered wrapper as a live program and never contact Azure. They verify every
subgate failure, 404 versus transport-unknown handling, redaction, exact Claim
and postcheck GET targets, and that no later Token or Blob request occurs after a
failure. Additional offline cases cover App instance rollover, malformed
historical Claim binding, generic Claim 404, and postcheck 404 ambiguity.

## Authorization boundary

This is source review and NONDEPLOY test evidence only. It is not permission to
run either wrapper, request a Token, read a Blob, query SQL, replay the terminated
Recovery, rerun G1, deploy, merge, or change Azure.

