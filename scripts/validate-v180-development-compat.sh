#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

manifest=database/development/v1.8.0/compat/manifest.json
harness=.github/workflows/v180-development-schema-harness.yml
[[ -f "$manifest" && -f "$harness" ]] || { echo 'Development compatibility manifest or harness is missing.' >&2; exit 1; }

jq -e '
  .status == "development-only-migration-compatibility" and
  .productionUpgradePath == false and
  .azureExecutionAllowed == false and
  (.entries | length == 7) and
  ([.entries[].migration] == ["1800_001","1800_002","1800_003","1800_004","1800_005","1800_006","1800_007"])
' "$manifest" >/dev/null

session_options="$(jq -r '.sessionOptions' "$manifest")"
session_options_hash="$(jq -r '.sessionOptionsSha256' "$manifest")"
[[ -f "$session_options" && "$session_options_hash" != "TO_BE_FILLED" ]] || { echo 'Pinned SQL session-options file is missing or uninitialized.' >&2; exit 1; }
[[ "$(sha256sum "$session_options" | awk '{print $1}')" == "$session_options_hash" ]] || { echo 'SQL session-options file drifted; compatibility review required.' >&2; exit 1; }
for option in 'SET ANSI_NULLS ON;' 'SET ANSI_PADDING ON;' 'SET ANSI_WARNINGS ON;' 'SET ARITHABORT ON;' 'SET CONCAT_NULL_YIELDS_NULL ON;' 'SET QUOTED_IDENTIFIER ON;' 'SET NUMERIC_ROUNDABORT OFF;'; do
  grep -Fqx "$option" "$session_options" || { echo "Required SQL session option missing: $option" >&2; exit 1; }
done

entry_005_source="$(jq -r '.entries[] | select(.migration=="1800_005") | .source' "$manifest")"
entry_005_verify="$(jq -r '.entries[] | select(.migration=="1800_005") | .verify' "$manifest")"
entry_005_apply="$(jq -r '.entries[] | select(.migration=="1800_005") | .developmentApply' "$manifest")"
echo "DBW1_1800_005_SOURCE_SHA256=$(sha256sum "$entry_005_source" | awk '{print $1}')"
echo "DBW1_1800_005_VERIFY_SHA256=$(sha256sum "$entry_005_verify" | awk '{print $1}')"
echo "DBW1_1800_005_DEVELOPMENT_APPLY_SHA256=$(sha256sum "$entry_005_apply" | awk '{print $1}')"

while IFS=$'\t' read -r migration source source_hash verify verify_hash apply apply_hash; do
  for path in "$source" "$verify" "$apply"; do
    [[ -f "$path" ]] || { echo "$migration missing pinned file: $path" >&2; exit 1; }
  done
  actual_source="$(sha256sum "$source" | awk '{print $1}')"
  actual_verify="$(sha256sum "$verify" | awk '{print $1}')"
  actual_apply="$(sha256sum "$apply" | awk '{print $1}')"
  [[ "$actual_source" == "$source_hash" ]] || { echo "$migration source Up.sql drifted; compatibility review required." >&2; exit 1; }
  [[ "$actual_verify" == "$verify_hash" ]] || { echo "$migration original Verify.sql drifted; compatibility review required." >&2; exit 1; }
  [[ "$actual_apply" == "$apply_hash" ]] || { echo "$migration development apply file drifted; manifest review required." >&2; exit 1; }
done < <(jq -r '.entries[] | [.migration,.source,.sourceSha256,.verify,.verifySha256,.developmentApply,.developmentApplySha256] | @tsv' "$manifest")

mapfile -t adapters < <(find database/development/v1.8.0/compat -maxdepth 1 -name '1800_*.dev.sql' | sort)
[[ "${#adapters[@]}" -eq 5 ]] || { echo 'Expected five reviewed development adapters.' >&2; exit 1; }

if grep -REIn --include='*.sql' -E '(database\.windows\.net|db-fieldvisit-uat|sql-fieldvisit-jpe-uat|\bGRANT\b|\bDENY\b|ALTER[[:space:]]+ROLE|CREATE[[:space:]]+USER)' database/development/v1.8.0/compat; then
  echo 'Compatibility SQL contains an Azure target or security-provisioning statement.' >&2; exit 1
fi

mapfile -t workflow_refs < <(grep -RIl 'database/development/v1.8.0/compat' .github/workflows || true)
[[ "${#workflow_refs[@]}" -eq 1 && "${workflow_refs[0]}" == "$harness" ]] || { echo 'Development compatibility layer is referenced outside its disposable harness.' >&2; printf '%s\n' "${workflow_refs[@]}" >&2; exit 1; }

grep -Fq 'environment: uat' "$harness"
grep -Fq 'TargetFile:"$RUNNER_TEMP/uat-v17-schema.dacpac"' "$harness"
grep -Fq 'session-options.sql' "$harness"
grep -Fq -- '-C -I -b' "$harness"
! grep -Fq 'uat-migration' "$harness"
! grep -Fq 'AZURE_MIGRATION_CLIENT_ID' "$harness"

grep -Fq 'UX_Teams_Organization_TeamCode definition differs' database/development/v1.8.0/compat/1800_001.dev.sql

# D-B Work 1: development 1800_005 must be exactly the canonical migration.
adapter_005=database/development/v1.8.0/compat/1800_005.dev.sql
source_005="$(jq -r '.entries[] | select(.migration=="1800_005") | .source' "$manifest")"
cmp -s "$source_005" "$adapter_005" || { echo '1800_005 development adapter must remain byte-identical to canonical Up.sql.' >&2; exit 1; }
for guard in "AFTER INSERT, UPDATE, DELETE" "ORDER BY ResourceName COLLATE Latin1_General_100_BIN2 ASC" "@LockMode=N''Exclusive''" "@LockOwner=N''Transaction''" "@LockTimeout=10000" "WITH (UPDLOCK,HOLDLOCK)" "MileageRateRules physical DELETE is prohibited" "MileageRate canonical VehicleType final invariant failed" "MileageRate duplicate active EffectiveFrom final invariant failed" "MileageRate derived EffectiveTo final invariant failed" "MileageRate terminal EffectiveTo final invariant failed"; do
  grep -Fq "$guard" "$adapter_005" || { echo "1800_005 Work-1 contract missing: $guard" >&2; exit 1; }
done
if grep -Eiq 'ISNULL\([[:space:]]*(r\.|a\.)?OrganizationId[[:space:]]*,[[:space:]]*-1[[:space:]]*\)' "$adapter_005"; then
  echo '1800_005 sentinel Organization series identity detected.' >&2; exit 1
fi

for migration in 1800_001 1800_002 1800_004 1800_007; do
  grep -Fq 'EXEC sys.sp_executesql' "database/development/v1.8.0/compat/${migration}.dev.sql"
done

node scripts/scan-sql-add-column-order.mjs "${adapters[@]}" >/dev/null

echo 'v1.8 development compatibility static validation passed.'
