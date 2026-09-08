#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

declare -A frozen_hashes=(
  ["database/migrations/1800_001_organization_center_team_lifecycle/Up.sql"]="7a2f5409b1ed5aaa52686c8d4be8346dd60c25f00b42a52489314ad63b00214c"
  ["database/migrations/1800_001_organization_center_team_lifecycle/Verify.sql"]="4dfa7f571c1946e6a034bcb9acb9a3e7504cfd050bcfdfbb6426a091a12e1026"
  ["database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_001.sql"]="876534db6aeac85ea555da5468b8bf66bb9fb914008043a33020e0ef7103f114"
  ["database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_001.sql"]="654cc22091747621ca232c5ac2c1d62c99d9d7ac06a64d4ff1aecddbc4ff1c88"
  ["database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_001.sql"]="0478c2ea507f44ffc52df3ebcf6634ebff1b46aa7c38f518e8a89529236c5010"
  [".github/workflows/azure-sql-uat-migration-1800-001.yml"]="434e6042ca53234cc683835c4aa72d2c8ae70c48731f6f4bd18ca0a375c15bff"
)

for path in "${!frozen_hashes[@]}"; do
  [[ -f "${path}" ]] || { echo "Missing preserved migration artifact: ${path}" >&2; exit 1; }
  actual="$(sha256sum "${path}" | awk '{print $1}')"
  [[ "${actual}" == "${frozen_hashes[${path}]}" ]] || {
    echo "Protected 1800_001 artifact changed: ${path}" >&2
    exit 1
  }
done

for required in \
  docs/ai/PROJECT-STATE.md \
  docs/ai/ARCHITECTURE-DECISIONS.md \
  docs/ai/UAT-BACKLOG.md \
  docs/ai/DEFINITION-OF-DONE.md \
  database/baseline/v1.8.0/schema.sql \
  database/baseline/v1.8.0/manifest.json \
  database/baseline/v1.8.0/Verify.sql \
  database/reset/v1.8.0/plan.json \
  test-data/automated/README.md \
  test-data/templates/README.md \
  test-data/uat/README.md; do
  [[ -f "${required}" ]] || { echo "Missing Fast-Track artifact: ${required}" >&2; exit 1; }
done

grep -Fq 'UAT FAST-TRACK DEVELOPMENT' docs/ai/PROJECT-STATE.md
grep -Fiq 'Protected Baseline' docs/ai/ARCHITECTURE-DECISIONS.md
grep -Fq 'BUG -> FIX -> PERMANENT REGRESSION TEST' docs/ai/DEFINITION-OF-DONE.md
grep -Fq 'THROW 55000' database/baseline/v1.8.0/schema.sql
grep -Fq '"executionEnabled": false' database/reset/v1.8.0/plan.json

if find test-data/uat -type f ! -name README.md ! -name .gitignore -print -quit | grep -q .; then
  echo "Real UAT data directory contains a committable payload." >&2
  exit 1
fi

if grep -ERni --include='*.csv' -E '@(gmail|outlook|hotmail|yahoo|icloud|live)\.' \
  test-data/automated test-data/templates; then
  echo "Public email domain found in repository test data." >&2
  exit 1
fi

bash scripts/validate-v180-migration-readiness.sh >/dev/null
bash scripts/plan-uat-fasttrack-reset.sh >/dev/null

echo "Fast-Track strategy static validation passed."
