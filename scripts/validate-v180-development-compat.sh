#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

manifest=database/development/v1.8.0/compat/manifest.json
harness=.github/workflows/v180-development-schema-harness.yml
[[ -f "$manifest" && -f "$harness" ]] || {
  echo 'Development compatibility manifest or harness is missing.' >&2
  exit 1
}

jq -e '
  .status == "development-only-migration-compatibility" and
  .productionUpgradePath == false and
  .azureExecutionAllowed == false and
  (.entries | length == 7) and
  ([.entries[].migration] == ["1800_001","1800_002","1800_003","1800_004","1800_005","1800_006","1800_007"])
' "$manifest" >/dev/null

session_options="$(jq -r '.sessionOptions' "$manifest")"
session_options_hash="$(jq -r '.sessionOptionsSha256' "$manifest")"
[[ -f "$session_options" && "$session_options_hash" != "TO_BE_FILLED" ]] || {
  echo 'Pinned SQL session-options file is missing or uninitialized.' >&2
  exit 1
}
[[ "$(sha256sum "$session_options" | awk '{print $1}')" == "$session_options_hash" ]] || {
  echo 'SQL session-options file drifted; compatibility review required.' >&2
  exit 1
}
for option in 'SET ANSI_NULLS ON;' 'SET ANSI_PADDING ON;' 'SET ANSI_WARNINGS ON;' \
  'SET ARITHABORT ON;' 'SET CONCAT_NULL_YIELDS_NULL ON;' \
  'SET QUOTED_IDENTIFIER ON;' 'SET NUMERIC_ROUNDABORT OFF;'; do
  grep -Fqx "$option" "$session_options" || {
    echo "Required SQL session option missing: $option" >&2
    exit 1
  }
done

while IFS=$'\t' read -r migration source source_hash verify verify_hash apply apply_hash; do
  for path in "$source" "$verify" "$apply"; do
    [[ -f "$path" ]] || { echo "$migration missing pinned file: $path" >&2; exit 1; }
  done
  [[ "$(sha256sum "$source" | awk '{print $1}')" == "$source_hash" ]] || {
    echo "$migration source Up.sql drifted; compatibility review required." >&2
    exit 1
  }
  [[ "$(sha256sum "$verify" | awk '{print $1}')" == "$verify_hash" ]] || {
    echo "$migration original Verify.sql drifted; compatibility review required." >&2
    exit 1
  }
  [[ "$(sha256sum "$apply" | awk '{print $1}')" == "$apply_hash" ]] || {
    echo "$migration development apply file drifted; manifest review required." >&2
    exit 1
  }
done < <(jq -r '.entries[] | [.migration,.source,.sourceSha256,.verify,.verifySha256,.developmentApply,.developmentApplySha256] | @tsv' "$manifest")

mapfile -t adapters < <(find database/development/v1.8.0/compat -maxdepth 1 -name '1800_*.dev.sql' | sort)
[[ "${#adapters[@]}" -eq 5 ]] || { echo 'Expected five reviewed development adapters.' >&2; exit 1; }

if grep -REIn --include='*.sql' -E \
  '(database\.windows\.net|db-fieldvisit-uat|sql-fieldvisit-jpe-uat|\bGRANT\b|\bDENY\b|ALTER[[:space:]]+ROLE|CREATE[[:space:]]+USER)' \
  database/development/v1.8.0/compat; then
  echo 'Compatibility SQL contains an Azure target or security-provisioning statement.' >&2
  exit 1
fi

mapfile -t workflow_refs < <(grep -RIl 'database/development/v1.8.0/compat' .github/workflows || true)
[[ "${#workflow_refs[@]}" -eq 1 && "${workflow_refs[0]}" == "$harness" ]] || {
  echo 'Development compatibility layer is referenced outside its disposable harness.' >&2
  printf '%s\n' "${workflow_refs[@]}" >&2
  exit 1
}

grep -Fq 'environment: uat' "$harness"
grep -Fq 'TargetFile:"$RUNNER_TEMP/uat-v17-schema.dacpac"' "$harness"
grep -Fq 'session-options.sql' "$harness"
grep -Fq -- '-C -I -b' "$harness"
! grep -Fq 'uat-migration' "$harness"
! grep -Fq 'AZURE_MIGRATION_CLIENT_ID' "$harness"

# Known predecessor hazards must remain explicitly guarded, rather than being
# silently hidden by a name-only object check.
grep -Fq 'UX_Teams_Organization_TeamCode definition differs' database/development/v1.8.0/compat/1800_001.dev.sql
vehicle_adapter=database/development/v1.8.0/compat/1800_005.dev.sql
for guard in \
  'RateVehicleRawDefinition' \
  'RateVehicleDefinition' \
  'is_disabled = 0 AND is_not_trusted = 0' \
  "vehicletype=''other''orvehicletype=''motorcycle''orvehicletype=''car''" \
  "VehicleType NOT IN (N'Motorcycle', N'Car') OR VehicleType IS NULL" \
  'definition is unrecognized' \
  'DROP CONSTRAINT CK_MileageRateRules_VehicleType' \
  'WITH CHECK ADD' \
  "CHECK (VehicleType IN (N'Motorcycle', N'Car'))" \
  'recreated VehicleType constraint is not enabled and trusted'; do
  grep -Fq "$guard" "$vehicle_adapter" || {
    echo "1800_005 safety guard missing: $guard" >&2
    exit 1
  }
done
for migration in 1800_001 1800_002 1800_004 1800_007; do
  grep -Fq 'EXEC sys.sp_executesql' "database/development/v1.8.0/compat/${migration}.dev.sql"
done

node scripts/scan-sql-add-column-order.mjs "${adapters[@]}" >/dev/null

echo 'v1.8 development compatibility static validation passed.'
