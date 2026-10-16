# WORK C — Existing UAT minimal Leader RC (NONDEPLOY)

Current reconciled Protected base: `7f0cf0cb55467916fb0f57239301a8787fd5cb69`.
Original engineering base: `d691fba430ce80dd2048f84863b4628e1e6713d8` (retained in ancestry; not deployed-binary evidence).
Selected R9 source: `95b1987c7f9f92bf589f2f9aecb0632d5d880325`.
Authority: PR #33 Control Tower comment 6208799210 and Owner WORK C continuation.
Candidate: `work/leader-uat010-minimal-rc-20261016`.

## Decision and scope

**Partial Leader location draft candidate only. Existing UAT release HOLD.** No HTML Demo, separate site, UAT deployment, SQL read/write, migration, restore, flag change or merge is performed by this work.

Existing `/leader/locations` uses a new eight-field form calling existing `/api/v1/managed-locations` POST/PUT. Team, Name, City, District, Address, PlusCode, TaxId and MasterNote map to existing Protected contracts. Customer is fixed; Pending/inactive is enforced. Leaders can modify other creators' Customer drafts only within their current authorized organization/team. No ownership expansion outside that scope. Temporary, merged, active and non-Pending records cannot be updated in this path. Missing RowVersion is rejected; stale RowVersion returns HTTP 409.

Selectively ported R9: leader draft write policy, dated direct/delegated appointment policy, live Leader scope queries and eight-field UX contract. Live HR/account/identity/organization checks are implemented using existing Protected entities/access service, without porting the Option C engine. Each Leader mutation wraps live checks, draft write, duplicate flag refresh and audit in one serializable SQL transaction. SQL/write/unknown-commit failures require refresh; commit acknowledgement failure is not blindly replayed. Admin legacy behavior remains outside the narrow Leader path.

### Deliberate safety restrictions / regression review

The replaced Leader page no longer exposes old Excel confirm or geocode/publication controls. Server-side legacy `ConfirmImportAsync`, master `UpdateMaintenanceAsync`, geocoding job enqueue and direct geocode now require Admin. This also prevents Visitor use of the legacy master PUT; team note/history endpoints are unchanged. These restrictions are necessary to prevent bypassing the draft-only wave. Review the resulting loss of legacy Leader Excel/geocode actions explicitly before release; do not describe them as supported by this RC. Admin published mutation/merge behavior is not upgraded to canonical review in this wave.

## 010 compatibility — precise evidence boundary

- Database migration files, Domain entities, AppDbContext, production appsettings, deployed frontend config and existing API/Pages workflows are unchanged from the exact Protected base. CI verifies this byte-level diff.
- New queries/writes use only Protected-model entities: Users, Roles, UserRoles, UserRoleAssignments, UserTeamScopes, UserTeamAssignments, UserIdentityProfiles, Employments, EmploymentStatusPeriods, TeamLeaderAssignments, TeamLeaderDelegations, Teams, Locations, LocationApprovalHistory and AuditLogs. Duplicate refresh also reads existing Locations and LocationApprovalHistory. No ChangeRequests/Stage/PublishedVersion SQL is invoked.
- Disposable SQL fixture uses **EnsureCreated from the unchanged Protected EF model**, not copied business/personnel data, not live UAT, and not a reconstructed 001–010 migration chain. It explicitly asserts canonical tables are absent. This proves protected-model runtime compatibility only.
- Real UAT inventory run 38845007030 failed. A 1.8.0-010 marker is not metadata parity. Actual table/column/type/nullability/length/default/FK/index/trigger parity and application principal SELECT/INSERT/UPDATE permissions remain **HOLD** pending separately authorized WORK B evidence. No inference from EF model test success can close that gate.
- Do not install this draft-only RC on a future 011–020 canonical database: canonical reservation/publication guards are deliberately excluded. A later release must supersede these legacy draft routes atomically with the canonical engine.

## Isolated test evidence

Own nondeploy workflow `leader-uat010-rc-nondeploy.yml` runs .NET 8, full backend tests, frontend tests/build, real in-process HTTP/SQL negative checks, and actual Chromium → candidate React → running candidate API → disposable SQL, followed by an independent SQL persistence/audit probe. Browser tests do not mock API routes. Synthetic fixtures and token/connection files stay in `/tmp`; they are not uploaded. Runner container disposal removes the retained browser fixture DB.

