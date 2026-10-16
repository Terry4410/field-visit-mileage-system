# Owner release decision sheet — evidence readiness only

**Recommended decision: KEEP HOLD. No GO, merge or execution is requested.**
Implementation anchor: `811bf37a1d79742ea1f6a2de734e4a0d4af34f05`; Protected `7f0cf0cb55467916fb0f57239301a8787fd5cb69`. The preflight continuation changes evidence/manifest/CI only, not the partial Leader implementation or WORK A.

| Requirement | Evidence now obtained | Remaining condition |
|---|---|---|
| Pages deployed source | Public source-sha.txt = `0899f3996618c4b2bd02ecd659c76fdd07cc91ed`; successful deploy run 38338108364; public config hash equals that source | Original Pages archive unavailable, HTTP 404; full deployed archive comparison/rollback remains HOLD |
| API deployed source | Historical successful run 38334707319 checked out `db235938b8b13ca8a65ab3b525de1df10d911645`, stamped package and deployed via OneDeploy `2a2c4204-e53e-47af-8739-c00343a05fe8` | Current active Azure deployment and binary hash unverified; live source stamp is 404. Health reports version/configured schema label only |
| Protected 001–010 reference | Forty immutable prerequisite/001–010 Up/Verify files inventoried; disposable reference builder prepared | No approved schema-only 1.5 CREATE baseline/hash/provenance; tracked old ZIP is UUID-key Gate1 design draft, not accepted baseline |
| 010 source compatibility | EF model, all Domain files and migration tree match historical API source exactly; SaveManagedLocationRequest unchanged | Source equality does not prove live catalog facets, triggers, constraints or app-principal permissions. Actual inventory run 38859687847 failed; no retry performed |
| Leader isolated execution | Previously passed real HTTP/SQL and Chromium→running API→SQL at 811bf37; continuation CI reruns exact source | Partial scope only; no actual Business UAT claim |
| Existing Visitor functionality | Source comparison identified removal of deployed Visitor nav/route/API contract from this Protected-based RC | **P0 RELEASE BLOCKER.** Resolve through the single WORK A/Control Tower integration plan, or obtain an explicit scoped disposition. Do not silently accept loss of existing functionality |
| Binary rollback | API run retains only test reports; logged Pages artifact 11785810643 cannot be downloaded (404) | No verified immutable prior API+Pages binary pair or settings fingerprint; source rebuild is not binary identity |
| Exact-SHA preflight | Manifest binds candidate HEAD/ref, source stamps, package hashes, evidence snapshot hash and HOLD gates | Not execution authorization. Active-source, reference, Visitor non-regression and rollback gates remain HOLD |

## P0 — regression discovered against recorded deployment sources

1. Live Pages-stamped source `0899f39` includes Visitor navigation and route `/locations/manage` using `ScopedLocationPage`. Current RC `frontend/src/App.tsx` removes both.
2. Historical API source `db23593` allows Visitor GET/search/POST/PUT on managed-locations and provides `/managed-locations/visitor-teams`. Current RC controller lacks the endpoint and removes Visitor authorization.
3. Historical `V160FinalContracts.cs` includes `VisitorTeamDto` and `ManagedLocationDto.CreatedByUserId`; current RC lacks them. The old Visitor UI uses the creator field for draft ownership controls.
4. This is a confirmed source-level regression against the observed frontend stamp / recorded backend source, **not a newly executed UAT business test**. Current live API SHA remains unverified. The earlier Leader-only CI deliberately denied Visitor and did not establish preservation of the deployed Visitor feature.

The preflight comparison test detects and records HOLD; its successful execution is not a regression acceptance PASS. WORK C does not patch Visitor or duplicate WORK A Scheme A engineering in this continuation.

## P1 — review/operational gaps

- LowRisk, HighRisk, independent canonical Leader review, canonical Excel and merge remain excluded. No complete Leader or Scheme A acceptance.
- Legacy master PUT / Excel confirm / geocode are Admin-only in this RC; Visitor legacy master PUT impact and old Leader actions need explicit review.
- Original API deployment pipeline publishes to Azure without retaining a deploy package artifact. Pages original artifact retrieval returned 404; reason is not inferred as expiration versus deletion/access.
- Available UAT Playwright reports are test diagnostics, not deploy/rollback archives, and were not downloaded because they are unnecessary for binary provenance and may contain runtime content.
- Azure App Service deployment/status and GitHub deployment/Pages metadata endpoints are not exposed by the current connector; direct public app reads do not replace authenticated active-deployment proof. No matching App Service management plugin was found; unrelated hosting plugins were not suggested.

## Required next evidence / release authorization boundary

Control Tower should coordinate these independent gates: (1) resolve existing Visitor regression using the single implementation writer; (2) obtain approved genuine baseline plus synthetic reference chain and independently authorized UAT metadata/app-principal evidence; (3) obtain authenticated read-only current API deployment/source/package evidence; (4) preserve and independently verify a matched prior binary pair/settings fingerprint; (5) review exact new candidate CI/manifest and partial feature tradeoffs.

Only after all gates are separately satisfied should one consolidated Owner execution request name exact candidate and rollback artifact IDs/digests, API/Pages target IDs, schema compatibility evidence, canary scope, execution order, expiry and recovery criteria. This sheet itself grants no operation, retries no consumed authorization, and leaves release-gates.json HOLD.

## Frozen Owner requirements alignment — not a substitute for WORK A/AUDIT

Authority read directly: PR #33 frozen Owner comment 6211550638 and audit checklist 6211568211. This is a preflight scope/decision cross-reference, not a duplicate A1–A15 engineering audit.

| Frozen item | PR #37 partial candidate disposition |
|---|---|
| A1–A9: HR primary site, Visitor Google/save/submit/return/resubmit, fallback and Leader review/retry | Outside this lane. Coordinate WORK A and independent AUDIT; no full lifecycle or actual UAT PASS claimed here |
| A10: scoped pending-location review tab | Canonical review is excluded/disabled in narrow 010 RC. No operational review tab or endpoint acceptance |
| A11: own drafts vs maintained-for-others labels/two views | Current RC has a single table, no creator-vs-maintainer labeling, and lacks CreatedByUserId DTO. Frozen two-view requirement remains unmet |
| A12: temporary Trip stop TaxId/MasterNote persistence | Not implemented/validated by this preflight continuation; do not equate eight master fields with temporary-stop/snapshot parity |
| A13: literal button 儲存地點草稿 and accurate saved/submitted state | Current Leader page uses 新增地點（待審核） / 儲存草稿修改（待審核）; frozen literal wording is not met. Do not rename or change semantics in this separate readiness lane |
| A14: all-user published-write/merge/geocode/UseExisting safety and history | Narrow Leader draft negatives PASS in isolated tests, but existing Admin legacy mutation/merge remains outside this upgrade. Full all-user canonical/history invariant cannot be certified; HOLD |
| A15: actual catalog/permissions/provenance/rollback | Explicitly tracked as HOLD/partially observed in this package; no inference from isolated CI |

Owner's frozen note explicitly says not to revive PR #37 as a surrogate for the full requirements. Keep this RC preserved for reviewed reuse; do not recommend its standalone deployment as completion of the Owner scope. The P0 Visitor regression requires coordinated integration, not a silent tradeoff or a test expectation change.
