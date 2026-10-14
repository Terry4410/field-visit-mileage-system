#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
files=(
  "docs/release/optionc-canonical-sql-draft/1800_011/Up.sql"
  "docs/release/optionc-canonical-sql-draft/1800_011/Verify.sql"
  "docs/release/optionc-canonical-sql-draft/1800_012/Up.sql"
  "docs/release/optionc-canonical-sql-draft/1800_012/Verify.sql"
  "docs/release/optionc-canonical-sql-draft/1800_014/Up.sql"
  "docs/release/optionc-canonical-sql-draft/1800_014/Verify.sql"
  "docs/release/optionc-canonical-sql-draft/1800_014/Backfill.sql"
  "docs/release/optionc-canonical-sql-draft/merge/LocationMergeDetails.disposable.sql"
  "docs/release/optionc-canonical-sql-draft/audit/StageEvidenceAuditEvents.disposable.sql"
  "docs/release/optionc-canonical-sql-draft/preflight/UAT_ReadOnly_Preflight.sql"
)
for rel in "${files[@]}"; do
  test -f "$root/$rel"
  sha256sum "$root/$rel" | awk -v path="$rel" '{print path "|" $1 "|" $2}'
done
if find "$root/database/migrations" -maxdepth 1 \( -type f -o -type d \) | grep -E '/(013|1800_013)(/|$)' >/dev/null; then
  echo "reserved migration 013 must remain untouched" >&2
  exit 1
fi
