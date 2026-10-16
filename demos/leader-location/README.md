# WORK C — Leader location demo / feasibility checkpoint

Base: `95b1987c7f9f92bf589f2f9aecb0632d5d880325` (R9).
Branch: `demo/leader-location-20261016`.
Planning authority: PR #33 comment 6207697449 and Owner WORK C instruction.

## Current decision

Deliver offline interactive Mock UX fallback first. Open `index.html` in a current browser; no server, account, installation, API, SQL, credentials or network is required. For a meeting, open the file on the presenter's computer and screen-share. This is a hand-built workflow illustration, not the R9 React application. Refresh/reset discards all artificial data. Never enter real employee or customer data.

The current workspace lacks dotnet, Docker and SQL Server. Existing Azure and GitHub Pages workflows deploy shared UAT and must not be used. No independent cloud host or URL was verified. No cloud resources, tunnels, public URLs or shared-UAT settings have been created/changed.

## Evidence boundaries

| Item | Evidence / state |
|---|---|
| R9 frontend tests | Locally executed: 26 files / 115 tests PASS |
| R9 frontend production build | Locally executed PASS; default flags remain false |
| Eight fields | Matched ScopedLocationFields: Team, Name, City, District, Address, PlusCode, TaxId, MasterNote |
| Interactive fallback | Mock only; smoke script supplied; browser execution reported separately by CI |
| Browser → real API → SQL | HOLD; no such execution in this workspace |
| True Excel parsing / canonical submission | HOLD; fixed artificial rows only in fallback |
| Actual HR / role revocation, scope enforcement, concurrency | HOLD; UX rejection simulation only |
| Actual merge / versions / Trip exclusions | HOLD; fallback never performs a successful merge |
| Existing R9 real HTTP/SQL CI | Historical baseline evidence only, not this demo's business acceptance |
| Shared UAT / full Scheme A | HOLD / NO-GO unchanged |

## 20–30 minute walkthrough

1. Keep the amber MOCK banner visible. Explain that all actions are browser-memory simulations.
2. Leader A creates a Customer using the eight fields. Show Pending/inactive and Submitted, no Trip eligibility.
3. Edit the artificial Visitor-created draft in authorized Team A.
4. Save published LowRisk MasterNote. Create a separate HighRisk address-change proposal.
5. Attempt self-review (denied); switch to independent Leader B for first review. Switch to Admin, attempt approval before evidence (denied), simulate evidence, then approval. Explain no real Maps/DB transaction occurred.
6. Preview fixed Excel Create / Update rows and submit to simulated review. Mixed actions are rejected. This does not parse an .xlsx.
7. Demonstrate cross-scope, revoked role, legacy/direct PUT, and published UseExisting denial. Real successful merge stays HOLD.
8. Show the fixed historical Snapshot and simulated Trip list. Explain real historical invariants remain unverified here.
9. Collect Business User comments as UX feedback only; never record full UAT PASS.

## Bounded deployment decision (not executed)

Preferred live option: a separately managed temporary host capable of running .NET 8 API, separate frontend, and disposable SQL Server 2022. Required before approval: named host/subscription owner, validated runtime/memory, region, access restrictions, HTTPS domain/certificate, synthetic seed, exact API/frontend SHA, backend flag configuration, spending ceiling, expiry and deletion owner. None was available/verified here; cost and URL are deliberately not guessed.

Once those concrete facts are available, submit one consolidated approval containing: exact resource IDs/names, total spending cap, allowlisted participants, exposure window, credential delivery outside chat, disposable database isolation, no UAT peering/access, preview-only settings, smoke/rollback/cleanup commands. Do not execute shared API/Pages workflows or request UAT credentials as a substitute.

No additional approval is needed for this offline fallback. A live cloud deployment remains HOLD pending that concrete resource plan and Owner approval.

## Nondeploy CI

`work-c-leader-demo-nondeploy.yml` is copied from the R9 nondeploy workflow with branch guards scoped only to this Demo branch, plus the Mock browser smoke. It uses disposable localhost SQL and existing artificial tests, no Azure login or environment secrets. It does not create a persistent operational demo environment. Even a successful run does not satisfy Browser→real API→SQL or full Business UAT.

`node demos/leader-location/smoke.cjs` requires Playwright (from `uat/npm ci`) and Chromium. `DEMO_PLAYWRIGHT_MODULE` can point to an existing runtime module. The test checks simulated interactions and asserts zero external HTTP requests; it is not a backend authorization test.
