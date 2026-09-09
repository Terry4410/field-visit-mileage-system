#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

manifest=database/development/v1.8.0/manifest.json
workflow=.github/workflows/v180-development-schema-harness.yml
[[ -f "$manifest" && -f "$workflow" ]] || { echo 'Development harness files missing.' >&2; exit 1; }
grep -Fq '"status": "mutable-development-harness"' "$manifest"
grep -Fq '"finalBaseline": false' "$manifest"
grep -Fq '"frozen": false' "$manifest"
grep -Fq 'workflow_dispatch:' "$workflow"
grep -Fq 'database/development/v1.8.0/Verify.sql' "$workflow"
grep -Fq 'database/baseline/v1.8.0/Verify.sql' "$workflow"

mapfile -t dirs < <(find database/migrations -maxdepth 1 -type d -name '1800_0*' | sort)
[[ "${#dirs[@]}" -eq 7 ]] || { echo 'Expected exactly seven 1.8 migration directories.' >&2; exit 1; }
for i in {1..7}; do
  printf '%s\n' "${dirs[$((i-1))]}" | grep -Eq "/1800_00${i}_"
  [[ -f "${dirs[$((i-1))]}/Up.sql" && -f "${dirs[$((i-1))]}/Verify.sql" ]]
done

if grep -REIn --include='*.sql' -E '\b(GRANT|DENY|ALTER[[:space:]]+ROLE|CREATE[[:space:]]+USER|ALTER[[:space:]]+USER)\b' database/development/v1.8.0; then
  echo 'Development harness package must not provision security principals.' >&2
  exit 1
fi

echo 'Development schema harness static validation passed.'
