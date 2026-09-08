#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

manifest="database/baseline/v1.8.0/manifest.json"
schema="database/baseline/v1.8.0/schema.sql"
verify="database/baseline/v1.8.0/Verify.sql"

for required in "${manifest}" "${schema}" "${verify}"; do
  [[ -f "${required}" ]] || { echo "Missing baseline package file: ${required}" >&2; exit 1; }
done

grep -Fq '"containsBusinessData": false' "${manifest}"
grep -Fq '"containsSecurityPrincipals": false' "${manifest}"

status="$(sed -n 's/^[[:space:]]*"status":[[:space:]]*"\([^"]*\)".*/\1/p' "${manifest}")"
schema_sha="$(sed -n 's/^[[:space:]]*"schemaSha256":[[:space:]]*"\([0-9a-fA-F]*\)".*/\1/p' "${manifest}")"

case "${status}" in
  fail-closed-pending-isolated-generation)
    grep -Fq '"schemaSha256": null' "${manifest}"
    grep -Fq 'THROW 55000' "${schema}"
    echo "v1.8.0 baseline package: PENDING / FAIL-CLOSED"
    ;;
  verified)
    [[ "${schema_sha}" =~ ^[0-9a-f]{64}$ ]] || {
      echo "Verified baseline manifest has no valid lowercase SHA-256." >&2
      exit 1
    }
    if grep -Fq 'THROW 55000' "${schema}"; then
      echo "Verified baseline still contains the fail-closed placeholder." >&2
      exit 1
    fi
    actual_sha="$(sha256sum "${schema}" | awk '{print $1}')"
    [[ "${actual_sha}" == "${schema_sha}" ]] || {
      echo "Verified baseline SHA-256 does not match manifest." >&2
      exit 1
    }
    if grep -Eiq '(^|[[:space:]])(CREATE[[:space:]]+USER|ALTER[[:space:]]+ROLE|GRANT|DENY|REVOKE)([[:space:]]|$)' "${schema}"; then
      echo "Baseline contains a forbidden security-principal or permission statement." >&2
      exit 1
    fi
    if grep -Eiq '(^|[[:space:]])(UPDATE|DELETE|MERGE)([[:space:]]|$)' "${schema}"; then
      echo "Baseline contains forbidden mutable business-data statements." >&2
      exit 1
    fi
    echo "v1.8.0 baseline package: VERIFIED (${actual_sha})"
    ;;
  *)
    echo "Unsupported baseline manifest status: ${status:-<empty>}" >&2
    exit 1
    ;;
esac

