# WORK D hardening — validation scope

The baseline is PR #42 SHA `14d5fc005587b260723a316a586278a7a886daa0`; this batch modifies only WORK D's Hub paths. Exact final commit/run/artifact and live publication receipt are recorded in the PR and Issue #41 after CI, avoiding self-referential commit/hash claims in source.

## Isolated gates

- Schema is read from the actual JSON file, with bounded offline keyword evaluation and strict JSON parsing. Tests cover unknown Completed/Blockers/NextDelta enums, non-pure Evidence hashes, extra fields, invalid types/bounds and unauthorized authority.
- Exact historical comment #6216822321 is INVALID with Completed ENUM, Blockers ENUM, NextDelta ENUM and Evidence PATTERN errors. It is not normalized. Prior #6214053257 is schema-valid, but historical/stale for a newer source.
- CI binding checks exact source SHA, fixed repository, success/completion and numeric run IDs. Artifact digest metadata must match Evidence on that same CI source, be unexpired and bind the same source SHA.
- Duplicate same-ID GitHub events deduplicate; separate duplicate Sequences HOLD; replayed lower Sequences HOLD; conflicting same-ID event bodies fail closed. Delivery ordering is reconciled by original GitHub comment ID.
- Two concurrent plans sharing a Sequence are exercised: after both persist, readback verification rejects both and aggregation reports HOLD. Late conflicts can invalidate an earlier result on next collection; no atomic lock is claimed.
- Source/stream movement, wrong readback author/body/Issue, changed schema plan, network failures and stale publication preconditions are rejected. HANDOFF_VALID cannot come from prepare, check or local schema validation.
- Wrong trigger events, repository and refs are denied before collection. Test workflow has no issue/contents write, OIDC, Azure/UAT environment, secrets or SQL capability.
- Delta tests cover SHA change, Evidence change, deterministic event IDs, timestamp/Sequence-only no-op and repeated baseline empty queue.

## Real evidence / remaining limits

The GitHub push CI must execute isolated tests, select/download the prior successful Hub artifact, aggregate PR #33/#37/#38/#42 + Protected and Issue #41, then publish the sanitized dashboard/JSON. The WORK D conversation downloads that exact artifact, verifies its GitHub archive SHA-256 and internal state fingerprint, and checks the invalid historical row and all lane/source fields.

A real WORK_D payload uses final exact PR #42 SHA, successful exact-head CI ID and the archive digest from that run. It is prepared and rechecked locally, published once with the supported GitHub connector, read back via GitHub GET and independently revalidated. The receipt gives the numeric comment ID, source SHA, Sequence, schema/body hashes and linked artifact metadata. No historical A/C payload is rewritten or re-published by WORK D.

Collector Evidence verification is registered GitHub archive-digest **metadata** validation. It does not download each arbitrary lane's evidence, execute artifact contents or certify semantic correctness. The delivered dashboard ZIP itself is independently byte-hashed during live QA. All source claims remain subject to independent audit and UAT remains HOLD.

No default-branch activation, continuous schedule, conversation wake-up, external task dispatch, release approval or application/SQL execution is part of this batch. Existing lane work proceeds independently. A live artifact is a timestamped snapshot, not proof that its sources remain unchanged later.
