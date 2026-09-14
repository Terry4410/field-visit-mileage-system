#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet test backend/tests/FieldVisit.Application.Tests/FieldVisit.Application.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~V180GoogleProviderAdapterTests"

npm --prefix frontend run test -- src/fc-google-map-ux.test.ts

rg -q 'translate="no"' frontend/src/components/GoogleRouteSuggestionPanel.tsx
rg -q 'libraries=geometry' frontend/src/fc-google-map-ux.ts
if rg -n 'localStorage|sessionStorage' \
  frontend/src/fc-google-map-ux.ts \
  frontend/src/components/GoogleRouteMap.tsx \
  frontend/src/components/GoogleRouteSuggestionPanel.tsx; then
  echo "F-C provider output must remain component state only." >&2
  exit 1
fi

echo "FC_GOOGLE_PROVIDER_ADAPTERS=PASS"
echo "FC_GOOGLE_MAP_UX=PASS"
