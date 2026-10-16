# Control Tower Hub — Nondeploy Design v1

**Issue:** #39  
**Source branch:** post-uat/v1.8.0 at 7f0cf0cb55467916fb0f57239301a8787fd5cb69  
**Status:** DRAFT / NO DEPLOY / NO AUTONOMOUS CHAT WAKE-UP

## Goal
Stop using the Owner as a manual relay between WORK A, WORK B, WORK C and independent AUDIT. GitHub Issue #39 is the append-only handoff ledger. GitHub Actions will eventually produce a read-only aggregate after independent review.

## Lane ownership
WORK A writes PR #33 staging only; WORK B writes PR #38 diagnostic branch only until an exact-SHA Owner merge; WORK C writes PR #37 partial RC only; AUDIT remains read-only; WORK D changes this separate coordination branch only.

## Standard handoff
Every lane reports after each *meaningful batch* under Issue #39, with `record_type=control-tower-handoff`, `schema_version=1`, lane, source PR, full head SHA, exact SHA CI run, evidence level, completed, blockers, next Delta, authorization boundary and release HOLD/NO-GO. The canonical schema is embedded in Issue #39.

## Nondeploy automation plan
1. Implement an offline validator for the allowlisted record schema. Reject invalid SHA, enum, missing provenance, unintended secrets/PII, and CI/SHA mismatch.
2. Add a read-only status aggregator with `permissions: {contents: read, issues: read, pull-requests: read, actions: read}`, limited to issues/PRs #39/#33/#37/#38 and Protected, triggered manually or by safe events only.
3. Aggregator generates ephemeral CI artifacts (Markdown and JSON), never modifies a branch or comments using a write token. Do not change existing deployment/SQL workflows.
4. Deduplicate by lane + PR + SHA + run ID. Do not infer live UAT validation from isolated tests. A new SHA invalidates old PASS until its exact-SHA CI is verified.
5. Add synthetic fixture tests for successful handoff, stale SHA, wrong CI, conflicting lane writers, credential-like content and missing Owner authorization.
6. After isolated CI, submit a separate Draft PR with explicit diff and Owner review. Do not enable privileged automation until separately approved.

## Human and agent operational SOP
- Each active WORK begins by fetching Issue #39, Protected, lane PR and current CI.
- It works only within previously authorized nondeploy scope, posts one append-only factual handoff on completion, and never rewrites another lane's record.
- AUDIT compares current SHA and only reviews impacted requirements.
- Owner Control Tower reads #39 and requests explicit one-time approval for Merge, UAT SQL, Migration, deployment, Restore, privileges, flags and Business UAT.
- Distinct ChatGPT chats **cannot be assumed** to wake or communicate directly via GitHub. Future automatic start/resume needs a separately supported scheduler or event integration.

## Gates
R1/R2 one-time Azure SELECT-only authorizations are consumed. PR #38 merge is still Owner-pending. R3 SQL must be independently approved and separately staged with hashes. PR #33 full Scheme A and PR #37 partial Leader are not approved for existing UAT deployment. All live UAT/Production operations remain HOLD.
