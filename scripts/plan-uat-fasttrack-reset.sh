#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

plan="database/reset/v1.8.0/plan.json"
manifest="database/baseline/v1.8.0/manifest.json"
schema="database/baseline/v1.8.0/schema.sql"
verify="database/baseline/v1.8.0/Verify.sql"

for required in "${plan}" "${manifest}" "${schema}" "${verify}"; do
  [[ -f "${required}" ]] || { echo "Missing reset prerequisite: ${required}" >&2; exit 1; }
done

grep -Fq '"executionEnabled": false' "${plan}"
grep -Fq '"requiresHumanGate": "HUMAN GATE A"' "${plan}"
grep -Fq '"status": "fail-closed-pending-isolated-generation"' "${manifest}"
grep -Fq 'THROW 55000' "${schema}"

echo "UAT reset plan validation: PASS (planning only)"
echo "Target: db-fieldvisit-uat"
echo "Baseline: fail closed; isolated generation and verification still required"
echo "Azure mutation: NOT STARTED"
echo "Required before execution: HUMAN GATE A"
