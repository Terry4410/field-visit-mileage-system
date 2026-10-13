# UAT010 × Package B — Option C integration contract (NONDEPLOY)

**Owner decision (2026-10-13):** One canonical high-risk location approval engine. This decision authorizes isolated implementation work, **not** Azure UAT/Production deployment, migration, feature enablement, or promotion.

## Frozen source/provenance
- Integration branch forked from frontend role UI commit `6a07a8fb0053a1032aabe76a65aed37f40dd3c4a`.
- UAT010 delivery bundle: `65b2649ef902142b01394ef79559261cca10c9fc`, parent `6a07a8fb...`, ZIP SHA-256 `4c2124e44b2e4e1373077acf0332bbb9145b74afe8ea4e7d1b089ed7f6de3c3f`. **Not yet imported to GitHub**. Treat as design/reference, never claim integrated without tree/diff proof.
- Package B comparison only: `work/v180-package-b-b2-b3-controlled` @ `c4ed7b9f053d9c3aac320b8c70321b1451d67e53`. DO NOT mutate this branch.
- Current UAT backend `db235938b8b13ca8a65ab3b525de1df10d911645`, frontend `0899f3996618c4b2bd02ecd659c76fdd07cc91ed`, DB reported `1.8.0-010`. These are **not** approval of this integration.

## Canonical contract
1. Use one request/event model: `ChangeRequests` and append-only `ChangeRequestEvents`, with org/team/owner and current HR/role checks.
2. `Pending` requests must be unique per (OrganizationId, EntityKind, EntityId) at SQL filtered unique index level; `DecisionKey` unique for reviewer decisions. Treat SQL 2601/2627 as conflict only for recognized indexes.
3. Only an independent current Admin can review; request and location RowVersion both validated. Acquire request then location locks; audit event and location update in one transaction. Existing Package B `ApproveAsync` is DENY ALL and must remain so until separately authorized release gate.
4. Visitor/Leader single-record, Leader Excel, Admin changes and legacy batch APIs share server-side risk classification. No UI-only protection. Low-risk owned Pending/Inactive Customer drafts may save directly; Approved/Active/high-risk changes must be staged.
5. Geocoding changes coordinates/status only. It must not auto-approve, auto-publish, or deactivate an Approved/Active location. Pending new address needs an explicit publication policy; default fail-closed pending Owner/IT design.
6. Legacy `BatchPublishAsync` is a known direct publication bypass and must be blocked until replaced with a canonical admin-only, audited publication service. Excel Admin cannot directly mutate approved location.
7. No parallel `LocationChangeRequests` engine. **Never run** UAT010 standalone `1800_011_uat010_location_change_governance` or Package B 011 without reviewed unified schema.
8. Unified 011/012 SQL design must be independently reviewed; 013 is already reserved in Package B candidate. No SQL against UAT or production.

## Work gates (W0–W6)
- W0: establish isolated GitHub branch and provenance. **Not a sync of local 65b2649.**
- W1: canonical DTO/EF/DDL IA and explicit feature flags OFF.
- W2: stage, independent review, atomic apply and live authorization with tests.
- W3: close all write/publish bypasses, including Excel, geocoding and legacy batch publish.
- W4: single-record Visitor/Leader UI, Excel Leader, one Admin review queue.
- W5: exact-SHA nondeploy .NET 8/Frontend tests, SQL Server 2022 disposable 011/012 Up/Verify and concurrency/rollback security tests. Baseline CI is NOT a substitute for unified schema runtime proof.
- W6: Owner/IT impact review and separately authorized Migration/Backend/Frontend UAT deployment.

## GO/NO-GO
**NO-GO** until canonical integration is complete, exact SHA verified, full negative security and isolated SQL tests PASS, migration/rollback hashes approved, Owner separately authorizes Azure UAT migration and each deployment. Keep `main`, `post-uat/v1.8.0`, Package B B3/B4, UAT DB, Production untouched.
