#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

bash scripts/test-fast-regression.sh
bash scripts/test-da0b-snapshot-concurrency.sh
bash scripts/test-da1b-location-governance-integration.sh
bash scripts/test-db-work1-concurrency.sh
bash scripts/test-db-work2-correction-atomicity.sh
npm --prefix frontend run build
dotnet build backend/src/FieldVisit.Api/FieldVisit.Api.csproj --configuration Release --no-restore

if [[ "${RUN_ISOLATED_BROWSER_REGRESSION:-0}" == "1" ]]; then
  UAT_BASE_URL=http://127.0.0.1:4173/ npm --prefix uat run test:protected
else
  echo "Isolated browser regression not run. Set RUN_ISOLATED_BROWSER_REGRESSION=1 after Chromium is available."
fi

echo "Live role E2E and write-flow UAT are intentionally excluded from this local command and remain environment-gated."
