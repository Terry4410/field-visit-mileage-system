#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

bash scripts/validate-fasttrack-strategy.sh
npm --prefix frontend test

dotnet_restore_args=()
if [[ "${SKIP_DOTNET_RESTORE:-0}" == "1" ]]; then
  dotnet_restore_args+=(--no-restore)
fi

dotnet test backend/tests/FieldVisit.Application.Tests/FieldVisit.Application.Tests.csproj \
  --configuration Release \
  "${dotnet_restore_args[@]}"
