# WORK C delta readiness — 2026-10-16

Decision: HOLD. NONDEPLOY_ONLY. This delta preserves the partial Leader candidate and restores Visitor backward compatibility; it does not implement WORK A Scheme A.

## Exact inputs and scope

Starting C d664d4470d8b1dfad5ec5e0357f79562bfd956f4; Protected b1a507c90c577fb2c0cdd6a98ef33481d3a5f790 integrated as a second parent. Its diagnostic scripts/workflows are preserved byte for byte. A requested reference 08c0e463089ffe771d6957728067574328993555 moved to live PR33 head 4bf090fce8a3ed338b30db1a72120629e4751d4f at task start. Both pins are retained; no A branch write.

Historical observed Pages source 0899f3996618c4b2bd02ecd659c76fdd07cc91ed and recorded API deployment source db235938b8b13ca8a65ab3b525de1df10d911645 supply the Visitor compatibility contract. They do not identify today's active API binary.

## Visitor matrix (candidate execution pending exact CI)

| Requirement | Evidence required | Current release state |
|---|---|---|
| Entry/nav and /locations/manage | Chromium actual candidate React/API | HOLD pending new exact CI |
| Search/create/edit and eight fields | real HTTP/SQL plus browser persistence probe | HOLD pending new exact CI |
| Own draft only, CreatedByUserId, VisitorTeamDto | DTO assertions, other-owner 403, disabled browser action | HOLD pending new exact CI |
| Current role/HR/team/account and scope | disposable SQL mutation negatives | HOLD pending new exact CI |
| Leader current authorization and other-owner authorized draft | prior tests retained and rerun | HOLD pending new exact CI |
| Admin legacy create/edit, unauthorized/anonymous refusal | real HTTP/SQL role checks | HOLD pending new exact CI |
| Published/foreign historical row remains unchanged | real SQL assertion | HOLD pending new exact CI |
| Existing deployed Visitor behavior/business history | no authenticated UAT execution or business data accessed | HOLD |

Visitor policy is selectively restored from the recorded deployed API, with explicit organization matching, rejection of temporary/merged records and serializable Visitor draft writes. Existing Leader live authorization remains separate. Legacy maintenance/geocode/import restrictions remain intact. No database/domain/model/config/default flag change.

## Schema Fidelity

The migration inventory contains 40 Protected Up/Verify files (1600 two, 1700 eight, 1800 ten pairs). Available 015 verifies an already existing 1.5 schema; it does not create it. The tracked Gate1 design ZIP uses UUID keys and lacks the accepted SchemaVersions baseline. Neither is a trusted substitute. No approved schema-only 1.5 DDL, hash and provenance has been supplied. Therefore genuine 001–010 reference chain, expected catalog comparison and application principal permissions remain HOLD. EF EnsureCreated and absence of 011–020 tables prove only this fixture's boundary.

Actual UAT read-only inventory R2 run 38859687847 failed. No SQL was retried. PR38's bounded diagnostic change was integrated, not dispatched. Required future evidence: authorized exact inventory from the real catalog; trusted reference chain/catalog; principal identity and effective SELECT/EXECUTE/INSERT/UPDATE/DELETE permissions on required objects; migration prerequisite/version/constraints/index/FK/rowversion postconditions. No privilege or migration change is authorized here.

## Deployment and rollback

Use PREDEPLOY-EVIDENCE.md, ROLLBACK-READINESS.md and immutable evidence/existing-uat-observations.json for prior read-only observations. Pages source stamp correlates with run38338108364, source0899f39; original artifact11785810643 download returned404. API successful historical run38334707319 used source db23593 and OneDeploy ID2a2c4204-e53e-47af-8739-c00343a05fe8; active binary source remains unverified. Config and health labels do not prove it. There is no verified matched API/Pages rollback archive pair or settings fingerprint. Candidate packages are not rollback packages. No deployment, UAT SQL, health/ready or restore was executed.

Recovery remains a reviewed plan only: identify active deployment and paired immutable archives/settings; verify hashes and restore dependencies; obtain one exact-target/exact-SHA execution authorization; recover matched frontend/API settings and schema-compatible binaries; verify liveness and authorized business smoke; stop on unknown schema/binary mismatch. Database restore needs its own approved backup/PITR evidence and authorization. No downgrade SQL is assumed.

## Controlled integration plan

Common merge base C/A at start: 68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2. A is not a descendant of current Protected and includes canonical schema/model/provider changes. Never overwrite C with A wholesale or use a clean textual merge as acceptance.

1. Freeze exact current A/C/Protected SHAs and passing artifact hashes; re-read heads immediately before testing. Create only an isolated candidate ref/worktree, no A or Protected writes.
2. Resolve overlapping V160FinalRepository (Visitor/Leader live scope, transaction wrappers, DTO mapping), V160 contracts/interfaces/controller/service, Program, Google orchestration tests and frontend App/types semantically. Preserve the deployed Visitor contract and C scope gates; integrate A canonical services behind its default-OFF dependencies. Do not transplant C's minimal draft page over A's full Leader UI.
3. Keep Protected PR38 diagnostic scripts and SQL hash pins unchanged. Compare A migrations/model 011–020 separately from genuine Protected010; require explicit migration prerequisite/postcondition evidence before future release. Do not infer 010 compatibility for A's canonical paths.
4. Verify backend option flags and frontend build/runtime flags as separate gates; retain shared environment OFF defaults. Deployment patch remains inert, requires exact source/artifact SHA correlation and a verified rollback pair, and cannot activate flags.
5. Nondeploy gates: full backend/frontend tests; real SQL provider/transaction/concurrency negatives; Visitor/Leader/Admin/anonymous regression matrix; browser navigation/create/edit plus independent persistence/audit probe; canonical review/import authorization and history/merge protections; exact model/schema fidelity and feature-OFF route checks. A's final P1 provider race/deadlock evidence needs separate audit acceptance.
6. Publish isolated candidate exact CI and conflicts/limitations. Only after independent audit and Owner review can an integrated candidate replace current release decision. No UAT GO is implied.

## Owner release decision sheet

| Gate | Decision / unresolved requirement |
|---|---|
| C engineering | Await new exact-SHA CI; partial Leader + Visitor compatibility only |
| Full Owner A1–A15 | HOLD; A engineering and independent acceptance remain required |
| True010 schema/app principal | HOLD; trusted baseline/catalog/permissions missing |
| Active API provenance | HOLD; authenticated active deployment identity not available |
| Pages archive/paired rollback | HOLD; original archive not verified and no binary pair |
| Cross-lane integration | HOLD until semantic integration and combined exact tests |
| Business UAT/history | HOLD; no real-user acceptance performed |
| Execution | NONDEPLOY_ONLY; no merge, SQL, migration, deploy, restore, privilege or flag grant |

## Hub operating adoption

Read reviewed SOP/schema at Hub14d5fc005587b260723a316a586278a7a886daa0 and Issue41. Downloaded artifact12037935872 from successful producer38881037875 and inspected status.json. It records A8f69816/Cd664/Protectedb1a507c; A is stale against live4bf090f and C will become stale after this delta. Treat it as historical, not current acceptance. WORK_C had no marked handoff at task start. Completion must append schema-validated fixed-code JSON, increasing sequence, exact successful C CI and retrievable evidence hash, then read back. Hub branch/activation remains untouched.
