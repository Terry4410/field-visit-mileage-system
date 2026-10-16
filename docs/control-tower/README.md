# Control Tower Hub — WORK D validation and operating SOP

Canonical coordination: [Issue #41](https://github.com/Terry4410/field-visit-mileage-system/issues/41). Implementation/review: Draft PR #42. Execution authority is always **HOLD**.

## Scope and architecture

This continues the existing Hub. Only `work/control-tower-hub-20261016` is writable by WORK D. No WORK A/C, Protected, main or deployment writes. Actions has contents/actions/issues/pull-requests **read** only; checkout credentials are not persisted; action commits are pinned. The publication adapter is deliberately separate from the read-only Actions collector.

| Lane | Source | Interpretation |
| --- | --- | --- |
| WORK_A | PR #33 exact head | Current engineering claims |
| WORK_C | PR #37 exact head | Current readiness claims |
| WORK_B | PR #38 exact historical head | MERGED_HISTORICAL; never infer current deployed state |
| WORK_D | PR #42 exact head | Hub engineering claims |
| AUDIT | PR #33 exact head | Audit-scoped claims; consumes delta events for all lanes |
| OWNER | Protected exact head | Coordination claims; never an execution grant |

The collector reads fixed GitHub sources and exact-head Actions runs, then scans Issue #41. It applies the actual local `handoff.schema.json` through an offline bounded evaluator. The evaluator supports exactly the checked-in vocabulary, rejects unknown keywords/dialects and duplicate JSON keys, never fetches remote `$ref`s and does not silently repair payloads. It is not a general-purpose JSON Schema implementation. The only new Lane enum is WORK_D; old enum and Evidence restrictions remain. Existing invalid records do not become valid through a schema expansion.

Every valid current handoff must bind all CI IDs to successful runs on its exact source SHA. Every pure SHA-256 Evidence value must equal an **unexpired GitHub artifact's registered archive digest** from one of those CI runs, with matching artifact head SHA. Missing metadata/digest, expiry, mismatched run or hash means INVALID/HOLD. This verifies GitHub metadata provenance, not artifact semantics or byte contents. An independent audit must still download, hash and inspect the evidence. CI success does not prove requirement compliance, deployed UAT or Business UAT.

Exact-head validation runs publish a separate `control-tower-validation` artifact. A WORK_D handoff can cite the completed PR validation run while the read-only collector refreshes in a different push run; it must not cite its own in-progress run as successful evidence.

The report contains current refs, CI IDs/attempts/status, safe acceptance codes, authorization, artifacts, schema hash, source commit/PR/comment timestamps, collector SHA/run and collection timestamp. No PR titles/bodies, raw comment text, actor/employee/business data, SQL text, log excerpts, tokens or exception strings enter artifacts. Invalid records contain only numeric comment IDs, recognized lane/sequence and fixed diagnostics. Source and handoff stream are re-read after collection; movement fails closed. API errors are generic HOLD messages.

## Historical invalid handoff — preserved, not normalized

- `6214053257`: schema-valid WORK_A Sequence 1; historical source SHA. It is not accepted evidence for a different live SHA.
- `6216822321`: invalid WORK_A Sequence 3. Unknown Completed/Blockers enums, free-text NextDelta enums and Evidence pattern failures are independently tested using the exact original body in a fixture. The report shows INVALID/HOLD with those four fixed diagnostic categories. It never emits the invalid raw values or silently falls back to the old valid record.
- Historical comments are not edited or deleted. A correction is a **new** record, using the actual current source/CI/evidence. WORK D must not rewrite WORK A's payload on its behalf.

`SCHEMA_VALID_ONLY` is the local syntax/contract result. It is not `HANDOFF_VALID`. Only the publish/readback verifier may produce `HANDOFF_VALID`.

## Reusable publish/readback path

The supported GitHub connector can append Issue comments. It has no safe atomic compare-and-swap operation on an Issue comment stream. The CLI therefore performs GET-only validation and the connector performs the single explicit publication. No issue-write token or privileged workflow is added.

1. At task start, read Issue #41, this SOP from the reviewed WORK D commit, actual schema, live source SHA, CI and current report. Treat old artifacts as historical unless compared with live refs.
2. Build a payload matching the actual schema. Keep `OwnerAuthorization=NONDEPLOY_ONLY`. Evidence is pure lowercase archive SHA-256, CI is numeric Actions run ID. Use fixed codes for Completed/Blockers/NextDelta; keep human detail in a separate ordinary comment. Do not put business data into hashes.
3. Find the lane's sequence frontier, including typed sequences in invalid **trusted** historical records. WORK_A's specified invalid Sequence 3 is reserved, so a later correction starts above 3 (and above any newer record found at publication time). Do not reuse or normalize it.
4. Prepare and immediately recheck:

```bash
python3 scripts/control_tower/hub.py --validate /tmp/handoff.json
python3 scripts/control_tower/publish.py prepare --payload /tmp/handoff.json --plan /tmp/handoff-plan.json
python3 scripts/control_tower/publish.py check --plan /tmp/handoff-plan.json
```

Prepare reads the actual schema, validates payload and monotonic Sequence, confirms source/CI/artifact metadata, rechecks source/stream, then writes a schema-bound plan with the **exact comment body**. Outputs are `SCHEMA_VALID_ONLY`, `PAYLOAD_VALIDATED_NOT_PUBLISHED`, and `PUBLICATION_READY_NOT_PUBLISHED`, respectively. No success here claims delivery.

5. Through the supported GitHub connector `add_comment_to_issue`, append `plan.body` to repository `Terry4410/field-visit-mileage-system`, Issue #41. Pass structured arguments (`repo_full_name`, `pr_number=41`, `comment=plan.body`); never interpolate the body into shell commands. The body is exactly marker `<!-- control-tower-handoff:v1 -->`, newline, canonical JSON; no Markdown fences. Never pass arbitrary comment instructions to a shell.
6. Read back and revalidate:

```bash
python3 scripts/control_tower/publish.py verify --plan /tmp/handoff-plan.json --comment-id COMMENT_ID --receipt /tmp/handoff-receipt.json
```

The verifier GETs the persisted comment, checks Issue membership, author, exact body hash, current schema, source SHA, successful CI, artifact digest metadata and latest stream/Sequence. It then re-reads source and stream. Only after all checks does it return `HANDOFF_VALID COMMENT_ID` and a receipt. Include the comment URL and receipt evidence in the completion report; release authority remains HOLD.

7. If delivery is ambiguous, **do not append again**. Fetch Issue comments and locate the exact planned body; call verify with its ID. Multiple distinct comments with the same Sequence are INVALID/HOLD even if bodies match. A duplicate delivery of the same comment ID is idempotent. If verify fails, report HANDOFF_PUBLISH_HOLD; preserve the original comments and prepare a higher-sequence correction only after reviewing the conflict.

If no supported publication connector is available, stop before publishing and deliver the prepared validated payload/plan for manual publication. That is `PAYLOAD_VALIDATED_NOT_PUBLISHED`, never HANDOFF_VALID. The operator must still run readback verification. No token/PAT/password is requested in ChatGPT. Local public reads need no credential; authenticated reads may use an existing least-privilege GH_TOKEN outside the payload.

## Concurrency, replay and delta behavior

Comment IDs establish publication order. Same-ID duplicate events deduplicate; same-ID different-body events fail the collection. A lower Sequence arriving later is a replay and remains INVALID/HOLD. A new higher Sequence can resolve the lane's latest conflict, while all rejected records remain in report history. Untrusted authors cannot advance the trusted frontier. Edited machine comments are invalid; always append corrections.

Prepare/check uses a stream fingerprint as an optimistic precondition; any intervening marked handoff invalidates the plan. Readback checks that the persisted record is the lane's latest valid observation. Two simultaneous publishers can still both pass prepare before either writes: after both comments are visible the duplicate Sequence puts the lane in HOLD, and both verification paths fail. A conflict arriving **after** an earlier readback can invalidate that earlier result on the next collection. This is fail-closed optimistic conflict detection, not an atomic distributed lock. Unrelated conversation writers must follow the same publication SOP; they are not controlled by this branch.

Actions concurrency serializes collectors with cancel-in-progress false. Pending runs may coalesce; this is current-state convergence, not a lossless event bus. Artifact names include immutable run ID and attempt suffixes (`control-tower-state-RUN-ATTEMPT`, `control-tower-validation-RUN-ATTEMPT`), so retries never overwrite/delete the prior evidence. Baseline selection supports older legacy names and chooses the latest valid prior-run attempt. The previous successful same-branch artifact is compared with current state; missing/expired baseline emits explicit BOOTSTRAP events, failed retrieval aborts. State/digest and source provenance are retained. Artifact retention is 30 days; Issue comments remain administratively editable/deletable, so no tamper-proof ledger is claimed.

Only source SHA/state, acceptance/CI status, Evidence, Completed/Blockers/NextDelta/authorization or validation-gate changes produce audit events. Timestamp-only changes and a valid Sequence-only increment do not. Event IDs are deterministic; AUDIT stores consumed IDs to avoid replay notifications. The notification channel is `status.json.audit_queue` plus Actions Job Summary, not external messaging. No automatic release approval exists.

## Actions operation and realistic automation boundary

Pushes to the authorized Hub branch run isolated tests and GET-only aggregation. PR events validate only; the collector explicitly allows **push/workflow_dispatch on the exact Hub branch and fixed repository**. Code and workflow guards reject workflow_run, issue_comment, repository_dispatch, schedule, other refs and repos. Manual dispatch availability is not assumed before default-branch workflow registration. No unreviewed schedule/event/default-branch installation is performed.

GitHub Actions can test, read GitHub metadata, compare states, classify invalid handoffs and publish dashboard artifacts automatically when this installed workflow runs. It cannot infer business acceptance, grant authorization or start/resume arbitrary existing ChatGPT WORK conversations. Each conversation must be invoked/resumed and adopt the start/completion SOP once. The existing supported GitHub connector supplies read/comment capabilities; it is not a ChatGPT conversation scheduler.

Future automatic task dispatch would require a separately approved GitHub App/webhook or polling service **and a documented supported task-execution/invocation API**. No such interface for waking these existing Work conversations is available or verified in this implementation. It must be confirmed before claiming dispatch support. Do not simulate it with repository comments, browser automation or privileged workflow triggers. Default-branch continuous read-only reconciliation, if desired, also requires separate Owner review and updating guards/baseline configuration; WORK D may not write that branch here.

## Owner acceptance and operating checklist

- [ ] Review PR #42 exact SHA/diff: only Hub branch/paths; existing lane and Protected refs untouched by WORK D.
- [ ] Verify exact-head CI and isolated negative-test evidence.
- [ ] Download the latest control-tower-state artifact, verify archive digest and JSON fingerprint, inspect provenance/timestamps.
- [ ] Confirm #6216822321 remains INVALID/HOLD; #6214053257 remains historical schema-valid only and cannot approve a newer source.
- [ ] Confirm all six lanes including WORK_D and historical WORK_B are visible, with no free-text/sensitive values exported.
- [ ] Verify WORK D's real prepare/check/connector/GET/readback receipt. Adopt the same path in WORK A/C/AUDIT/OWNER.
- [ ] Verify duplicate/replayed/out-of-order/concurrent records and wrong CI/artifact hashes stay HOLD; historical records remain.
- [ ] Keep independent requirement, isolated test, deployed UAT and Business UAT acceptance separate.
- [ ] Separately review any continuous default-branch collection or future task-dispatch integration.
- [ ] Keep Protected merge, UAT SQL, migration, deploy/restore, privilege changes and flags separately authorized.

Rollback: stop using the draft Hub branch/close the draft PR through a separately requested action. No application runtime/SQL/deployment/flag rollback is needed. Do not alter the other lanes.

## Validation commands

```bash
python3 -m unittest discover -s scripts/control_tower -p 'test_*.py' -v
python3 scripts/control_tower/hub.py --output /tmp/control-tower-hub
python3 scripts/control_tower/hub.py --previous /tmp/control-tower-hub/status.json --output /tmp/control-tower-hub-next
```

Python standard library only; no SDK installs. Tests use synthetic clients plus the two preserved historical handoff fixtures. No test executes SQL or contacts UAT. Live aggregation uses fixed-host GitHub GET requests only and suppresses raw provider failures. See `docs/control-tower/validation.md` for evidence scope.
