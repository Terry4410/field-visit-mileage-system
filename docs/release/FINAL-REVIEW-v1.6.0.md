# v1.6.0 FINAL Acceptance / Final Review

Baseline: `4bc7ab6706485d490ce7be0a4fb86d2c92e8381a`

## Acceptance checklist

| Area | Acceptance | RC Status |
|---|---|---|
| Multi-Team | Leader query/review/mileage/location/project scope uses active TeamScopes; Admin can maintain scopes | STATIC PASS |
| Snapshot | Approved query/export uses latest Snapshot; migration backfills old Approved rows | STATIC PASS |
| Correction | Visitor → Leader → Admin when financial; original Snapshot retained; result V2+ | STATIC PASS |
| Stop rule | >=1 may submit; <2 mileage/rate/subsidy N/A; >=2 requires mileage | STATIC PASS |
| Time overlap | Warning + explicit confirmation; backend rechecks | BASELINE + RC PASS |
| Visit stop UX | Project optional, existing/temp location, VisitType optional for every stop, purpose optional | STATIC PASS |
| LocationCode | Backfill + unique org/code index + auto create/import | STATIC PASS |
| Temporary lifecycle | Reuse pending temporary location; abandoned unused draft/edit locations | STATIC PASS |
| Rates | EffectiveFrom-driven auto range; no duplicate active same-day start | STATIC PASS |
| Query | One server query definition: date/team/visitor/location/project/visit type/status + pagination | STATIC PASS |
| Excel | Server-side, all matching rows, 3 sheets, same query | STATIC PASS |
| PDF | Server-side formal A4 landscape grid, same query, totals, audit | STATIC PASS; RUNTIME FONT VERIFY REQUIRED |
| Import | Template → preview → validation → error Excel → confirm/result | STATIC PASS |
| Background jobs | Mileage modes AllPending/DateRange/Selected; Geocode modes AllPending/DateRange/Selected | STATIC PASS |
| Authorization | Backend role/org/team/ownership/reference checks; active role enforced | STATIC PASS |
| Mobile | 16px controls, >=44px touch, >=48px key actions, >=52px bottom nav, responsive/sticky modal | STATIC PASS |
| Time zone | Business date/time uses Asia/Taipei helper | STATIC PASS |
| Health | live/ready; DB readiness returns 503 on failure | STATIC PASS |
| CI | Front test/build and Backend build/test before deployment | STATIC PASS |
| Migration | Up/Verify/Rollback supplied | STATIC PASS; AZURE EXECUTION REQUIRED |

## Static review performed before packaging

- TypeScript parser/transpile syntax sweep on FINAL payload: PASS.
- C# lexical delimiter/string/comment scan: PASS.
- Python apply script `py_compile`: PASS.
- `.csproj` XML parse: PASS.
- Migration structure/content gate: PASS.
- Forbidden RC markers scan (`TODO`, `FIXME`, `NotImplementedException`, hard-coded 40km, browser-side report generator): PASS.

## Must be verified in the user's real environment

- `npm install && npm test && npm run build`.
- `dotnet restore/build/test`.
- Azure SQL `Up.sql` then `Verify.sql`.
- Azure App Service runtime / GitHub Actions.
- Browser desktop/mobile UAT.
- PDF Traditional-Chinese glyph rendering on Azure host.

Only after these environment gates pass should the status advance beyond Release Candidate.
