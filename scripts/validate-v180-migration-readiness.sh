#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

up_script="database/migrations/1800_001_organization_center_team_lifecycle/Up.sql"
verify_script="database/migrations/1800_001_organization_center_team_lifecycle/Verify.sql"
up_script_002="database/migrations/1800_002_person_employment_role_membership/Up.sql"
up_script_003="database/migrations/1800_003_deployment_sites/Up.sql"
draft="docs/workflows/azure-sql-uat-migration-1800-001.draft.yml"
workflow=".github/workflows/azure-sql-uat-migration-1800-001.yml"
recovery_preflight_workflow=".github/workflows/azure-sql-migration-identity-readonly-uat-smoke.yml"
grant_script="database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_001.sql"
permission_verify_script="database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_001.sql"
revoke_script="database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_001.sql"

expected_up_sha="8e21073a1cd8bf239ad20504e670ea8c0df69cf02f630bb92f473465fbd6726c"
expected_verify_sha="4dfa7f571c1946e6a034bcb9acb9a3e7504cfd050bcfdfbb6426a091a12e1026"
expected_up_sha_002="82c9e06743af010fdde97d77b7fdb3455ce51b1f1bf0c71c61174b812d2a4f87"
expected_up_sha_003="e8dea828f29dc64c99b59a042be75580c08c4dfbc9f681223d30950dad3f4aab"
checkout_sha="11d5960a326750d5838078e36cf38b85af677262"
azure_login_sha="7184910d9eb2b1c5e48f7073824a90609bb9b6d6"

for required_file in \
  "${up_script}" \
  "${verify_script}" \
  "${up_script_002}" \
  "${up_script_003}" \
  "${draft}" \
  "${workflow}" \
  "${recovery_preflight_workflow}" \
  "${grant_script}" \
  "${permission_verify_script}" \
  "${revoke_script}" \
  "docs/release/POST-UAT-v1.8.0-1800-001-PERMISSION-REVIEW.md" \
  "docs/release/POST-UAT-v1.8.0-1800-001-RECOVERY-GATE.md"; do
  [[ -f "${required_file}" ]] || { echo "Missing required file: ${required_file}" >&2; exit 1; }
done

actual_up_sha="$(sha256sum "${up_script}" | awk '{print $1}')"
actual_verify_sha="$(sha256sum "${verify_script}" | awk '{print $1}')"
actual_up_sha_002="$(sha256sum "${up_script_002}" | awk '{print $1}')"
actual_up_sha_003="$(sha256sum "${up_script_003}" | awk '{print $1}')"
[[ "${actual_up_sha}" == "${expected_up_sha}" ]] || { echo "Unexpected Up.sql SHA-256" >&2; exit 1; }
[[ "${actual_verify_sha}" == "${expected_verify_sha}" ]] || { echo "Unexpected Verify.sql SHA-256" >&2; exit 1; }
[[ "${actual_up_sha_002}" == "${expected_up_sha_002}" ]] || { echo "Unexpected 1800_002 Up.sql SHA-256" >&2; exit 1; }
[[ "${actual_up_sha_003}" == "${expected_up_sha_003}" ]] || { echo "Unexpected 1800_003 Up.sql SHA-256" >&2; exit 1; }

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

python3 - "${up_script}" "${up_script_002}" "${up_script_003}" <<'PY'
import re
import sys
from pathlib import Path

