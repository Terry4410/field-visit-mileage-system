# Path B Static Web App — controlled release gate (NOT AUTHORIZED TO DEPLOY)

## Confirmed evidence (2026-11-16)
- Azure resource: `rg-fieldvisit-uat/swa-fieldvisit-pathb-uat`; source: `SwaCli`; production URL: `https://ashy-water-021992f00.2.azurestaticapps.net` (Owner Azure Portal screenshot).
- GitHub PR #90 head at validation: `13170abead888598e5518c630b81ad98d673e388`.
- Successful GitHub Actions run: `43799294603`; uploaded artifact `pathb-frontend-static-nondeploy`, artifact ID `14195477350`.
- GitHub-reported artifact ZIP digest: `sha256:9fb1a014872648a411dce635c6f1444f8c5a3be5a3a389682ad00bb5a3441a9a`.
- Owner reports creating repository secret `AZURE_STATIC_WEB_APPS_API_TOKEN_PATHB_UAT`. Secret value and usability **not verified**; do not expose.
- API destination in candidate: `https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1`.
- API live connectivity/CORS and SQL schema: UNKNOWN; Business UAT 0/25.

## Pre-deployment gate — stop on any mismatch
1. Require separate explicit Owner authorization for **one** production-environment deployment to **only** `swa-fieldvisit-pathb-uat`; no permission is implied by this document or the presence of a secret.
2. Pin the **same** successful build run, artifact ID, SHA-256, and exact file tree. Download artifact with GitHub's authorized Actions API. Verify the downloaded ZIP SHA-256 against the GitHub-reported digest; if GitHub's digest semantics differ from the downloaded archive, HOLD until reconciled. Never silently replace the artifact with a new build.
3. Check artifact root has `index.html`, `config.js`, and expected Vite assets; `config.js` contains only the approved Path B API target; reject legacy UAT, localhost, and unexpected domains.
4. Confirm the intended GitHub workflow has access to the **named Path B secret** without printing it; confirm target site association by Azure-side deployment credential verification. Token existence is not proof of target binding.
5. Require a dedicated, manually invoked, single-use workflow with a pinned artifact, explicit Owner approval gate, concurrency control, and **no** automatic trigger. Keep workflow inactive until approved. Never use the existing Azure Web App publish profile.
6. Deploy **only static frontend assets** using the existing Static Web Apps deployment mechanism; no SQL/Token acquisition, API deployment, Azure resource creation, RBAC, reset, or PR merge.
7. Post-deployment: GET site `/config.js` and `/index.html`, verify expected configuration and assets, and check API request destination without submitting login or business data. Do not claim live API connectivity or schema readiness from frontend checks.
8. Stop and report HOLD on any error. No automatic retries or fallback to legacy site.

## Release disposition
FRONTEND_BUILD=PASS; ARTIFACT_UPLOAD=PASS; SWA_TARGET=PASS; SECRET_CREATED=OWNER_REPORTED; DEPLOYMENT_AUTHORIZATION=NO; DEPLOYMENT=NOT_RUN; API=UNKNOWN; SQL_SCHEMA=UNKNOWN; BUSINESS_UAT=NO_GO.

## Important
GitHub Actions workflows added only on a feature branch may not be manually dispatchable until available on the repository default branch. Do **not** merge merely to make a deploy workflow dispatchable. Choose a separately approved safe delivery mechanism after the release gate is approved.
