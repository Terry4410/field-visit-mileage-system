#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

up_script="database/migrations/1800_001_organization_center_team_lifecycle/Up.sql"
verify_script="database/migrations/1800_001_organization_center_team_lifecycle/Verify.sql"
draft="docs/workflows/azure-sql-uat-migration-1800-001.draft.yml"
workflow=".github/workflows/azure-sql-uat-migration-1800-001.yml"
grant_script="database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_001.sql"
permission_verify_script="database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_001.sql"
revoke_script="database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_001.sql"

expected_up_sha="7a2f5409b1ed5aaa52686c8d4be8346dd60c25f00b42a52489314ad63b00214c"
expected_verify_sha="4dfa7f571c1946e6a034bcb9acb9a3e7504cfd050bcfdfbb6426a091a12e1026"
checkout_sha="11d5960a326750d5838078e36cf38b85af677262"
azure_login_sha="7184910d9eb2b1c5e48f7073824a90609bb9b6d6"

for required_file in \
  "${up_script}" \
  "${verify_script}" \
  "${draft}" \
  "${workflow}" \
  "${grant_script}" \
  "${permission_verify_script}" \
  "${revoke_script}" \
  "docs/release/POST-UAT-v1.8.0-1800-001-PERMISSION-REVIEW.md" \
  "docs/release/POST-UAT-v1.8.0-1800-001-RECOVERY-GATE.md"; do
  [[ -f "${required_file}" ]] || { echo "Missing required file: ${required_file}" >&2; exit 1; }
done

actual_up_sha="$(sha256sum "${up_script}" | awk '{print $1}')"
actual_verify_sha="$(sha256sum "${verify_script}" | awk '{print $1}')"
[[ "${actual_up_sha}" == "${expected_up_sha}" ]] || { echo "Unexpected Up.sql SHA-256" >&2; exit 1; }
[[ "${actual_verify_sha}" == "${expected_verify_sha}" ]] || { echo "Unexpected Verify.sql SHA-256" >&2; exit 1; }

diff -u <(tail -n +2 "${draft}") <(tail -n +2 "${workflow}")
grep -Fq -- 'name: Azure SQL UAT migration 1800_001' "${workflow}"
grep -Fq -- 'workflow_dispatch:' "${workflow}"
if grep -Eq '^  (push|pull_request|schedule|workflow_call):' "${workflow}"; then
  echo "Executable migration workflow has a forbidden trigger." >&2
  exit 1
fi

grep -Fq -- 'refs/heads/post-uat/v1.8.0' "${workflow}"
grep -Fq -- 'environment: uat-migration' "${workflow}"
grep -Fq -- 'APPROVED_MIGRATION_COMMIT_SHA' "${workflow}"
grep -Fq -- "actions/checkout@${checkout_sha}" "${workflow}"
grep -Fq -- "azure/login@${azure_login_sha}" "${workflow}"
grep -Fq -- 'persist-credentials: false' "${workflow}"
grep -Fq -- "EXPECTED_UP_SHA256: ${expected_up_sha}" "${workflow}"
grep -Fq -- "EXPECTED_VERIFY_SHA256: ${expected_verify_sha}" "${workflow}"
grep -Fq -- 'client-id: ${{ vars.AZURE_MIGRATION_CLIENT_ID }}' "${workflow}"
grep -Fq -- 'USER_NAME() <> N'"'"'gh-fieldvisit-uat-migrate'"'"'' "${workflow}"
grep -Fq -- '1.7.0-008' "${workflow}"
grep -Fq -- 'partial 1.8.0-001 columns exist' "${workflow}"
grep -Fq -- 'FieldVisit.SchemaMigration' "${up_script}"
grep -Fq -- 'STOP_FOR_REVIEW' "${workflow}"

if grep -Eq 'uses:[[:space:]]+(actions/checkout|azure/login)@v[0-9]' "${workflow}"; then
  echo "Executable migration workflow contains a floating action tag." >&2
  exit 1
fi

install_line="$(grep -nF -- 'Install pinned SQL Server PowerShell module before Azure login' "${workflow}" | cut -d: -f1)"
login_line="$(grep -nF -- 'Sign in to Azure with migration OIDC identity' "${workflow}" | cut -d: -f1)"
hash_line="$(grep -nF -- 'Validate approved commit and SQL SHA-256 locks' "${workflow}" | cut -d: -f1)"
[[ "${hash_line}" -lt "${install_line}" && "${install_line}" -lt "${login_line}" ]] || {
  echo "Hash/dependency/login ordering is unsafe." >&2
  exit 1
}

mapfile -t sql_refs < <(grep -Eo 'database/migrations/[A-Za-z0-9_./-]+\.sql' "${workflow}" | sort -u)
[[ "${#sql_refs[@]}" -eq 2 ]] || { echo "Workflow must reference exactly two SQL files." >&2; exit 1; }
[[ "${sql_refs[0]}" == "${up_script}" && "${sql_refs[1]}" == "${verify_script}" ]] || {
  echo "Workflow SQL references are not the fixed 1800_001 Up/Verify pair." >&2
  exit 1
}

for forbidden in 1800_002 1800_003 1800_004 1800_005 1800_006 1800_007 sqlcmd; do
  if grep -Fq -- "${forbidden}" "${workflow}"; then
    echo "Forbidden executable workflow content: ${forbidden}" >&2
    exit 1
  fi
done

if grep -Eiq '(seed|data[ -]?import|firewall|deploy(ment)?|database copy|restore)' "${workflow}"; then
  echo "Executable workflow contains a forbidden operation category." >&2
  exit 1
fi

if grep -Fq -- 'database/migrations/security/uat/' "${workflow}"; then
  echo "Executable workflow must not run Grant/Verify/Revoke security scripts." >&2
  exit 1
fi

grep -Fq -- 'ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]' "${grant_script}"
grep -Fq -- 'GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]' "${grant_script}"
grep -Fq -- 'GRANT UPDATE ON OBJECT::dbo.Organizations TO [gh-fieldvisit-uat-migrate]' "${grant_script}"
grep -Fq -- 'ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]' "${revoke_script}"

if grep -RFq -- '[gh-fieldvisit-uat]' database/migrations/security/uat; then
  echo "Security scripts must not modify gh-fieldvisit-uat." >&2
  exit 1
fi
if grep -ERq 'ALTER ROLE[[:space:]]+(db_owner|db_securityadmin|db_datawriter)[[:space:]]+ADD MEMBER' database/migrations/security/uat; then
  echo "Security scripts contain a forbidden broad-role grant." >&2
  exit 1
fi

echo "Migration readiness static validation passed."
echo "1800_001 Up.sql SHA-256: ${actual_up_sha}"
echo "1800_001 Verify.sql SHA-256: ${actual_verify_sha}"