scripts = {
    "1800_001": Path(sys.argv[1]).read_text(encoding="utf-8"),
    "1800_002": Path(sys.argv[2]).read_text(encoding="utf-8"),
    "1800_003": Path(sys.argv[3]).read_text(encoding="utf-8"),
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def require_dynamic_fragments(stage: str, fragments: tuple[str, ...]) -> None:
    text = scripts[stage]
    for fragment in fragments:
        require(fragment in text, f"{stage} deferred-binding guard missing: {fragment}")


def reject_static_patterns(stage: str, patterns: tuple[tuple[str, str], ...]) -> None:
    text = scripts[stage]
    for pattern, label in patterns:
        require(
            re.search(pattern, text) is None,
            f"{stage} static binding regression detected: {label}",
        )


require_dynamic_fragments(
    "1800_001",
    (
        "EXEC sys.sp_executesql N'\n        ALTER TABLE dbo.Organizations WITH CHECK ADD",
        "EXEC sys.sp_executesql N'\n        ALTER TABLE dbo.Teams WITH CHECK ADD",
        "EXEC sys.sp_executesql N'\n        CREATE INDEX IX_Teams_Organization_Effective",
    ),
)
reject_static_patterns(
    "1800_001",
    (
        (r"(?m)^    ALTER TABLE dbo\.Organizations WITH CHECK ADD$", "Organizations FK outer-batch binding"),
        (r"(?m)^    ALTER TABLE dbo\.Teams WITH CHECK ADD$", "Teams constraint/FK outer-batch binding"),
        (r"(?m)^    CREATE INDEX IX_Teams_Organization_Effective$", "Teams effective-index outer-batch binding"),
    ),
)

require(
    scripts["1800_001"].lower().count("and ignore_dup_key = 0") == 1,
    "1800_001 equivalent-index guard must require IGNORE_DUP_KEY=OFF exactly once",
)

require_dynamic_fragments(
    "1800_002",
    (
        "EXEC sys.sp_executesql N'\n        ALTER TABLE dbo.UserIdentityProfiles WITH CHECK ADD",
        "        CREATE UNIQUE INDEX UX_UserIdentityProfiles_Employment",
        "        UPDATE p\n           SET EmploymentId = e.EmploymentId,",
        "EXEC sys.sp_executesql N'\n        ALTER TABLE dbo.VisitTrips WITH CHECK ADD",
        "        CREATE INDEX IX_VisitTrips_Employment_VisitDate",
    ),
)
reject_static_patterns(
    "1800_002",
    (
        (r"(?m)^    ALTER TABLE dbo\.UserIdentityProfiles WITH CHECK ADD$", "UserIdentityProfiles FK outer-batch binding"),
        (r"(?m)^    CREATE UNIQUE INDEX UX_UserIdentityProfiles_Employment$", "UserIdentityProfiles index outer-batch binding"),
        (r"(?m)^    UPDATE p\n       SET EmploymentId = e\.EmploymentId,", "UserIdentityProfiles UPDATE outer-batch binding"),
        (r"(?m)^    ALTER TABLE dbo\.VisitTrips WITH CHECK ADD$", "VisitTrips Employment FK outer-batch binding"),
        (r"(?m)^    CREATE INDEX IX_VisitTrips_Employment_VisitDate$", "VisitTrips Employment index outer-batch binding"),
    ),
)

require_dynamic_fragments(
    "1800_003",
    (
        "EXEC sys.sp_executesql N'\n        ALTER TABLE dbo.VisitTrips WITH CHECK ADD",
        "            CONSTRAINT FK_VisitTrips_StartDeploymentSite FOREIGN KEY(StartDeploymentSiteId)",
        "            CONSTRAINT FK_VisitTrips_EndDeploymentSite FOREIGN KEY(EndDeploymentSiteId)",
    ),
)
reject_static_patterns(
    "1800_003",
    (
        (r"(?m)^    ALTER TABLE dbo\.VisitTrips WITH CHECK ADD$", "VisitTrips deployment-site FK outer-batch binding"),
    ),
)

stage_invariants = {
    "1800_001": ("1.7.0-008", "1.8.0-001", "SchemaMigrationDataBaselines", "HASHBYTES(N'SHA2_256'"),
    "1800_002": ("1.8.0-001", "1.8.0-002", "dbo.UserIdentityProfiles", "dbo.VisitTrips"),
    "1800_003": ("1.8.0-002", "1.8.0-003", "dbo.VisitTrips", "dbo.VisitTripSnapshots"),
}
for stage, markers in stage_invariants.items():
    text = scripts[stage]
    require(re.search(r"(?mi)^\s*GO\s*$", text) is None, f"{stage} must remain one outer batch; GO is forbidden")
    for marker, expected in (
        ("BEGIN TRANSACTION;", 1),
        ("COMMIT TRANSACTION;", 1),
        ("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", 1),
    ):
        actual = text.count(marker)
        require(actual == expected, f"{stage} transaction invariant failed for {marker!r}: expected {expected}, got {actual}")
    for marker in ("SET NOCOUNT ON;", "SET XACT_ABORT ON;", "sys.sp_getapplock", "FieldVisit.SchemaMigration", *markers):
        require(marker in text, f"{stage} safety invariant missing: {marker}")

require(scripts["1800_001"].count("EXEC sys.sp_executesql N'") == 4, "1800_001 expected exactly four deferred dynamic DDL units")
require(scripts["1800_002"].count("EXEC sys.sp_executesql N'") == 7, "1800_002 expected exactly seven deferred dynamic SQL units")
require(scripts["1800_003"].count("EXEC sys.sp_executesql N'") == 8, "1800_003 expected exactly eight deferred dynamic SQL units")

print("PASS 1800_001 deferred-binding+single-transaction static validation")
print("PASS 1800_002 deferred-binding+single-transaction static validation")
print("PASS 1800_003 deferred-binding+single-transaction static validation")
PY

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

grep -Fq -- 'environment: uat-migration' "${recovery_preflight_workflow}"
grep -Fq -- 'client-id: ${{ vars.AZURE_MIGRATION_CLIENT_ID }}' "${recovery_preflight_workflow}"
grep -Fq -- 'az sql db show' "${recovery_preflight_workflow}"
grep -Fq -- 'az sql db str-policy show' "${recovery_preflight_workflow}"
grep -Fq -- 'earliestRestoreDate:earliestRestoreDate' "${recovery_preflight_workflow}"
grep -Fq -- 'LatestSchemaVersion=' "${recovery_preflight_workflow}"
grep -Fq -- 'TargetVersion1800_001Count=' "${recovery_preflight_workflow}"
grep -Fq -- 'PartialObjectCount=' "${recovery_preflight_workflow}"
grep -Fq -- 'PartialColumnCount=' "${recovery_preflight_workflow}"
grep -Fq -- 'VisitTripsCount=' "${recovery_preflight_workflow}"
grep -Fq -- 'VisitTripSnapshotsCount=' "${recovery_preflight_workflow}"
grep -Fq -- 'VisitTripSnapshotStopsCount=' "${recovery_preflight_workflow}"
grep -Fq -- 'DatabaseStatusBeforeSql=' "${recovery_preflight_workflow}"
grep -Fq -- 'Capture post-query database status read-only' "${recovery_preflight_workflow}"

if grep -Eq -- '-InputFile|database/migrations/.+\.sql' "${recovery_preflight_workflow}"; then
  echo "Recovery preflight workflow must not execute a SQL file." >&2
  exit 1
fi
if grep -Eiq '(^|[[:space:]])(CREATE|ALTER|DROP|TRUNCATE|INSERT|UPDATE|DELETE|MERGE|EXEC(UTE)?)[[:space:]]' "${recovery_preflight_workflow}"; then
  echo "Recovery preflight workflow contains a non-read-only SQL verb." >&2
  exit 1
fi
if grep -Eiq 'az[[:space:]]+sql[[:space:]]+(db[[:space:]]+(restore|copy|update)|server[[:space:]]+firewall-rule)|(^|[[:space:]])(seed|deploy(ment)?)[[:space:]]' "${recovery_preflight_workflow}"; then
  echo "Recovery preflight workflow contains a forbidden Azure or application operation category." >&2
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
echo "1800_002 Up.sql SHA-256: ${actual_up_sha_002}"
echo "1800_003 Up.sql SHA-256: ${actual_up_sha_003}"