HTTP/SQL scenarios include eight-field create, cross-creator draft edit, missing/stale RowVersion, published write refusal, cross-team/organization, active create, Visitor denial, stale-token HR leave/role/team scope/membership/appointment/team-active revocation, and legacy master PUT denial. Browser success verifies all eight saved fields plus Pending/inactive and audit in SQL. This does not certify production concurrency, shared UAT triggers, legacy Admin merge safety or full Scheme A acceptance.

## Feature / migration dependency matrix

| Capability | This RC | Future canonical prerequisite chain | Required candidate flags (all OFF / absent here) |
|---|---|---|---|
| Customer Pending/inactive eight-field create | Included, legacy draft only | None beyond verified actual Protected 010 contract | None added; deployment of reviewed RC is activation boundary |
| Authorized cross-creator draft update | Included | Same | None added |
| Published LowRisk MasterNote direct apply | Excluded / HOLD | 011 request, 012 atomic ledger, 014 stage/version, 015 evidence, 016 leader, 017 create/lowrisk contract, 018 direct-lowrisk | Candidate/Approval backend configuration + DIRECT_LOWRISK UI |
| Published HighRisk request and Admin apply | Excluded / HOLD | 011 → 012 → 014 → 015 → 016 (017 for Create) | Backend Candidate/Approval + APPROVAL UI |
| Independent Leader review / delegation | Live draft authorization only; canonical review excluded | Through 016; live role/HR/scope still required | LEADER_REVIEW UI + canonical backend configuration |
| Excel Create / Update canonical review | Excluded / HOLD | Through 020; 017 create contract and 019 merge are chain prerequisites | EXCEL_CREATE / EXCEL_UPDATE UI + canonical backend configuration |
| Published merge | Excluded / HOLD | Through 019; independent canonical approval/history preservation | Backend canonical merge gates; no enabling in this RC |

013 is reserved; do not invent it. The R9 015–020 scripts include disposable-only guards and are NOT ready-to-run UAT migrations. Separate DBA promotion, schema reconciliation and Owner authorization are required. The RC does not introduce or globally enable Option C flags.

## Same existing UAT deployment proposal (NOT AUTHORIZED)

Minimal API/Frontend workflow diff is supplied as `deployment-workflows.patch` for review only. It is not applied to active workflows. It removes automatic deployment from the proposed release variant, requires exact approved 40-character source SHA and a separately approved rollback operation, pins checkout and stamps source artifacts, and adds protected UAT environment review. Existing target resource/Pages project and configuration must be preserved, never guessed. Proposed dispatch remains blocked by `release-gates.json` whose defaults are HOLD. Do not bypass environment branch policy; explicitly review candidate-ref allowance before dispatch.

Before any dispatch, require all of:
1. Control Tower review of candidate diff, Exact-SHA CI and this partial scope.
2. Successful independently reviewed actual UAT schema parity plus application-principal permission evidence, obtained only through separately authorized read-only diagnostics. Existing failed inventory is insufficient.
3. Both current API and Pages **actual deployed** artifact IDs/run IDs, source SHAs, SHA-256, existing `config.js` and App Service setting fingerprint captured/verified read-only; neither original `d691fba...` nor current Protected `7f0cf0c...` establishes deployed source; actual provenance must be captured. No credentials in artifacts/review text.
4. Approved backup/export evidence for UAT. Capture schema-only and relevant synthetic test-row/audit before/after evidence under a separately authorized procedure; do not export actual personnel/business content into this branch/CI. No backup/export performed by this PR.
5. Reviewed same-host API/Frontend workflow patch and participant acceptance script. Candidate artifacts require PDF font packaging consistent with existing pipeline before release; current CI package does not claim a fully font-complete deploy bundle.
6. One consolidated Owner approval naming both candidate artifacts, exact source SHA, unchanged UAT target identities, operations/order, test data cleanup, expiry and rollback artifacts. No existing SQL/dispatch approval is reused.

### Release order / rollback

After approval only: preserve verified current artifacts/config and backup evidence → pause participant writes per agreed window → API exact-SHA deploy → verify provenance and health → Frontend matching exact-SHA deploy → Leader canary with expressly approved synthetic UAT data and real browser flow → observe scope/rowversion/published-denial checks. Stop if any gate fails; do not silently advance Frontend after API failure.

