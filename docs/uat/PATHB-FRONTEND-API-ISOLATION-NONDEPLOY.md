# Path B frontend API isolation — NONDEPLOY candidate

Scope: isolated frontend safety guard and a Path B-specific runtime config candidate only. No Azure action, SQL, deployment, merge, secrets, or modification of existing legacy UAT config.

Evidence: current GitHub `frontend/public/config.js` targets the legacy UAT API; owner observed `config.js` response `{}` on Path B SWA, but the actual API request target was not observed. Treat deployed API target as UNKNOWN, not as proven legacy.

The Path B hostname guard in `frontend/src/api.ts` rejects missing, localhost, legacy UAT, unexpected HTTP origin/port/path/query, and any non-exact API URL. Local development keeps its existing localhost fallback.

`frontend/config.pathb-uat.js` is a **candidate**, not an automatically activated file. Do not overwrite shared `frontend/public/config.js` because that is used by other environments. A separately approved Path B-only deployment must place the reviewed candidate at the deployed Path B site's `config.js` path, with a pinned artifact SHA, and verify the deployed bytes and actual request target. Confirm API CORS, accessibility, correct backend environment and schema before allowing UAT login. Frontend guard alone does not prove those conditions.

Run existing frontend tests and build in CI. No automatic deployment. Do not attempt login against UNKNOWN SQL schema.
