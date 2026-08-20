# Automated UAT Smoke

This folder contains non-destructive browser-level UAT checks for the deployed UAT environment.

## Phase 1 scope

- API `/health` returns healthy status.
- Demo login works for visitor, leader, admin, and supervisor roles.
- Every role can open its main navigation pages.
- No browser page error occurs during navigation.
- No HTTP 5xx response occurs during navigation.
- Visitor mobile shell renders the mobile navigation.

The smoke suite intentionally does not create, submit, approve, delete, or permanently change UAT business data.

## Run locally

```bash
cd uat
npm install
npx playwright install chromium
npm run test:smoke
```

Optional environment variables:

- `UAT_BASE_URL`
- `UAT_API_HEALTH_URL`
- `UAT_DEMO_PASSWORD`

## GitHub Actions

Run the `UAT Browser Smoke` workflow. On failure, the workflow uploads the Playwright HTML report, screenshots, and trace files for diagnosis.

## Next phase

After Phase 1 is stable, add isolated write-flow UAT for `Create -> Submit -> Mileage -> Approve -> Snapshot -> Unified Query` with uniquely prefixed test data and guaranteed cleanup.