This wave changes no schema. Rollback restores the **captured actual prior API and Frontend artifacts and configuration** as a pair, verifies hashes/source and health, and checks current draft/audit rows remain intact. Do not use DB restore or delete audit as rollback. Source rebuild from Protected is only a reproducibility fallback, never proof it matches the prior deployed artifact. If rollback artifacts cannot be verified, release stays HOLD. Restoring the old API also restores its old security behavior; keep affected Leader writes paused until Control Tower evaluates the incident.

## Acceptance classification

Engineering candidate/isolated validation can PASS. Actual UAT compatibility, release and Business acceptance remain HOLD. Even after this narrow wave is authorized and verified on existing UAT, label it **partial Leader location UAT**, never complete Leader/Scheme A acceptance.


## Post-PR36 reconciliation

PR #37 merges exact Protected `7f0cf0cb55467916fb0f57239301a8787fd5cb69` as a second parent, preserving its diagnostic SQL, safe classifier, tests and immutable workflow pins byte for byte. No API/domain/EF/security code changed in the Protected delta. CI now compares unchanged schema/config/deployment files against this current base and reruns the same real HTTP/SQL and Chromium persistence checks. Historical d691/af8 CI results are historical only; use the reconciled source SHA and its new run.

`reference-prerequisites.json` in the candidate artifact inventories/hash-pins the exact 1600/1700 prerequisites and Protected 1800_001–010 Up/Verify files. `run-leader-rc-protected-reference.ps1` is the disposable-only reference builder. Its prerequisites are a separately approved, immutable **schema-only 1.5 baseline**, its SHA-256 and provenance reference. It rejects absent inputs, nonlocal targets, EF baselines and business INSERTs. It executes all unmodified protected migration/verify files in order and emits normalized reference metadata using a local-target-only derivative of the preserved inventory query. Derived and original query hashes are recorded distinctly. It never overwrites the protected UAT query or connects to UAT.

The repository/history contains only `database/015_schema_v1_5_verify.sql`, not a 1.5 CREATE schema source. Thus genuine chain execution/reference compatibility remains HOLD, even if candidate runtime CI succeeds. No SchemaVersions row is fabricated to turn an EF fixture into a reference. Future comparison requires both the genuine generated reference and an independently authorized normalized UAT inventory, plus application-principal permissions. No read-only UAT dispatch is executed or retried by WORK C.

The CI's candidate manifest explicitly marks deployed API/Pages source UNVERIFIED; Protected HEAD must never be substituted. Read-only deployment provenance collection is prepared in `PREDEPLOY-EVIDENCE.md`; execution requires a separately approved plan for actual Azure/Pages deployment inspection, verified prior artifact hashes and rollback eligibility. No live target inspection is implied by this nondeploy PR.

## Read-only preflight continuation — material release blocker

Evidence now distinguishes live Pages source stamp 0899f3996618c4b2bd02ecd659c76fdd07cc91ed from historical API deployment source db235938b8b13ca8a65ab3b525de1df10d911645; neither equals Protected HEAD. Public API health has no source SHA; /source-sha.txt returned 404. API active binary remains unverified. Original Pages artifact retrieval returned 404; API retained artifacts are test reports only, so rollback readiness remains HOLD.

Comparing those source records to this RC identifies a P0 existing-Visitor regression: /locations/manage nav/route, Visitor managed-location API grants, visitor-teams endpoint and creator DTO field are absent in the RC. No runtime feature is altered in this continuation; the single WORK A/Control Tower plan must resolve this before release. Source model/DDL and SaveManagedLocationRequest match the historical API source, but this is not actual catalog parity or full existing-UAT compatibility. The new source-comparison CI step records HOLD rather than declaring business acceptance.

The exact-SHA preflight manifest now binds candidate source/ref, package stamps/hashes, immutable evidence snapshot/hash and unreleased gate/hash. It denies branch/SHA/stamp mismatch, symlink artifacts and unsupported evidence promotion; it cannot emit GO. The Owner decision sheet and verified recovery procedure report unresolved baseline, app-principal, active binary and rollback gaps. No UAT SQL, credentials, deployments or business row copies were used.
