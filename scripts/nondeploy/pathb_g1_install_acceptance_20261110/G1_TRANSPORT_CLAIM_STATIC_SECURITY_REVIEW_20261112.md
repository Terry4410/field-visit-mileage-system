# G1 Kudu transport and one-time claim — source-level security review

**Decision: CONDITIONAL HOLD for live execution.** This is a static review, not an independent live security certification or Owner authorization. Reviewed PR #86 source HEAD `93b407f9e44473d11a2ac0c209d79ee71aff2412`.

## 1. Source-level controls confirmed

| Boundary | Source-level observation | Decision |
|---|---|---|
| Distribution | Parent verifies exact outer SHA, 22 exact archive entries and each binding digest before Azure management | STATIC PASS |
| Management target | Existing RG/App/SQL only; one instance selected; Owner and private SQL management checks | STATIC PASS; LIVE UNVERIFIED |
| Kudu stage | Receiver checks exact frame length and SHA before extraction, rejects duplicate/absolute/traversal/symlink members; exclusive stage directory | STATIC PASS; LIVE UNVERIFIED |
| Token handoff | PTY echo control and secret-line protocol; two bounded token handoffs after Stage ready | STATIC PASS; LIVE UNVERIFIED |
| Pre-DDL SQL | Frozen preflight, 22-field strict parser, fresh empty DB and TLS/identity checks precede claim | STATIC PASS; LIVE UNVERIFIED |
| One-time claim | New Blob key; checks predecessor keys; exactly one conditional create-only PUT, readback; unknown outcome stops | STATIC PASS; LIVE UNVERIFIED |
| DDL execution | Frozen installer and 14 SQL layers unchanged; single child group; no auto retry | STATIC PASS; LIVE UNVERIFIED |
| Receipt | Fixed final receipt and bounded SQL count; no PASS without 19 calls, 14 layers, postcheck and cleanup | STATIC PASS; LIVE UNVERIFIED |
| Cleanup | Exact owned Stage tree, SHA-bound files, bounded process checks and second-session verification; UNKNOWN -> HOLD | STATIC PASS; LIVE UNVERIFIED |

## 2. Non-waivable live risks

1. **Interactive Kudu channel and PTY behavior** have never been proven on the exact App instance for this new source revision. Static checks and synthetic ZIP assembly cannot prove terminal echo suppression, reliable framing, token secrecy, or actual process-group cleanup.
2. **Live SQL state remains UNKNOWN.** Historical SQL attempt outcomes must not be treated as evidence of an empty database. A fresh authorized preflight must prove exact identity and empty schema before the one-time claim.
3. **One-time claim is irreversible.** Once the create-only Blob PUT is attempted, ambiguous success is consumed; do not retry even when the installer fails or transport drops. No automatic compensation or schema rollback is guaranteed.
4. **Short-lived Owner authorization is absent.** A constant named `OWNER_APPROVED_...` in source is only a string comparison, not actual Owner consent. Exact source ZIP SHA, binding SHA, instance SHA, new operation ID, expiry and one-attempt scope must be explicitly approved.
5. **No live compatibility claim:** ODBC tool binaries, Azure Private DNS, Kudu availability and token audiences are not verified by the offline suite.

## 3. Owner decision boundary

- This review does **not** recommend immediate live execution from CHAT.
- Do **not** issue an authorization file or consume a claim on the basis of offline tests alone.
- To proceed, obtain independent human approval of the new Kudu transport and one-time-claim design, then a fresh exact-scope Owner authorization. Verify exact App instance and time window at execution; fail closed if any gate differs.
- If review cannot be completed, retain **G1 HOLD**; do not create another Azure resource or diagnostic branch as a workaround.

## 4. Immutable evidence

- Offline CI [43239102686](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/43239102686): **45/45 PASS** (27 focused, 18 integrated).
- Source-only ZIP SHA256 `a9690b6cbe0d1bf44f00292bcfa7d92b64ee409462292b98f35e21d767d460fe`.
- Binding SHA256 `e8fc28417976074ea8c3514c1047a939743641ba22b1bd7b0dae1314e7675272`.
- No Azure/Token/SQL/DDL/Blob/Deploy/Merge executed during this review.

**FINAL: STATIC_CONTROLS_REVIEWED; LIVE_AUTHORIZATION=ABSENT; G1=HOLD; SQL=UNKNOWN; BUSINESS_UAT=0/25.**
